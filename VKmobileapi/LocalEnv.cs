namespace VKmobileapi;

internal static class LocalEnv
{
    public static void LoadBestEffort()
    {
        foreach (var candidate in GetCandidatePaths())
        {
            if (!File.Exists(candidate)) continue;
            LoadFrom(candidate);
            return;
        }
    }

    private static IEnumerable<string> GetCandidatePaths()
    {
        var current = Directory.GetCurrentDirectory();
        var baseDir = AppContext.BaseDirectory;

        yield return Path.Combine(current, "db", ".env.local");
        yield return Path.Combine(current, "db", ".env");
        yield return Path.Combine(current, "..", "db", ".env.local");
        yield return Path.Combine(current, "..", "db", ".env");
        yield return Path.Combine(baseDir, "db", ".env.local");
        yield return Path.Combine(baseDir, "..", "..", "..", "..", "db", ".env.local");
        yield return Path.Combine(current, ".env");
        yield return Path.Combine(baseDir, ".env");
    }

    private static void LoadFrom(string path)
    {
        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#')) continue;

            var sep = line.IndexOf('=');
            if (sep <= 0) continue;

            var key   = line[..sep].Trim();
            var value = line[(sep + 1)..].Trim().Trim('"');

            if (string.IsNullOrWhiteSpace(key)) continue;
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(key))) continue;

            Environment.SetEnvironmentVariable(key, value);
        }
    }
}
