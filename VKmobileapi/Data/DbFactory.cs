using MySqlConnector;

namespace VKmobileapi.Data;

public static class DbFactory
{
    private static string _masterConn = "";

    // Called once at startup — captures env config into TenantContext and
    // builds the crm_master connection string.
    public static void Init()
    {
        var host = Environment.GetEnvironmentVariable("MYSQL_HOST")     ?? "127.0.0.1";
        var user = Environment.GetEnvironmentVariable("MYSQL_USER")     ?? "vkre_db1";
        var pass = Environment.GetEnvironmentVariable("MYSQL_PASSWORD") ?? "db1";
        var db   = Environment.GetEnvironmentVariable("MYSQL_DATABASE") ?? "vkre_db1";
        uint port = uint.TryParse(Environment.GetEnvironmentVariable("MYSQL_PORT"), out var p) ? p : 3306u;

        TenantContext.MysqlHost = host;
        TenantContext.MysqlPort = (int)port;
        TenantContext.DefaultConn = new MySqlConnectionStringBuilder
        {
            Server          = host,
            UserID          = user,
            Password        = pass,
            Database        = db,
            Port            = port,
            SslMode         = MySqlSslMode.None,
            Pooling         = true,
            MaximumPoolSize = 20,
            ConnectionTimeout     = 10,
            DefaultCommandTimeout = 30,
        }.ConnectionString;

        // crm_master registry — agency list + registration verification gate.
        _masterConn = new MySqlConnectionStringBuilder
        {
            Server   = host,
            Port     = port,
            Database = "crm_master",
            UserID   = Environment.GetEnvironmentVariable("MASTER_DB_USER")     ?? "crm_master_app",
            Password = Environment.GetEnvironmentVariable("MASTER_DB_PASSWORD") ?? "SET_VIA_ENV",
            SslMode  = MySqlSslMode.None,
            Pooling  = true,
            ConnectionTimeout     = 10,
            DefaultCommandTimeout = 30,
        }.ConnectionString;
    }

    /// <summary>Connection to the current request's tenant DB (vkre_db1 by default).</summary>
    public static MySqlConnection Create() => new(TenantContext.Conn);

    /// <summary>Connection to the crm_master agency registry.</summary>
    public static MySqlConnection CreateMaster() => new(_masterConn);
}
