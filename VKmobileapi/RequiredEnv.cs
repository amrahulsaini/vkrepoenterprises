namespace VKmobileapi;

internal static class RequiredEnv
{
    public static string Get(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } v
            ? v
            : throw new InvalidOperationException($"{name} must be set (db/.env.local or a systemd drop-in).");
}
