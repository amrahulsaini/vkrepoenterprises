using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace CRMRSDesktopApp;

internal static class GateAccess
{
    private static readonly TimeSpan Window = TimeSpan.FromDays(7);

    private static string Path_ => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CRMRS", "gates.dat");

    private static string Hash(string s)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s ?? "")));

    private static Dictionary<string, (string Hash, DateTime When)> Read()
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

    private static void Write(Dictionary<string, (string Hash, DateTime When)> map)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path_)!);
            File.WriteAllLines(Path_, map.Select(kv =>
                $"{kv.Key}|{kv.Value.Hash}|{kv.Value.When:O}"));
        }
        catch { }
    }

    public static bool RecentlyPassed(string gate, string requiredPassword)
    {
        var map = Read();
        if (!map.TryGetValue(gate, out var e)) return false;
        if (e.Hash != Hash(requiredPassword)) return false;
        return DateTime.UtcNow - e.When < Window;
    }

    public static void MarkPassed(string gate, string requiredPassword)
    {
        var map = Read();
        map[gate] = (Hash(requiredPassword), DateTime.UtcNow);
        Write(map);
    }

    public static void ClearAll()
    {
        try { if (File.Exists(Path_)) File.Delete(Path_); }
        catch { }
    }
}
