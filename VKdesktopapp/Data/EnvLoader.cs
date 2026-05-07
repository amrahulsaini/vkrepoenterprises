using System;
using System.IO;

namespace VRASDesktopApp.Data;

public static class EnvLoader
{
    // Look for a .env in the application folder or parent folders named VKdesktopapp\.env
    public static void LoadDotEnv()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        // Walk up directories from baseDir to find a .env file (either at root or in a VKdesktopapp subfolder)
        var dir = new DirectoryInfo(baseDir);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, ".env");
            if (File.Exists(candidate))
            {
                LoadFile(candidate);
                return;
            }

            var candidate2 = Path.Combine(dir.FullName, "VKdesktopapp", ".env");
            if (File.Exists(candidate2))
            {
                LoadFile(candidate2);
                return;
            }

            dir = dir.Parent;
        }
    }

    private static void LoadFile(string path)
    {
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;
            var idx = line.IndexOf('=');
            if (idx <= 0) continue;
            var key = line.Substring(0, idx).Trim();
            var val = line.Substring(idx + 1).Trim().Trim('"', '\'');
            if (Environment.GetEnvironmentVariable(key) == null)
                Environment.SetEnvironmentVariable(key, val);
        }
    }
}
