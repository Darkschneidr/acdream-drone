// DroneCam drone camera (drone fork). ACDREAM_DRONE_CAM=1 detaches the
// render camera from the player: the character stands where she is, the
// movement keys drive this camera instead of her (nothing goes to the server),
// and streaming follows the camera (tethered to the player's landblock).
// See the top-level README.
using System.Globalization;
using System.Numerics;

namespace AcDream.App.Rendering;

internal static class DroneCam
{
    public static readonly bool Enabled =
        Environment.GetEnvironmentVariable("ACDREAM_DRONE_CAM") == "1";

    public static bool Active { get; private set; }
    /// <summary>Player position (render space) the drone was anchored at.</summary>
    public static Vector3 Anchor { get; private set; }
    public static Func<float, float, float?>? TerrainZ { get; set; }

    /// <summary>Closes the window. Closing logs the character off, then disconnects.</summary>
    public static Action? RequestClose { get; set; }
    /// <summary>ACDREAM_DRONE_KEEP_OPEN=1 leaves the client running after a route ends.</summary>
    static readonly bool KeepOpen = Environment.GetEnvironmentVariable("ACDREAM_DRONE_KEEP_OPEN") == "1";
    /// <summary>Set by a town route: the client only closes after its probe script has run.</summary>
    public static bool ExpectTeleport { get; set; }
    /// <summary>Set when the UI probe script (cloak and teleport) has finished.</summary>
    public static volatile bool ProbeDone;
    static bool _pathDone, _closeRequested;

    static readonly float Speed = F("ACDREAM_DRONE_SPEED", 10f);
    static readonly float Boost = F("ACDREAM_DRONE_BOOST", 40f);
    static readonly float TurnRate = F("ACDREAM_DRONE_TURN", 45f) * MathF.PI / 180f;
    static readonly float MinAgl = F("ACDREAM_DRONE_MIN_AGL", 2f);
    public static readonly int Tether = (int)F("ACDREAM_DRONE_TETHER", 2f);
    static readonly string? PathFile = Environment.GetEnvironmentVariable("ACDREAM_DRONE_PATH");

    static readonly float Bank = F("ACDREAM_DRONE_BANK", 0f) * MathF.PI / 180f;
    static float _bank, _pathRoll;
    static float AngleDelta(float a, float b)
    {
        float d = (b - a) % (2 * MathF.PI);
        if (d > MathF.PI) d -= 2 * MathF.PI;
        if (d < -MathF.PI) d += 2 * MathF.PI;
        return d;
    }

    static DronePath? _path;
    static double _pathT;
    static DateTime _pathStamp;
    static double _pathCheck;
    static double _logT;

    static float F(string name, float dflt) =>
        float.TryParse(Environment.GetEnvironmentVariable(name), NumberStyles.Float,
            CultureInfo.InvariantCulture, out var v) ? v : dflt;

    /// <summary>Anchor at the player; start pose from ACDREAM_DRONE_START "dx,dy,dz,yawDeg,pitchDeg" (relative to her) or 8 m up behind her.</summary>
    public static void Init(FlyCamera fly, Vector3 player, float playerYaw)
    {
        Anchor = player;
        var s = Environment.GetEnvironmentVariable("ACDREAM_DRONE_START");
        if (!string.IsNullOrWhiteSpace(s))
        {
            var c = s.Split(',').Select(v => float.Parse(v, CultureInfo.InvariantCulture)).ToArray();
            fly.Position = player + new Vector3(c[0], c[1], c[2]);
            fly.Yaw = c.Length > 3 ? c[3] * MathF.PI / 180f : playerYaw;
            fly.Pitch = c.Length > 4 ? c[4] * MathF.PI / 180f : -0.25f;
        }
        else
        {
            var back = new Vector3(MathF.Cos(playerYaw), MathF.Sin(playerYaw), 0f);
            fly.Position = player - back * 10f + new Vector3(0, 0, 8f);
            fly.Yaw = playerYaw;
            fly.Pitch = -0.35f;
        }
        LoadPath(force: true);
        Active = true;
        Console.WriteLine(FormattableString.Invariant(
            $"[drone] on anchor=({player.X:F1},{player.Y:F1},{player.Z:F1}) path={(_path is null ? "none" : PathFile)}"));
    }

