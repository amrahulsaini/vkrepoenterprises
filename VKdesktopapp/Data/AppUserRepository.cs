using MySqlConnector;
using System.Data;
using VRASDesktopApp.Models;

namespace VRASDesktopApp.Data;

public class AppUserRepository
{
    public async Task<List<AppUserListItem>> GetUsersAsync()
    {
        var list = new List<AppUserListItem>();
        await using var conn = MySqlFactory.CreateConnection();
        await conn.OpenAsync();
        const string sql = @"
            SELECT u.id, u.name, u.mobile, u.address, u.pincode,
                   u.pfp, u.device_id, u.is_active, u.is_admin,
                   u.balance, u.created_at,
                   (SELECT MAX(s.end_date) FROM subscriptions s WHERE s.user_id = u.id) AS sub_end
            FROM app_users u
            ORDER BY u.created_at DESC";
        await using var cmd = new MySqlCommand(sql, conn);
        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
        {
            list.Add(new AppUserListItem
            {
                Id         = rdr.GetInt64("id"),
                Name       = rdr.GetString("name"),
                Mobile     = rdr.GetString("mobile"),
                Address    = rdr.IsDBNull("address") ? null : rdr.GetString("address"),
                Pincode    = rdr.IsDBNull("pincode") ? null : rdr.GetString("pincode"),
                PfpBase64  = rdr.IsDBNull("pfp") ? null : rdr.GetString("pfp"),
                DeviceId   = rdr.IsDBNull("device_id") ? null : rdr.GetString("device_id"),
                IsActive   = rdr.GetBoolean("is_active"),
                IsAdmin    = rdr.GetBoolean("is_admin"),
                Balance    = rdr.GetDecimal("balance"),
                CreatedAt  = rdr.GetDateTime("created_at"),
                SubEndDate = rdr.IsDBNull("sub_end") ? null : rdr.GetString("sub_end"),
            });
        }
        return list;
    }

    public async Task SetActiveAsync(long userId, bool active)
    {
        await using var conn = MySqlFactory.CreateConnection();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "UPDATE app_users SET is_active = @v WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("@v", active ? 1 : 0);
        cmd.Parameters.AddWithValue("@id", userId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task SetAdminAsync(long userId, bool admin)
    {
        await using var conn = MySqlFactory.CreateConnection();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "UPDATE app_users SET is_admin = @v WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("@v", admin ? 1 : 0);
        cmd.Parameters.AddWithValue("@id", userId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<SubscriptionItem>> GetSubscriptionsAsync(long userId)
    {
        var list = new List<SubscriptionItem>();
        await using var conn = MySqlFactory.CreateConnection();
        await conn.OpenAsync();
        const string sql = @"
            SELECT id, start_date, end_date, amount, notes, created_at
            FROM subscriptions WHERE user_id = @uid ORDER BY created_at DESC";
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@uid", userId);
        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
        {
            list.Add(new SubscriptionItem
            {
                Id        = rdr.GetInt64("id"),
                StartDate = rdr.GetString("start_date"),
                EndDate   = rdr.GetString("end_date"),
                Amount    = rdr.IsDBNull("amount") ? 0m : rdr.GetDecimal("amount"),
                Notes     = rdr.IsDBNull("notes") ? null : rdr.GetString("notes"),
                CreatedAt = rdr.GetDateTime("created_at"),
            });
        }
        return list;
    }

    public async Task AddSubscriptionAsync(long userId, string startDate, string endDate,
        decimal amount, string? notes)
    {
        await using var conn = MySqlFactory.CreateConnection();
        await conn.OpenAsync();
        const string sql = @"
            INSERT INTO subscriptions (user_id, start_date, end_date, amount, notes)
            VALUES (@uid, @s, @e, @a, @n)";
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@uid", userId);
        cmd.Parameters.AddWithValue("@s",   startDate);
        cmd.Parameters.AddWithValue("@e",   endDate);
        cmd.Parameters.AddWithValue("@a",   amount);
        cmd.Parameters.AddWithValue("@n",   (object?)notes ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteSubscriptionAsync(long subId)
    {
        await using var conn = MySqlFactory.CreateConnection();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "DELETE FROM subscriptions WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("@id", subId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task ResetDeviceAsync(long userId)
    {
        await using var conn = MySqlFactory.CreateConnection();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "UPDATE app_users SET device_id = NULL WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("@id", userId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<(int total, int active, int admins, int withSub)> GetStatsAsync()
    {
        await using var conn = MySqlFactory.CreateConnection();
        await conn.OpenAsync();
        const string sql = @"
            SELECT
                COUNT(*) AS total,
                SUM(is_active) AS active,
                SUM(is_admin) AS admins,
                (SELECT COUNT(DISTINCT user_id) FROM subscriptions
                 WHERE end_date >= CURDATE()) AS with_sub
            FROM app_users";
        await using var cmd = new MySqlCommand(sql, conn);
        await using var rdr = await cmd.ExecuteReaderAsync();
        await rdr.ReadAsync();
        return (
            rdr.GetInt32("total"),
            rdr.GetInt32("active"),
            rdr.GetInt32("admins"),
            rdr.GetInt32("with_sub")
        );
    }
}
