// Built-in drone routes. ACDREAM_DRONE_ROUTE picks a camera move by name, a
// town by name, or both as "town:move". ACDREAM_DRONE_ROUTE=list prints the
// names and exits. An ACDREAM_DRONE_PATH file always wins over a built-in move,
// and an ACDREAM_UI_PROBE_SCRIPT always wins over a built-in town.
using System.Reflection;

namespace AcDream.App.Rendering;

internal static class DroneRoutes
{
    const string Prefix = "AcDream.App.DroneRoutes.";
    const string DefaultTownMove = "orbit-high";

    // Town stops in @teleloc form: 0xLANDBLOCKCELL [x y z].
    static readonly Dictionary<string, (string Label, string Loc)> Towns = new(StringComparer.OrdinalIgnoreCase)
    {
        ["holtburg"] = ("Holtburg", "0xA9B40019 [84 7.1 95]"),
        ["yaraq"] = ("Yaraq", "0x7D64000D [31.9 104.6 13]"),
        ["shoushi"] = ("Shoushi", "0xDA55001D [84.8 99 21]"),
        ["arwic"] = ("Arwic", "0xC6A90009 [46.8 4.2 43]"),
        ["cragstone"] = ("Cragstone", "0xBB9F0040 [169.4 168.3 55]"),
        ["qalabar"] = ("Qalaba'r", "0x9722003A [168.4 24.6 103]"),
        ["zaikhal"] = ("Zaikhal", "0x80900013 [64.9 55.7 125]"),
        ["linvak"] = ("Linvak Tukal", "0xA21E001A [83 38 561.4]"),
        ["plateau"] = ("The Plateau", "0x49B70021 [100.1 20.8 239.6]"),
        ["eastwatch"] = ("Eastwatch", "0x49F00013 [70 70 171]"),
        ["tethana"] = ("Fort Tethana", "0x2681001D [77.7 108.1 241]"),
        ["kara"] = ("Kara", "0xBA170039 [181.2 3.2 168.7]"),
        ["bluespire"] = ("Bluespire", "0x21B00017 [48.2 165.9 1]"),
        ["greenspire"] = ("Greenspire", "0x2BB5003C [179 86.6 1]"),
        ["redspire"] = ("Redspire", "0x17B2002A [132.6 25.8 45]"),
        ["ayanbaqur"] = ("Ayan Baqur", "0x1133001F [88.1 166.2 65.8]"),
        ["hebianto"] = ("Hebian-to", "0xE64E002F [138.3 161.9 21]"),
        ["sawato"] = ("Sawato", "0xC95B0001 [14.8 0.3 13]"),
        ["baishi"] = ("Baishi", "0xCE410007 [12.6 152.8 56]"),
        ["aljalima"] = ("Al-Jalima", "0x8588002C [120.4 95.5 91]"),
        ["neydisa"] = ("Neydisa Castle", "0x95D60033 [146.9 71.3 100.8]"),
        ["timaru"] = ("Timaru", "0x1DB60016 [71.4 134.3 91]"),
        ["stonehold"] = ("Stonehold", "0x64D5000B [30 50 79]"),
        ["banditcastle"] = ("Bandit Castle", "0xBDD00006 [16.9 120.5 116]"),
        ["asheron"] = ("Asheron", "0xC39F001B [66 56 9]"),
    };

    static IEnumerable<string> Moves() =>
        typeof(DroneRoutes).Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(Prefix, StringComparison.Ordinal) && n.EndsWith(".path", StringComparison.Ordinal))
            .Select(n => n[Prefix.Length..^".path".Length])
            .OrderBy(n => n, StringComparer.Ordinal);

    /// <summary>
    /// Reads ACDREAM_DRONE_ROUTE and sets ACDREAM_DRONE_PATH and
    /// ACDREAM_UI_PROBE_SCRIPT to temp files. Call before anything reads them.
    /// Returns false when the program should exit (list, or an unknown name).
    /// </summary>
    public static bool Apply()
    {
        var route = Environment.GetEnvironmentVariable("ACDREAM_DRONE_ROUTE")?.Trim();
        if (string.IsNullOrEmpty(route)) return true;

        if (route.Equals("list", StringComparison.OrdinalIgnoreCase))
        {
            PrintList();
            return false;
        }

        string? town = null, move = null;
        foreach (var part in route.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Towns.ContainsKey(part)) town = part;
            else if (Moves().Contains(part, StringComparer.OrdinalIgnoreCase)) move = part.ToLowerInvariant();
            else
            {
                Console.WriteLine($"[drone] unknown route '{part}'. Use ACDREAM_DRONE_ROUTE=list to see the names.");
                return false;
            }
        }
        if (town is not null && move is null) move = DefaultTownMove;

        var dir = Path.Combine(Path.GetTempPath(), "acdream-drone");
        Directory.CreateDirectory(dir);

        if (move is not null)
        {
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ACDREAM_DRONE_PATH")))
                Console.WriteLine($"[drone] ACDREAM_DRONE_PATH is set, ignoring built-in move '{move}'");
            else
            {
                using var s = typeof(DroneRoutes).Assembly.GetManifestResourceStream(Prefix + move + ".path")!;
                using var r = new StreamReader(s);
                var file = Path.Combine(dir, move + ".path");
                File.WriteAllText(file, r.ReadToEnd());
                Environment.SetEnvironmentVariable("ACDREAM_DRONE_PATH", file);
                Console.WriteLine($"[drone] built-in move '{move}'");
            }
            if (Environment.GetEnvironmentVariable("ACDREAM_DRONE_CAM") != "1")
                Environment.SetEnvironmentVariable("ACDREAM_DRONE_CAM", "1");
        }

        if (town is not null)
        {
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ACDREAM_UI_PROBE_SCRIPT")))
                Console.WriteLine($"[drone] ACDREAM_UI_PROBE_SCRIPT is set, ignoring built-in town '{town}'");
            else
            {
                var (label, loc) = Towns[town];
                var file = Path.Combine(dir, "probe-" + town.ToLowerInvariant() + ".txt");
                File.WriteAllLines(file, new[]
                {
                    "sleep 30000",
                    "command @cloak on",
                    "sleep 12000",
                    "command @teleloc " + loc,
                });
                Environment.SetEnvironmentVariable("ACDREAM_UI_PROBE_SCRIPT", file);
                Console.WriteLine($"[drone] built-in town '{town}' ({label}) at {loc}");
            }
        }
        return true;
    }

    static void PrintList()
    {
        Console.WriteLine("Camera moves (ACDREAM_DRONE_ROUTE=<move>):");
        foreach (var m in Moves()) Console.WriteLine("  " + m);
        Console.WriteLine($"Towns (ACDREAM_DRONE_ROUTE=<town> or <town>:<move>, default move {DefaultTownMove}):");
        foreach (var (k, v) in Towns.OrderBy(t => t.Key, StringComparer.Ordinal))
            Console.WriteLine($"  {k,-14} {v.Label}");
    }
}