    public static void InitAtCamera(FlyCamera fly)
    {
        Anchor = fly.Position;
        if (Environment.GetEnvironmentVariable("ACDREAM_DRONE_FOLLOW_SIM") == "1")
        {
            // Offline test: a simulated runner jogging a 40 m circle at 5 m/s on the terrain.
            var c = Anchor; var t0 = DateTime.UtcNow;
            DroneFollow.FindTarget = _ =>
            {
                float t = (float)(DateTime.UtcNow - t0).TotalSeconds * (5f / 40f);
                var p = new Vector3(c.X + 40 * MathF.Cos(t), c.Y + 40 * MathF.Sin(t), 0);
                p.Z = (TerrainZ?.Invoke(p.X, p.Y) ?? c.Z - 40) ;
                return (p, t + MathF.PI / 2);
            };
            DroneFollow.HandleCommand("sim", "follow me");
        }
        LoadPath(force: true);
        Active = true;
        Console.WriteLine(FormattableString.Invariant(
            $"[drone] on (fly) anchor=({Anchor.X:F1},{Anchor.Y:F1},{Anchor.Z:F1})"));
    }

    /// <summary>Teleport/portal space: drop the anchor, re-init on arrival.</summary>
    public static void Suspend()
    {
        if (Active) Console.WriteLine("[drone] suspended (portal space)");
        Active = false;
    }

    static void LoadPath(bool force)
    {
        if (string.IsNullOrWhiteSpace(PathFile)) return;
        try
        {
            if (!File.Exists(PathFile)) { _path = null; return; }
            var stamp = File.GetLastWriteTimeUtc(PathFile);
            if (!force && stamp == _pathStamp) return;
            _pathStamp = stamp;
            _path = DronePath.Parse(File.ReadAllLines(PathFile));
            _pathT = -_path.Delay;
            _pathDone = false;
            Console.WriteLine(FormattableString.Invariant(
                $"[drone] path loaded: {_path.Count} segments, {_path.Duration:F1}s, loop={_path.Loop}"));
        }
        catch (Exception e)
        {
            Console.WriteLine($"[drone] path error: {e.Message}");
            _path = null;
        }
    }

    static void TryClose()
    {
        if (_closeRequested || KeepOpen || RequestClose is null) return;
        if (ExpectTeleport && !ProbeDone) return;
        _closeRequested = true;
        Console.WriteLine("[drone] route finished, closing (set ACDREAM_DRONE_KEEP_OPEN=1 to stay)");
        RequestClose();
    }

    public readonly record struct Keys(bool Fwd, bool Back, bool StrafeL, bool StrafeR,
        bool TurnL, bool TurnR, bool Up, bool Down, bool PitchUp, bool PitchDown, bool Fast);

