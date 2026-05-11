using MySqlConnector;

namespace VKmobileapi.Data;

public static class DbFactory
{
    public static MySqlConnection Create()
    {
        var host = Environment.GetEnvironmentVariable("MYSQL_HOST")     ?? "127.0.0.1";
        var user = Environment.GetEnvironmentVariable("MYSQL_USER")     ?? "vkre_db1";
        var pass = Environment.GetEnvironmentVariable("MYSQL_PASSWORD") ?? "db1";
        var db   = Environment.GetEnvironmentVariable("MYSQL_DATABASE") ?? "vkre_db1";
        var port = Environment.GetEnvironmentVariable("MYSQL_PORT")     ?? "3306";

        var cs = new MySqlConnectionStringBuilder
        {
            Server          = host,
            UserID          = user,
            Password        = pass,
            Database        = db,
            Port            = uint.TryParse(port, out var p) ? p : 3306u,
            SslMode         = MySqlSslMode.None,
            Pooling         = true,
            MaximumPoolSize = 20,
            ConnectionTimeout      = 10,
            DefaultCommandTimeout  = 30
        }.ConnectionString;

        return new MySqlConnection(cs);
    }
}
