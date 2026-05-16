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
            Id            = u.Id,
            Name          = u.Name,
            Mobile        = u.Mobile,
            Address       = u.Address,
            Pincode       = u.Pincode,
            PfpBase64     = u.PfpBase64,
            DeviceId      = u.DeviceId,
            IsActive      = u.IsActive,
            IsAdmin       = u.IsAdmin,
            Balance       = u.Balance,
            CreatedAt     = u.CreatedAt,
            SubEndDate    = u.SubEndDate,
            IsStopped     = u.IsStopped,
            IsBlacklisted = u.IsBlacklisted,
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

    public async Task SetStoppedAsync(long userId, bool stopped)
        => await DesktopApiClient.SetUserStoppedAsync(userId, stopped);

    public async Task SetBlacklistedAsync(long userId, bool blacklisted)
        => await DesktopApiClient.SetUserBlacklistedAsync(userId, blacklisted);

    public async Task<List<int>> GetFinanceRestrictionsAsync(long userId)
        => await DesktopApiClient.GetUserFinanceRestrictionsAsync(userId);

    public async Task SetFinanceRestrictionsAsync(long userId, List<int> financeIds)
        => await DesktopApiClient.SetUserFinanceRestrictionsAsync(userId, financeIds);

    internal async Task<DesktopApiClient.KycDocsDto> GetKycAsync(long userId)
        => await DesktopApiClient.GetUserKycAsync(userId);

    internal async Task DeleteKycAsync(long userId, string docType)
        => await DesktopApiClient.DeleteUserKycAsync(userId, docType);

    internal async Task SetAdminPassAsync(long userId, string password)
        => await DesktopApiClient.SetUserAdminPassAsync(userId, password);

    internal async Task<bool> IsAdminPassSetAsync(long userId)
        => await DesktopApiClient.IsUserAdminPassSetAsync(userId);
}
