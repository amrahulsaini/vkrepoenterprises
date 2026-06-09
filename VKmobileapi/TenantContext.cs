using System.Security.Cryptography;
using System.Text;
using MySqlConnector;

namespace VKmobileapi;

internal static class TenantContext
{
    private static readonly AsyncLocal<string?> _conn = new();
    private static readonly AsyncLocal<string?> _key  = new();

    public static string DefaultConn { get; set; } = "";
    public static string MysqlHost   { get; set; } = "127.0.0.1";
    public static int    MysqlPort   { get; set; } = 3306;

    public static string Conn { get => _conn.Value ?? DefaultConn; set => _conn.Value = value; }

    public static string Key { get => _key.Value ?? "default"; set => _key.Value = value; }

    public static void UseAgency(string slug)
    {
        Conn = BuildTenantConn(slug);
        Key  = slug;
    }

    public static string BuildTenantConn(string slug)
    {
        string dbName = "crmr_" + slug;
        string dbUser = "tu_" + slug;
        if (dbUser.Length > 32) dbUser = dbUser.Substring(0, 32);
        return new MySqlConnectionStringBuilder
        {
            Server   = MysqlHost,
            Port     = (uint)MysqlPort,
            UserID   = dbUser,
            Password = DeriveTenantPassword(slug),
            Database = dbName,
            SslMode  = MySqlSslMode.None,
            Pooling  = true,
            MaximumPoolSize       = 10,
            ConnectionTimeout     = 10,
            DefaultCommandTimeout = 30,
        }.ConnectionString;
    }

    private static readonly string TenantDbSecret =
        Environment.GetEnvironmentVariable("TENANT_DB_SECRET")
        ?? "crmrs-tenant-secret-rotate-me-2026";

    public static string DeriveTenantPassword(string slug)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(TenantDbSecret));
        var bytes = hmac.ComputeHash(Encoding.UTF8.GetBytes("tenant:" + slug));
        return "T1!" + Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').Substring(0, 25);
    }
}

internal static class MobileToken
{
    private static readonly byte[] Key = Encoding.UTF8.GetBytes(
        Environment.GetEnvironmentVariable("TENANT_DB_SECRET")
        ?? "crmrs-tenant-secret-rotate-me-2026");

    public static string Issue(string slug, int validDays = 90)
    {
        long exp = DateTimeOffset.UtcNow.AddDays(validDays).ToUnixTimeSeconds();
        var payload = Encoding.UTF8.GetBytes($"{slug}|{exp}");
        return "mt1." + B64(payload) + "." + B64(Sign(payload));
    }

    public static string? Verify(string? token)
    {
        try
        {
            if (string.IsNullOrEmpty(token) || !token.StartsWith("mt1.", StringComparison.Ordinal))
                return null;
            var p = token.Split('.');
            if (p.Length != 3) return null;
            var payload = UnB64(p[1]);
            if (!CryptographicOperations.FixedTimeEquals(UnB64(p[2]), Sign(payload))) return null;
            var f = Encoding.UTF8.GetString(payload).Split('|');
            if (f.Length != 2) return null;
            if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > long.Parse(f[1])) return null;
            return f[0];
        }
        catch { return null; }
    }

    private static byte[] Sign(byte[] data)
    {
        using var h = new HMACSHA256(Key);
        return h.ComputeHash(data);
    }

    private static string B64(byte[] b) =>
        Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] UnB64(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        s += (s.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };
        return Convert.FromBase64String(s);
    }
}