    public static void Update(FlyCamera fly, double dt, Keys k)
    {
        if (_pathDone) TryClose();
        _pathCheck += dt;
        if (_pathCheck > 1.0) { _pathCheck = 0; LoadPath(force: false); }

        float yawBefore = fly.Yaw;
        _pathRoll = 0f;
        if (DroneFollow.Tick(fly, dt))
        {
        }
        else if (_path is { } path && (path.Loop || _pathT <= path.Duration))
        {
            _pathT += dt;
            if (_pathT >= 0)
            {
                double t = path.Loop ? _pathT % path.Duration : Math.Min(_pathT, path.Duration);
                var (pos, yaw, pitch) = path.Sample(t, Anchor);
                fly.Position = pos; fly.Yaw = yaw; fly.Pitch = pitch;
                _pathRoll = path.SampleRoll(t);
                if (!path.Loop && _pathT > path.Duration)
                {
                    Console.WriteLine("[drone] path done");
                    _pathDone = true;
                    TryClose();
                }
            }
        }
        else
        {
            float step = (k.Fast ? Boost : Speed) * (float)dt;
            float turn = TurnRate * (float)dt;
            var fwd = new Vector3(MathF.Cos(fly.Yaw), MathF.Sin(fly.Yaw), 0f);
            var right = new Vector3(MathF.Sin(fly.Yaw), -MathF.Cos(fly.Yaw), 0f);
            var p = fly.Position;
            if (k.Fwd) p += fwd * step;
            if (k.Back) p -= fwd * step;
            if (k.StrafeL) p -= right * step;
            if (k.StrafeR) p += right * step;
            if (k.Up) p.Z += step;
            if (k.Down) p.Z -= step;
            fly.Position = p;
            if (k.TurnL) fly.Yaw += turn;
            if (k.TurnR) fly.Yaw -= turn;
            if (k.PitchUp) fly.Pitch = Math.Clamp(fly.Pitch + turn, -1.55f, 1.55f);
            if (k.PitchDown) fly.Pitch = Math.Clamp(fly.Pitch - turn, -1.55f, 1.55f);
        }

        // Roll: path roll= plus optional auto-bank from yaw rate (ACDREAM_DRONE_BANK = degrees per 1 rad/s, capped 35).
        float yawRate = dt > 0 ? AngleDelta(yawBefore, fly.Yaw) / (float)dt : 0f;
        float bankTarget = Math.Clamp(-yawRate * Bank, -0.61f, 0.61f);
        _bank += (bankTarget - _bank) * (1f - MathF.Exp(-3f * (float)dt));
        fly.Roll = _pathRoll + _bank;

        // Height clamp only (no collision).
        if (TerrainZ?.Invoke(fly.Position.X, fly.Position.Y) is { } ground
            && fly.Position.Z < ground + MinAgl)
            fly.Position = fly.Position with { Z = ground + MinAgl };

        _logT += dt;
        if (_logT > 5.0)
        {
            _logT = 0;
            var r = fly.Position - Anchor;
            Console.WriteLine(FormattableString.Invariant(
                $"[drone] rel=({r.X:F1},{r.Y:F1},{r.Z:F1}) yaw={fly.Yaw * 180 / MathF.PI:F0} pitch={fly.Pitch * 180 / MathF.PI:F0}"));
        }
    }
}

