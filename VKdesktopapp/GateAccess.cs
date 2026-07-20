using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CRMRSDesktopApp;

/// Remembers that a mode password was accepted on this device, so it is not
/// asked for again for a week. Nothing password-derived is stored locally:
/// the stamp is an opaque fingerprint the server returns, which changes when
/// the password changes, so a change forces the prompt back immediately.
internal static class GateAccess
{
    private static readonly TimeSpan Window = TimeSpan.FromDays(7);

    private static string Path_ => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CRMRS", "gates.dat");

    private static Dictionary<string, (string Stamp, DateTime When)> Read()
    {
        var map = new Dictionary<string, (string, DateTime)>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!File.Exists(Path_)) return map;
            foreach (var line in File.ReadAllLines(Path_))
            {
                var p = line.Split('|');
                if (p.Length == 3 && DateTime.TryParse(p[2], null,
                        System.Globalization.DateTimeStyles.RoundtripKind, out var when))
                    map[p[0]] = (p[1], when);
            }
        }
        catch { }
        return map;
    }

    private static void Write(Dictionary<string, (string Stamp, DateTime When)> map)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path_)!);
            File.WriteAllLines(Path_, map.Select(kv => $"{kv.Key}|{kv.Value.Stamp}|{kv.Value.When:O}"));
        }
        catch { }
    }

    public static bool RecentlyPassed(string gate, string currentStamp)
    {
        if (string.IsNullOrEmpty(currentStamp)) return false;
        var map = Read();
        if (!map.TryGetValue(gate, out var e)) return false;
        if (!string.Equals(e.Stamp, currentStamp, StringComparison.Ordinal)) return false;
        return DateTime.UtcNow - e.When < Window;
    }

    public static void MarkPassed(string gate, string stamp)
    {
        if (string.IsNullOrEmpty(stamp)) return;
        var map = Read();
        map[gate] = (stamp, DateTime.UtcNow);
        Write(map);
    }

    public static void ClearAll()
    {
        try { if (File.Exists(Path_)) File.Delete(Path_); }
        catch { }
    }
}
