using System;
using System.Threading.Tasks;
using MySqlConnector;

namespace VRASDesktopApp.Data;

public static class MySqlFactory
{
    private const string Host = "127.0.0.1";
    private const string User = "vkre_db1";
    private const string Pass = "db1";
    private const string Db   = "vkre_db1";
    private const uint   Port = 3306;

    public static MySqlConnection CreateConnection()
    {
        var cs = new MySqlConnectionStringBuilder
        {
            Server   = Host,
            UserID   = User,
            Password = Pass,
            Database = Db,
            Port     = Port,
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
        => $"Server={Host};Port={Port};User={User};Database={Db};Password=****";
}