/// <summary>
/// Path file: one directive per line, '#' comments. Coordinates are metres
/// relative to the anchor (player position at drone start) unless "abs" is set;
/// yaw 0 = +X (east), 90 = +Y (north); pitch negative looks down.
///   delay S                         wait S seconds before flying (let terrain stream)
///   loop                            repeat forever (else hold the last pose)
///   abs                             coordinates are raw client-local metres
///   start X Y Z YAW PITCH           initial pose (default: first wp)
///   wp X Y Z YAW PITCH DUR [ease|linear]        fly to pose over DUR seconds
///   look X Y Z TX TY TZ DUR [ease|linear]       fly to X Y Z while looking at TX TY TZ
///   orbit CX CY CZ R H FROMDEG SWEEPDEG DUR [ease|linear]
///                                   circle centre C at radius R, height H above CZ,
///                                   from compass angle FROMDEG sweeping SWEEPDEG, looking at C
///   hold DUR                        stay put
/// Waypoint chains use Catmull-Rom position splines; "ease" (default) is
/// smoothstep on the segment's time, "linear" is constant speed.
/// </summary>
internal sealed class DronePath
{
    abstract record Seg(double Dur, bool Ease)
    {
        public abstract (Vector3 P, float Yaw, float Pitch) At(double u, Vector3 o, (Vector3 P, float Yaw, float Pitch) prev, Vector3 before, Vector3 after);
        public abstract Vector3 End(Vector3 o);
    }
    sealed record Pose(Vector3 P, float Yaw, float Pitch, Vector3? Target, double D, bool E) : Seg(D, E)
    {
        public override Vector3 End(Vector3 o) => o + P;
        public override (Vector3 P, float Yaw, float Pitch) At(double u, Vector3 o, (Vector3 P, float Yaw, float Pitch) prev, Vector3 before, Vector3 after)
        {
            float s = (float)u;
            var p = CatmullRom(before, prev.P, o + P, after, s);
            float yaw, pitch;
            if (Target is { } t) (yaw, pitch) = LookAt(p, o + t);
            else (yaw, pitch) = (LerpAngle(prev.Yaw, Yaw, s), prev.Pitch + (Pitch - prev.Pitch) * s);
            return (p, yaw, pitch);
        }
    }
    sealed record Orbit(Vector3 C, float R, float H, float From, float Sweep, double D, bool E) : Seg(D, E)
    {
        public Vector3 PosAt(float s, Vector3 o)
        {
            float a = (From + Sweep * s) * MathF.PI / 180f;
            return o + C + new Vector3(MathF.Cos(a) * R, MathF.Sin(a) * R, H);
        }
        public override Vector3 End(Vector3 o) => PosAt(1, o);
        public override (Vector3 P, float Yaw, float Pitch) At(double u, Vector3 o, (Vector3 P, float Yaw, float Pitch) prev, Vector3 before, Vector3 after)
        {
            var p = PosAt((float)u, o);
            var (yaw, pitch) = LookAt(p, o + C);
            return (p, yaw, pitch);
        }
    }
    sealed record Hold(double D) : Seg(D, false)
    {
        public override Vector3 End(Vector3 o) => throw new InvalidOperationException();
        public override (Vector3 P, float Yaw, float Pitch) At(double u, Vector3 o, (Vector3 P, float Yaw, float Pitch) prev, Vector3 b, Vector3 a) => prev;
    }

    readonly List<Seg> _segs = new();
    readonly List<float?> _rolls = new();
    (Vector3 P, float Yaw, float Pitch)? _start;
    public bool Loop { get; private set; }
    public bool Abs { get; private set; }
    public double Delay { get; private set; }
    public int Count => _segs.Count;
    public double Duration => Math.Max(0.001, _segs.Sum(s => s.Dur));

    static float D2R(float d) => d * MathF.PI / 180f;

    public static DronePath Parse(IEnumerable<string> lines)
    {
        var p = new DronePath();
        foreach (var raw in lines)
        {
            var line = raw.Split('#')[0].Trim();
            if (line.Length == 0) continue;
            var all = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            float? roll = null;
            foreach (var tok in all)
                if (tok.StartsWith("roll=", StringComparison.OrdinalIgnoreCase))
                    roll = float.Parse(tok[5..], CultureInfo.InvariantCulture);
            var t = all.Where(x => !x.StartsWith("roll=", StringComparison.OrdinalIgnoreCase)).ToArray();
            int before = p._segs.Count;
            float N(int i) => float.Parse(t[i], CultureInfo.InvariantCulture);
            bool E(int i) => !(t.Length > i && t[i].Equals("linear", StringComparison.OrdinalIgnoreCase));
            switch (t[0].ToLowerInvariant())
            {
                case "loop": p.Loop = true; break;
                case "abs": p.Abs = true; break;
                case "delay": p.Delay = N(1); break;
                case "start": p._start = (new Vector3(N(1), N(2), N(3)), D2R(N(4)), D2R(N(5))); break;
                case "wp": p._segs.Add(new Pose(new Vector3(N(1), N(2), N(3)), D2R(N(4)), D2R(N(5)), null, N(6), E(7))); break;
                case "look": p._segs.Add(new Pose(new Vector3(N(1), N(2), N(3)), 0, 0, new Vector3(N(4), N(5), N(6)), N(7), E(8))); break;
                case "orbit": p._segs.Add(new Orbit(new Vector3(N(1), N(2), N(3)), N(4), N(5), N(6), N(7), N(8), E(9))); break;
                case "hold": p._segs.Add(new Hold(N(1))); break;
                default: throw new FormatException($"unknown directive '{t[0]}'");
            }
            if (p._segs.Count > before) p._rolls.Add(roll is { } r ? D2R(r) : null);
        }
        if (p._segs.Count == 0) throw new FormatException("no segments");
        return p;
    }

