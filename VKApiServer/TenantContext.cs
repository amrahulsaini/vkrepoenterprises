// ─────────────────────────────────────────────────────────────────────
//  Multi-tenant request routing for VKApiServer.
//
//  Every API endpoint opens MySQL with  TenantContext.Conn  — which is the
//  legacy single-tenant database (vkre_db1) by default. When a request
//  carries a valid CRMS agency Bearer token, the routing middleware in
//  Program.cs swaps Conn (for that request's async flow only) to the
//  agency's own isolated  crmr_<slug>  database — so the same ~60 endpoints
//  transparently serve every agency from its own schema.
// ─────────────────────────────────────────────────────────────────────
using System;
using System.Security.Cryptography;
using System.Text;
using MySqlConnector;

namespace VKApiServer;

internal static class TenantContext
{
    private static readonly AsyncLocal<string?> _conn = new();
    private static readonly AsyncLocal<string?> _key  = new();

    /// <summary>Legacy single-tenant connection string. Assigned once at startup.</summary>
    public static string DefaultConn { get; set; } = "";

    /// <summary>Connection string for the database the current request must use.</summary>
    public static string Conn
    {
        get => _conn.Value ?? DefaultConn;
        set => _conn.Value = value;
    }

    /// <summary>Cache-key suffix that isolates one tenant's cached data from
    /// another's. "default" for legacy / untenanted requests, else the slug.</summary>
    public static string Key
    {
        get => _key.Value ?? "default";
        set => _key.Value = value;
    }

    /// <summary>
    /// Builds the connection string for an agency's isolated tenant database.
    /// db name / user / password are all deterministic from the slug (mirrors
    /// AgencyPortal's approve step) — so routing needs no crm_master lookup.
    /// </summary>
    public static string BuildTenantConn(string host, int port, string slug)
    {
        string dbName = "crmr_" + slug;
        string dbUser = "tu_" + slug;
        if (dbUser.Length > 32) dbUser = dbUser.Substring(0, 32);
        return new MySqlConnectionStringBuilder
        {
            Server   = host,
            Port     = (uint)port,
            UserID   = dbUser,
            Password = AgencyPortal.DeriveTenantPassword(slug),
            Database = dbName,
            SslMode  = MySqlSslMode.None,
            Pooling  = true,
            MaximumPoolSize       = 10,
            ConnectionTimeout     = 10,
            DefaultCommandTimeout = 30,
            AllowLoadLocalInfile  = true,
        }.ConnectionString;
    }
}

/// <summary>
/// HMAC-signed, self-verifying desktop session token for an approved agency.
///   Format   :  agt1.{base64url(payload)}.{base64url(hmac-sha256)}
///   payload  :  {agencyId}|{slug}|{expiryUnixSeconds}
/// The routing middleware verifies it offline (no DB hit) and reads the slug.
/// </summary>
internal static class AgencyToken
{
    private static readonly byte[] Key = Encoding.UTF8.GetBytes(
        Environment.GetEnvironmentVariable("TENANT_DB_SECRET")
        ?? "crmrs-tenant-secret-rotate-me-2026");

    public static string Issue(int agencyId, string slug, int validDays = 60)
    {
        long exp = DateTimeOffset.UtcNow.AddDays(validDays).ToUnixTimeSeconds();
        var payload = Encoding.UTF8.GetBytes($"{agencyId}|{slug}|{exp}");
        return "agt1." + B64Url(payload) + "." + B64Url(Sign(payload));
    }

    /// <summary>(id, slug) if the token is a well-formed, correctly-signed,
    /// unexpired agency token — otherwise null.</summary>
    public static (int id, string slug)? Verify(string? token)
    {
        try
        {
            if (!LooksLikeAgencyToken(token)) return null;
            var parts = token!.Split('.');
            if (parts.Length != 3) return null;
            var payload = FromB64Url(parts[1]);
            if (!CryptographicOperations.FixedTimeEquals(FromB64Url(parts[2]), Sign(payload)))
                return null;
            var f = Encoding.UTF8.GetString(payload).Split('|');
            if (f.Length != 3) return null;
            if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > long.Parse(f[2])) return null;
            return (int.Parse(f[0]), f[1]);
        }
        catch { return null; }
    }

    /// <summary>True when a token claims to be an agency token. An invalid
    /// agency token must be rejected outright — never silently fall back to
    /// the default database (that would expose the legacy tenant's data).</summary>
    public static bool LooksLikeAgencyToken(string? token) =>
        !string.IsNullOrEmpty(token) && token.StartsWith("agt1.", StringComparison.Ordinal);

    private static byte[] Sign(byte[] data)
    {
        using var h = new HMACSHA256(Key);
        return h.ComputeHash(data);
    }

    private static string B64Url(byte[] b) =>
        Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromB64Url(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        s += (s.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };
        return Convert.FromBase64String(s);
    }
}
