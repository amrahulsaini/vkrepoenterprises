// ─────────────────────────────────────────────────────────────────────
//  Per-request tenant database routing for the mobile API.
//
//  Mirrors VKApiServer's TenantContext. Every DbFactory.Create() opens the
//  legacy vkre_db1 database by default, or an agency's own crmr_<slug>
//  database once the request has been bound to an agency (by a verified
//  registration, a login, or a signed mobile tenant token).
// ─────────────────────────────────────────────────────────────────────
using System.Security.Cryptography;
using System.Text;
using MySqlConnector;

namespace VKmobileapi;

internal static class TenantContext
{
    private static readonly AsyncLocal<string?> _conn = new();
    private static readonly AsyncLocal<string?> _key  = new();

    /// <summary>Legacy single-tenant connection string. Set once at startup.</summary>
    public static string DefaultConn { get; set; } = "";
    public static string MysqlHost   { get; set; } = "127.0.0.1";
    public static int    MysqlPort   { get; set; } = 3306;

    /// <summary>Connection string the current request must use.</summary>
    public static string Conn { get => _conn.Value ?? DefaultConn; set => _conn.Value = value; }

    /// <summary>Cache-key suffix isolating one tenant's cached data from another's.</summary>
    public static string Key { get => _key.Value ?? "default"; set => _key.Value = value; }

    /// <summary>Routes the rest of this request to the given agency's tenant DB.</summary>
    public static void UseAgency(string slug)
    {
        Conn = BuildTenantConn(slug);
        Key  = slug;
    }

    /// <summary>
    /// Builds the connection string for an agency's tenant database. The db
    /// name / user / password are deterministic from the slug — and must match
    /// VKApiServer's AgencyPortal provisioning exactly.
    /// </summary>
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

    /// <summary>Deterministic per-tenant DB password — must byte-for-byte match
    /// VKApiServer's AgencyPortal.DeriveTenantPassword (same secret + algorithm),
    /// since that is the password the tenant DB user was created with.</summary>
    public static string DeriveTenantPassword(string slug)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(TenantDbSecret));
        var bytes = hmac.ComputeHash(Encoding.UTF8.GetBytes("tenant:" + slug));
        return "T1!" + Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').Substring(0, 25);
    }
}

/// <summary>
/// HMAC-signed mobile session token binding a device to one agency.
///   Format :  mt1.{base64url(slug|expiryUnix)}.{base64url(hmac-sha256)}
/// Issued at login; the routing middleware verifies it offline and reads the
/// slug, so a client cannot switch itself to another agency's database.
/// </summary>
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

    /// <summary>Returns the agency slug if the token is valid &amp; unexpired, else null.</summary>
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