    public (Vector3, float, float) Sample(double t, Vector3 anchor)
    {
        var o = Abs ? Vector3.Zero : anchor;
        // Walk segments carrying the previous pose (pose at the end of each segment).
        var prev = _start is { } st ? (o + st.P, st.Yaw, st.Pitch) : First(o);
        var prevPrevP = prev.Item1;
        for (int i = 0; i < _segs.Count; i++)
        {
            var seg = _segs[i];
            Vector3 after = NextPos(i, o, seg);
            if (t <= seg.Dur || i == _segs.Count - 1)
            {
                double u = Math.Clamp(t / seg.Dur, 0, 1);
                if (seg.Ease) u = u * u * (3 - 2 * u);
                return seg.At(u, o, prev, prevPrevP, after);
            }
            t -= seg.Dur;
            var end = seg.At(1, o, prev, prevPrevP, after);
            prevPrevP = prev.Item1;
            prev = end;
        }
        return prev;
    }

    /// <summary>Roll (radians) at time t: each segment eases from the previous roll to its roll= value (default: keep).</summary>
    public float SampleRoll(double t)
    {
        float prev = 0f;
        for (int i = 0; i < _segs.Count; i++)
        {
            float target = _rolls[i] ?? prev;
            if (t <= _segs[i].Dur || i == _segs.Count - 1)
            {
                double u = Math.Clamp(t / _segs[i].Dur, 0, 1);
                u = u * u * (3 - 2 * u);
                return prev + (target - prev) * (float)u;
            }
            t -= _segs[i].Dur;
            prev = target;
        }
        return prev;
    }

    (Vector3, float, float) First(Vector3 o)
    {
        foreach (var s in _segs)
        {
            if (s is Pose ps) { var p = o + ps.P; var (y, pi) = ps.Target is { } tg ? LookAt(p, o + tg) : (ps.Yaw, ps.Pitch); return (p, y, pi); }
            if (s is Orbit ob) { var p = ob.PosAt(0, o); var (y, pi) = LookAt(p, o + ob.C); return (p, y, pi); }
        }
        return (o, 0, 0);
    }

    Vector3 NextPos(int i, Vector3 o, Seg cur)
    {
        var end = cur is Hold ? Vector3.Zero : cur.End(o);
        for (int j = i + 1; j < _segs.Count; j++)
            if (_segs[j] is not Hold) return _segs[j] is Orbit ob ? ob.PosAt(0, o) : _segs[j].End(o);
        return end;
    }

    static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t, t3 = t2 * t;
        return 0.5f * (2 * p1 + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (-p0 + 3 * p1 - 3 * p2 + p3) * t3);
    }

    static (float, float) LookAt(Vector3 from, Vector3 to)
    {
        var d = to - from;
        return (MathF.Atan2(d.Y, d.X), MathF.Atan2(d.Z, MathF.Sqrt(d.X * d.X + d.Y * d.Y)));
    }

    static float LerpAngle(float a, float b, float s)
    {
        float d = (b - a) % (2 * MathF.PI);
        if (d > MathF.PI) d -= 2 * MathF.PI;
        if (d < -MathF.PI) d += 2 * MathF.PI;
        return a + d * s;
    }
}
