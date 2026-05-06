namespace VKApiServer.Models;

public class LoginRequest
{
    public string mobileno { get; set; } = "";
    public string password { get; set; } = "";
}

public class SignedAppUser
{
    public int AppUserId { get; set; }
    public string MobileNo { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public bool IsAdmin { get; set; } = true;
    public string Token { get; set; } = "";
}

public class HomeDashboardResponse
{
    public OverviewCard Overview { get; set; } = new();
    public List<CollectionMetric> Collections { get; set; } = new();
    public List<UploadFileItem> RecentUploads { get; set; } = new();
    public List<DetailViewItem> RecentDetails { get; set; } = new();
    public List<BranchSummaryItem> TopBranches { get; set; } = new();
}

public class OverviewCard
{
    public long TotalRecords { get; set; }
    public long TotalBranches { get; set; }
    public long TotalHeadOffices { get; set; }
    public long TotalUsers { get; set; }
    public long ActiveUsers { get; set; }
    public long AdminUsers { get; set; }
    public long TotalDetailViews { get; set; }
    public long FoundDetails { get; set; }
    public long TotalUploads { get; set; }
    public long TotalOtps { get; set; }
    public long TotalBillings { get; set; }
}

public class CollectionMetric
{
    public string Name { get; set; } = "";
    public long Count { get; set; }
    public string Summary { get; set; } = "";
}

public class UploadFileItem
{
    public string FileName { get; set; } = "";
    public string BankName { get; set; } = "";
    public string VehicleType { get; set; } = "";
    public string UploadedBy { get; set; } = "";
    public string UploadedDate { get; set; } = "";
    public string CreatedOn { get; set; } = "";
}

public class DetailViewItem
{
    public string VehicleNo { get; set; } = "";
    public string ChassisNo { get; set; } = "";
    public string EngineNo { get; set; } = "";
    public string Model { get; set; } = "";
    public string UserName { get; set; } = "";
    public string UserMobile { get; set; } = "";
    public string Location { get; set; } = "";
    public string VehicleStatus { get; set; } = "";
    public string CreatedOn { get; set; } = "";
}

public class BranchSummaryItem
{
    public string BranchId { get; set; } = "";
    public string BranchName { get; set; } = "";
    public string HeadOfficeName { get; set; } = "";
    public long Records { get; set; }
    public string ContactPerson { get; set; } = "";
    public string ContactMobile { get; set; } = "";
    public string UpdatedOn { get; set; } = "";
}

public class VehicleSearchResponse
{
    public string Query { get; set; } = "";
    public string Mode { get; set; } = "";
    public int ResultCount { get; set; }
    public int UniqueBranches { get; set; }
    public List<VehicleSearchItem> Results { get; set; } = new();
}

public class VehicleSearchItem
{
    // Record Identifier
    public string Id { get; set; } = "";

    // Vehicle
    public string ReleaseStatus { get; set; } = "";
    public string VehicleNo { get; set; } = "";
    public string ChassisNo { get; set; } = "";
    public string Model { get; set; } = "";
    public string EngineNo { get; set; } = "";

    // Customer
    public string CustomerName { get; set; } = "";
    public string CustomerContactNos { get; set; } = "";
    public string CustomerAddress { get; set; } = "";

    // Finance
    public string Financer { get; set; } = "";
    public string BranchName { get; set; } = "";
    public string FirstContactDetails { get; set; } = "";
    public string SecondContactDetails { get; set; } = "";
    public string ThirdContactDetails { get; set; } = "";
    public string Address { get; set; } = "";
    public string BranchFromExcel { get; set; } = "";
    public string ExecutiveName { get; set; } = "";
    public string Level1 { get; set; } = "";
    public string Level1ContactNos { get; set; } = "";
    public string Level2 { get; set; } = "";
    public string Level2ContactNos { get; set; } = "";
    public string Level3 { get; set; } = "";
    public string Level3ContactNos { get; set; } = "";
    public string Level4 { get; set; } = "";
    public string Level4ContactNos { get; set; } = "";

