using MySqlConnector;

namespace VKmobileapi.Data;

public static class DbFactory
{
    private static string _masterConn = "";

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

    public static MySqlConnection Create() => new(TenantContext.Conn);

    public static MySqlConnection CreateMaster() => new(_masterConn);
}
