// DroneCam drone FOLLOW mode (drone fork). See the top-level README.
// Trails a named player with a critically damped spring; optional slow orbit drift; tell control from an
// allowlist; keeps the camera character's body near the target with the admin "@teleto <name>" command. Never chats.
using System.Globalization;
using System.Numerics;
using AcDream.Core.Chat;

namespace AcDream.App.Rendering;

internal static class DroneFollow
{
    static float F(string n, float d) => float.TryParse(Environment.GetEnvironmentVariable(n), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : d;

    static readonly float Back = F("ACDREAM_DRONE_FOLLOW_BACK", 8f);
    static readonly float Up = F("ACDREAM_DRONE_FOLLOW_UP", 5f);
    static readonly float Side = F("ACDREAM_DRONE_FOLLOW_SIDE", 0f);
    static readonly float Smooth = F("ACDREAM_DRONE_FOLLOW_SMOOTH", 1.2f);     // spring time constant, s
    static readonly float Drift = F("ACDREAM_DRONE_FOLLOW_DRIFT", 0f) * MathF.PI / 180f; // deg/s orbit drift
    static readonly float DriftSwing = F("ACDREAM_DRONE_FOLLOW_SWING", 0f) * MathF.PI / 180f; // +-deg sway instead of full orbit
    static readonly float TeleDist = F("ACDREAM_DRONE_FOLLOW_TELE_M", 150f);
    static readonly float TeleCooldown = F("ACDREAM_DRONE_FOLLOW_TELE_COOLDOWN", 20f);
    static readonly float SnapEvery = F("ACDREAM_DRONE_FOLLOW_SNAP_SEC", 0f);
    static readonly HashSet<string> Allow = new(
        (Environment.GetEnvironmentVariable("ACDREAM_DRONE_FOLLOW_ALLOW") ?? "")
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        StringComparer.OrdinalIgnoreCase);

    static volatile string? _target = Environment.GetEnvironmentVariable("ACDREAM_DRONE_FOLLOW") is { Length: > 0 } n ? n : null;
    static volatile bool _snapRequested;

    /// <summary>Target lookup by character name -> render-space position + heading (set by composition).</summary>
    public static Func<string, (Vector3 Pos, float Heading)?>? FindTarget { get; set; }
    /// <summary>Admin command submission ("@..." only; set by the automation runner).</summary>
    public static Action<string>? SubmitCommand { get; set; }
    public static Func<string, bool>? Screenshot { get; set; }
    /// <summary>the camera character's own body position (render space), updated every frame.</summary>
    public static Vector3? BodyPosition { get; set; }
    public static string? OwnName { get; set; }

    public static string? Target => _target;

    static Vector3 _vel, _lastTp;
    static bool _have;
    static float _drift, _heading, _lookYaw, _lookPitch, _snapT, _snapN;
    static double _clock, _lastTele = -1e9, _lostSince = -1;

    public static void Attach(ChatLog chat)
    {
        chat.EntryAppended += OnChat;
        Console.WriteLine($"[drone-follow] tell control armed, allow={(Allow.Count == 0 ? "(none)" : string.Join(",", Allow))}" +
            (_target is null ? "" : $" initial target set"));
    }

    static void OnChat(ChatEntry e)
    {
        if (e.Kind != ChatKind.Tell || e.IsTellToSelf) return;
        if (string.IsNullOrWhiteSpace(e.Sender) || !Allow.Contains(e.Sender.Trim())) return;
        HandleCommand(e.Sender.Trim(), e.Text);
    }

    /// <summary>Exposed for tests/offline: apply a tell command from sender. Never replies.</summary>
    public static void HandleCommand(string sender, string text)
    {
        string t = text.Trim().TrimEnd('.', '!').ToLowerInvariant();
        if (t is "follow me" or "cam follow" or "follow")
        {
            _target = sender; _have = false; _lostSince = -1;
            Console.WriteLine("[drone-follow] follow started (tell)");
        }
        else if (t is "stop" or "cam stop" or "unfollow")
        {
            _target = null;
            Console.WriteLine("[drone-follow] stopped (tell)");
        }
        else if (t is "snap" or "cam snap")
            _snapRequested = true;
    }

    static void Submit(string cmd)
    {
        if (!cmd.StartsWith('@')) return; // never let anything become a chat line
        SubmitCommand?.Invoke(cmd);
    }

    /// <summary>Returns true when follow drove the camera this frame.</summary>
    public static bool Tick(FlyCamera fly, double dt)
    {
        _clock += dt;
        if (_snapRequested) { _snapRequested = false; Snap("tell"); }
        string? name = _target;
        if (name is null) return false;

        var found = FindTarget?.Invoke(name);
        if (found is null)
        {
            if (_lostSince < 0) _lostSince = _clock;
            // Target not in our view of the world: hop the body to them (cloaked admin teleport).
            if (_clock - _lostSince > 3 && _clock - _lastTele > TeleCooldown)
            {
                _lastTele = _clock; Console.WriteLine("[drone-follow] target not visible -> @teleto");
                Submit("@teleto " + name);
            }
            return true; // hold the camera where it is
        }
        _lostSince = -1;
        var (tp, heading) = found.Value;

        if (BodyPosition is { } body && Vector2.Distance(new(body.X, body.Y), new(tp.X, tp.Y)) > TeleDist
            && _clock - _lastTele > TeleCooldown)
        {
            _lastTele = _clock; Console.WriteLine("[drone-follow] target beyond tether -> @teleto");
            Submit("@teleto " + name);
        }

        // Prefer the direction of travel when moving (robust to facing conventions).
        if (_have && dt > 0)
        {
            var mv = new Vector2(tp.X - _lastTp.X, tp.Y - _lastTp.Y) / (float)dt;
            if (mv.Length() > 1.0f && mv.Length() < 60f) heading = MathF.Atan2(mv.Y, mv.X);
            else heading = _heading;
        }
        _lastTp = tp;
        if (!_have) { _heading = heading; _drift = 0; }
        _heading += AngleDelta(_heading, heading) * (1f - MathF.Exp(-(float)dt / 1.5f)); // lag the heading
        _drift = DriftSwing > 0 ? DriftSwing * MathF.Sin((float)_clock * MathF.Max(Drift, 0.05f)) : _drift + Drift * (float)dt;
        float a = _heading + _drift;
        var fwd = new Vector3(MathF.Cos(a), MathF.Sin(a), 0);
        var right = new Vector3(MathF.Sin(a), -MathF.Cos(a), 0);
        var desired = tp - fwd * Back + right * Side + new Vector3(0, 0, Up);

        if (!_have) { fly.Position = desired; _vel = Vector3.Zero; }
        else fly.Position = SmoothDamp(fly.Position, desired, ref _vel, Smooth, (float)dt);

        var look = tp + new Vector3(0, 0, 1.2f) - fly.Position;
        float wantYaw = MathF.Atan2(look.Y, look.X);
        float wantPitch = MathF.Atan2(look.Z, MathF.Sqrt(look.X * look.X + look.Y * look.Y));
        if (!_have) { _lookYaw = wantYaw; _lookPitch = wantPitch; _have = true; }
        float k = 1f - MathF.Exp(-(float)dt / 0.35f);
        _lookYaw += AngleDelta(_lookYaw, wantYaw) * k;
        _lookPitch += (wantPitch - _lookPitch) * k;
        fly.Yaw = _lookYaw; fly.Pitch = _lookPitch;

        if (SnapEvery > 0 && (_snapT += (float)dt) >= SnapEvery) { _snapT = 0; Snap("auto"); }
        return true;
    }

    static void Snap(string why)
    {
        string n = $"follow-{DateTime.Now:yyyyMMdd-HHmmss}-{_snapN++}";
        bool ok = Screenshot?.Invoke(n) ?? false;
        Console.WriteLine($"[drone-follow] snap {why} {n} ok={ok}");
    }

    // Critically damped spring (Game Programming Gems 4, SmoothDamp).
    static Vector3 SmoothDamp(Vector3 cur, Vector3 target, ref Vector3 vel, float smoothTime, float dt)
    {
        smoothTime = MathF.Max(0.05f, smoothTime);
        float omega = 2f / smoothTime, x = omega * dt;
        float exp = 1f / (1f + x + 0.48f * x * x + 0.235f * x * x * x);
        var change = cur - target;
        var temp = (vel + omega * change) * dt;
        vel = (vel - omega * temp) * exp;
        return target + (change + temp) * exp;
    }

    static float AngleDelta(float a, float b)
    {
        float d = (b - a) % (2 * MathF.PI);
        if (d > MathF.PI) d -= 2 * MathF.PI;
        if (d < -MathF.PI) d += 2 * MathF.PI;
        return d;
    }
}
