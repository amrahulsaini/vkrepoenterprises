using MySqlConnector;

namespace VKmobileapi.Data;

public static class DbFactory
{
    private static string _masterConn = "";

    public static void Init()
    {
        var host = Environment.GetEnvironmentVariable("MYSQL_HOST")     ?? "127.0.0.1";
        var user = RequiredEnv.Get("MYSQL_USER");
        var pass = RequiredEnv.Get("MYSQL_PASSWORD");
        var db   = RequiredEnv.Get("MYSQL_DATABASE");
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
            UserID   = RequiredEnv.Get("MASTER_DB_USER"),
            Password = RequiredEnv.Get("MASTER_DB_PASSWORD"),
            SslMode  = MySqlSslMode.None,
            Pooling  = true,
            ConnectionTimeout     = 10,
            DefaultCommandTimeout = 30,
        }.ConnectionString;
    }

    public static MySqlConnection Create() => new(TenantContext.Conn);

    public static MySqlConnection CreateMaster() => new(_masterConn);
}