    // Loan
    public string AgreementNo { get; set; } = "";
    public string Region { get; set; } = "";
    public string Area { get; set; } = "";
    public string Bucket { get; set; } = "";
    public string GV { get; set; } = "";
    public string OD { get; set; } = "";
    public string Sec9Available { get; set; } = "";
    public string Sec17Available { get; set; } = "";
    public string TBRFlag { get; set; } = "";
    public string Seasoning { get; set; } = "";
    public string SenderMailId1 { get; set; } = "";
    public string SenderMailId2 { get; set; } = "";
    public string POS { get; set; } = "";
    public string TOSS { get; set; } = "";
    public string Remark { get; set; } = "";

    // Other
    public string CreatedOn { get; set; } = "";
    public string UpdatedOn { get; set; } = "";
}

public class FinanceDashboardResponse
{
    public long TotalHeadOffices { get; set; }
    public long TotalBranches { get; set; }
    public long TotalRecords { get; set; }
    public long TotalUploads { get; set; }
    public List<BranchSummaryItem> TopBranches { get; set; } = new();
    public List<UploadFileItem> RecentUploads { get; set; } = new();
    public List<NamedCountItem> Banks { get; set; } = new();
}

public class UsersDashboardResponse
{
    public long TotalUsers { get; set; }
    public long ActiveUsers { get; set; }
    public long AdminUsers { get; set; }
    public long TotalPlans { get; set; }
    public long RegisteredDevices { get; set; }
    public List<UserSummaryItem> Users { get; set; } = new();
    public List<PlanSummaryItem> PlanAlerts { get; set; } = new();
}

public class UserSummaryItem
{
    public string UserId { get; set; } = "";
    public string FullName { get; set; } = "";
    public string MobileNo { get; set; } = "";
    public string Address { get; set; } = "";
    public string Role { get; set; } = "";
    public string Status { get; set; } = "";
    public int BranchCount { get; set; }
    public string DeviceId { get; set; } = "";
    public string RequestDeviceId { get; set; } = "";
    public string PlanEndDate { get; set; } = "";
    public string CreatedOn { get; set; } = "";
}

public class PlanSummaryItem
{
    public string UserName { get; set; } = "";
    public string MobileNo { get; set; } = "";
    public string StartDate { get; set; } = "";
    public string EndDate { get; set; } = "";
}

public class UploadsDashboardResponse
{
    public long TotalFiles { get; set; }
    public long TotalBanks { get; set; }
    public long TotalHeaders { get; set; }
    public string LatestUpload { get; set; } = "";
    public List<UploadFileItem> Files { get; set; } = new();
    public List<NamedCountItem> Banks { get; set; } = new();
}
public class ConfirmationRequest
{
    public string VehicleNo { get; set; } = "";
    public string ChassisNo { get; set; } = "";
    public string Model { get; set; } = "";
    public string EngineNo { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public string CustomerContactNos { get; set; } = "";
    public string CustomerAddress { get; set; } = "";
    public string FinanceName { get; set; } = "";
    public string BranchName { get; set; } = "";
    public string BranchFirstContactDetails { get; set; } = "";
    public string BranchSecondContactDetails { get; set; } = "";
    public string BranchThirdContactDetails { get; set; } = "";
    
    public string SeizerId { get; set; } = "";
    public string SeizerName { get; set; } = "";
    public bool VehicleContainsLoad { get; set; }
    public string LoadDescription { get; set; } = "";
    public string ConfirmBy { get; set; } = "";
    public string Status { get; set; } = "";
    public string Yard { get; set; } = "";
    public bool ApplyAmtCredited { get; set; }
    public decimal AmountCredited { get; set; }
}

public class ConfirmationResponseItem
{
    public string Id { get; set; } = "";
    public string VehicleNo { get; set; } = "";
    public string ChassisNo { get; set; } = "";
    public string Model { get; set; } = "";
    public string SeizerName { get; set; } = "";
    public string Status { get; set; } = "";
    public string ConfirmedOn { get; set; } = "";
}
public class DetailsDashboardResponse
{
    public long TotalViews { get; set; }
    public long FoundCount { get; set; }
    public long NotFoundCount { get; set; }
    public long UniqueUsers { get; set; }
    public List<DetailViewItem> Items { get; set; } = new();
    public List<NamedCountItem> Locations { get; set; } = new();
}

public class OtpDashboardResponse
{
    public long TotalOtps { get; set; }
    public long TotalUsers { get; set; }
    public long Last24Hours { get; set; }
    public List<OtpItem> Items { get; set; } = new();
}

public class OtpItem
{
    public string UserName { get; set; } = "";
    public string UserMobile { get; set; } = "";
    public string Otp { get; set; } = "";
    public string UpdatedOn { get; set; } = "";
    public string Status { get; set; } = "";
}

public class ReportsDashboardResponse
{
    public long TotalVehicles { get; set; }
    public long TotalUploads { get; set; }
    public long TotalBranches { get; set; }
    public long TotalUsers { get; set; }
    public List<CollectionMetric> Collections { get; set; } = new();
    public List<BranchSummaryItem> TopBranches { get; set; } = new();
    public List<NamedCountItem> TopBanks { get; set; } = new();
}

public class PaymentsDashboardResponse
{
    public long TotalBanks { get; set; }
    public long TotalBillings { get; set; }
    public long WebhookUsers { get; set; }
    public long TotalUploads { get; set; }
    public string StatusNote { get; set; } = "";
    public List<PaymentMethodItem> PaymentMethods { get; set; } = new();
    public List<NamedCountItem> Banks { get; set; } = new();
    public List<UploadFileItem> RecentUploads { get; set; } = new();
}

public class PaymentMethodItem
{
    public int PaymentMethodId { get; set; }
    public string MethodName { get; set; } = "";
    public string Details { get; set; } = "";
    public bool IsActive { get; set; } = true;
}

public class ModuleStatusResponse
{
    public string ModuleKey { get; set; } = "";
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public MetricCard Primary { get; set; } = new();
    public MetricCard Secondary { get; set; } = new();
    public string Banner { get; set; } = "";
    public List<NamedCountItem> Highlights { get; set; } = new();
    public List<CollectionMetric> Collections { get; set; } = new();
    public List<TimelineItem> RecentItems { get; set; } = new();
}

public class MetricCard
{
    public string Label { get; set; } = "";
    public string Value { get; set; } = "";
    public string Description { get; set; } = "";
}

public class NamedCountItem
{
    public string Name { get; set; } = "";
    public long Count { get; set; }
    public string Detail { get; set; } = "";
}

public class TimelineItem
{
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public string Detail { get; set; } = "";
    public string Timestamp { get; set; } = "";
}

public class CreateConfirmationRequest
{
    public string VehicleId { get; set; } = "";
    public string VehicleNo { get; set; } = "";
    public string ChassisNo { get; set; } = "";
    public string Model { get; set; } = "";
    public string SeizerId { get; set; } = "";
    public string SeizerName { get; set; } = "";
    public bool VehicleContainsLoad { get; set; }
    public string LoadDescription { get; set; } = "";
    public string ConfirmBy { get; set; } = "";
    public string Status { get; set; } = "";
    public string Yard { get; set; } = "";
    public bool ApplyAmtCredited { get; set; }
    public string AmountCredited { get; set; } = "";
}

public class ConfirmationListItem
{
    public string Id { get; set; } = "";
    public string VehicleNo { get; set; } = "";
    public string ChassisNo { get; set; } = "";
    public string Model { get; set; } = "";
    public string SeizerName { get; set; } = "";
    public string Status { get; set; } = "";
    public string ConfirmedOn { get; set; } = "";
}
