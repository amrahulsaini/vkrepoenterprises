using System;
using System.Threading.Tasks;
using MySqlConnector;

namespace VRASDesktopApp.Data;

public static class MySqlFactory
{
    public static MySqlConnection CreateConnection()
    {
        var host = Environment.GetEnvironmentVariable("MYSQL_HOST") ?? "localhost";
        var user = Environment.GetEnvironmentVariable("MYSQL_USER") ?? "root";
        var pass = Environment.GetEnvironmentVariable("MYSQL_PASSWORD") ?? string.Empty;
        var db   = Environment.GetEnvironmentVariable("MYSQL_DATABASE") ?? "test";
        var port = Environment.GetEnvironmentVariable("MYSQL_PORT") ?? "3306";

        var cs = new MySqlConnectionStringBuilder
        {
            Server   = host,
            UserID   = user,
            Password = pass,
            Database = db,
            Port     = uint.TryParse(port, out var p) ? p : 3306u,
            SslMode  = MySqlSslMode.None,
            Pooling  = true,
            // Do NOT set MinimumPoolSize > 0 — it can cause eager connection
            // attempts that race and time out on slow remote servers.
            MaximumPoolSize      = 10,
            ConnectionIdleTimeout = 300,  // keep idle connections alive 5 min
            ConnectionReset      = false, // skip COM_CHANGE_USER on pool reuse (~5ms saved)
            ConnectionTimeout    = 8,     // generous enough for remote servers
            DefaultCommandTimeout = 30,
            AllowLoadLocalInfile  = true  // enables LOAD DATA LOCAL INFILE path in MySqlBulkCopy
        }.ConnectionString;

        return new MySqlConnection(cs);
    }

    /// <summary>
    /// Opens and immediately returns a connection to the pool.
    /// Call this at app startup so the first real query reuses a warm socket.
    /// </summary>
    public static async Task WarmUpAsync()
    {
        try
        {
            await using var conn = CreateConnection();
            await conn.OpenAsync();
            await using var cmd = new MySqlCommand("SELECT 1", conn);
            await cmd.ExecuteScalarAsync();
            // conn disposed → returned to pool; next Open() is near-instant
        }
        catch { /* silent — warm-up failure must never crash the app */ }
    }

    public static string GetConnectionInfoMasked()
    {
        var host = Environment.GetEnvironmentVariable("MYSQL_HOST") ?? "localhost";
        var user = Environment.GetEnvironmentVariable("MYSQL_USER") ?? "root";
        var db   = Environment.GetEnvironmentVariable("MYSQL_DATABASE") ?? "test";
        var port = Environment.GetEnvironmentVariable("MYSQL_PORT") ?? "3306";
        return $"Server={host};Port={port};User={user};Database={db};Password=****";
    }
}
