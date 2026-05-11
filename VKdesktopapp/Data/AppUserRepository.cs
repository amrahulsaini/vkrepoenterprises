using VRASDesktopApp.Models;

namespace VRASDesktopApp.Data;

public class AppUserRepository
{
    public async Task<(List<AppUserListItem> users, int total, int active, int admins, int withSub)>
        GetUsersWithStatsAsync()
    {
        var r = await DesktopApiClient.GetUsersWithStatsAsync();
        var users = r.Users.Select(u => new AppUserListItem
        {
            Id         = u.Id,
            Name       = u.Name,
            Mobile     = u.Mobile,
            Address    = u.Address,
            Pincode    = u.Pincode,
            PfpBase64  = u.PfpBase64,
            DeviceId   = u.DeviceId,
            IsActive   = u.IsActive,
            IsAdmin    = u.IsAdmin,
            Balance    = u.Balance,
            CreatedAt  = u.CreatedAt,
            SubEndDate = u.SubEndDate,
        }).ToList();
        return (users, r.Stats.Total, r.Stats.Active, r.Stats.Admins, r.Stats.WithSub);
    }

    public async Task<(int total, int active, int admins, int withSub)> GetStatsAsync()
    {
        var s = await DesktopApiClient.GetUserStatsAsync();
        return (s.Total, s.Active, s.Admins, s.WithSub);
    }

    public async Task SetActiveAsync(long userId, bool active)
        => await DesktopApiClient.SetUserActiveAsync(userId, active);

    public async Task SetAdminAsync(long userId, bool admin)
        => await DesktopApiClient.SetUserAdminAsync(userId, admin);

    public async Task<List<SubscriptionItem>> GetSubscriptionsAsync(long userId)
    {
        var subs = await DesktopApiClient.GetSubscriptionsAsync(userId);
        return subs.Select(s => new SubscriptionItem
        {
            Id        = s.Id,
            StartDate = s.StartDate,
            EndDate   = s.EndDate,
            Amount    = s.Amount,
            Notes     = s.Notes,
            CreatedAt = s.CreatedAt,
        }).ToList();
    }

    public async Task AddSubscriptionAsync(long userId, string startDate, string endDate,
        decimal amount, string? notes)
        => await DesktopApiClient.AddSubscriptionAsync(userId, startDate, endDate, amount, notes);

    public async Task DeleteSubscriptionAsync(long subId)
        => await DesktopApiClient.DeleteSubscriptionAsync(subId);

    public async Task ResetDeviceAsync(long userId)
        => await DesktopApiClient.ResetUserDeviceAsync(userId);
}
