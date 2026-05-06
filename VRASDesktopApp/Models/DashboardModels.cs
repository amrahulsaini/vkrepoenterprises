namespace VRASDesktopApp.Models;

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
    public string Name { get; set; } = string.Empty;
    public long Count { get; set; }
    public string Summary { get; set; } = string.Empty;
}

public class UploadFileItem
{
    public string FileName { get; set; } = string.Empty;
    public string BankName { get; set; } = string.Empty;
    public string VehicleType { get; set; } = string.Empty;
    public string UploadedBy { get; set; } = string.Empty;
    public string UploadedDate { get; set; } = string.Empty;
    public string CreatedOn { get; set; } = string.Empty;
}

public class DetailViewItem
{
    public string VehicleNo { get; set; } = string.Empty;
    public string ChassisNo { get; set; } = string.Empty;
    public string EngineNo { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string UserMobile { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string VehicleStatus { get; set; } = string.Empty;
    public string CreatedOn { get; set; } = string.Empty;
}

public class BranchSummaryItem
{
    public string BranchId { get; set; } = string.Empty;
    public string BranchName { get; set; } = string.Empty;
    public string HeadOfficeName { get; set; } = string.Empty;
    public long Records { get; set; }
    public string ContactPerson { get; set; } = string.Empty;
    public string ContactMobile { get; set; } = string.Empty;
    public string UpdatedOn { get; set; } = string.Empty;
}

public class VehicleSearchResponse
{
    public string Query { get; set; } = string.Empty;
    public string Mode { get; set; } = string.Empty;
    public int ResultCount { get; set; }
    public int UniqueBranches { get; set; }
    public List<VehicleSearchItem> Results { get; set; } = new();
}

public class VehicleSearchItem
{
    // Record Identifier
    public string Id { get; set; } = string.Empty;

    // Vehicle
    public string ReleaseStatus { get; set; } = string.Empty;
    public string VehicleNo { get; set; } = string.Empty;
    public string ChassisNo { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string EngineNo { get; set; } = string.Empty;

    // Customer
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerContactNos { get; set; } = string.Empty;
    public string CustomerAddress { get; set; } = string.Empty;

    // Finance
    public string Financer { get; set; } = string.Empty;
    public string BranchName { get; set; } = string.Empty;
    public string FirstContactDetails { get; set; } = string.Empty;
    public string SecondContactDetails { get; set; } = string.Empty;
    public string ThirdContactDetails { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string BranchFromExcel { get; set; } = string.Empty;
    public string ExecutiveName { get; set; } = string.Empty;
    public string Level1 { get; set; } = string.Empty;
    public string Level1ContactNos { get; set; } = string.Empty;
    public string Level2 { get; set; } = string.Empty;
    public string Level2ContactNos { get; set; } = string.Empty;
    public string Level3 { get; set; } = string.Empty;
    public string Level3ContactNos { get; set; } = string.Empty;
    public string Level4 { get; set; } = string.Empty;
    public string Level4ContactNos { get; set; } = string.Empty;

    // Loan
    public string AgreementNo { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string Area { get; set; } = string.Empty;
    public string Bucket { get; set; } = string.Empty;
    public string GV { get; set; } = string.Empty;
    public string OD { get; set; } = string.Empty;
    public string Sec9Available { get; set; } = string.Empty;
    public string Sec17Available { get; set; } = string.Empty;
    public string TBRFlag { get; set; } = string.Empty;
    public string Seasoning { get; set; } = string.Empty;
    public string SenderMailId1 { get; set; } = string.Empty;
    public string SenderMailId2 { get; set; } = string.Empty;
    public string POS { get; set; } = string.Empty;
    public string TOSS { get; set; } = string.Empty;
    public string Remark { get; set; } = string.Empty;

    // Other
    public string CreatedOn { get; set; } = string.Empty;
    public string UpdatedOn { get; set; } = string.Empty;
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
    public string UserId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string MobileNo { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int BranchCount { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    public string RequestDeviceId { get; set; } = string.Empty;
    public string PlanEndDate { get; set; } = string.Empty;
    public string CreatedOn { get; set; } = string.Empty;
}

public class PlanSummaryItem
{
    public string UserName { get; set; } = string.Empty;
    public string MobileNo { get; set; } = string.Empty;
    public string StartDate { get; set; } = string.Empty;
    public string EndDate { get; set; } = string.Empty;
}

public class UploadsDashboardResponse
{
    public long TotalFiles { get; set; }
    public long TotalBanks { get; set; }
    public long TotalHeaders { get; set; }
    public string LatestUpload { get; set; } = string.Empty;
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
    public string UserName { get; set; } = string.Empty;
    public string UserMobile { get; set; } = string.Empty;
    public string Otp { get; set; } = string.Empty;
    public string UpdatedOn { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
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
    public string StatusNote { get; set; } = string.Empty;
    public List<DashboardPaymentMethod> PaymentMethods { get; set; } = new();
    public List<NamedCountItem> Banks { get; set; } = new();
    public List<UploadFileItem> RecentUploads { get; set; } = new();
}

public class DashboardPaymentMethod
{
    public int PaymentMethodId { get; set; }
    public string MethodName { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

public class ModuleStatusResponse
{
    public string ModuleKey { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public MetricCard Primary { get; set; } = new();
    public MetricCard Secondary { get; set; } = new();
    public string Banner { get; set; } = string.Empty;
    public List<NamedCountItem> Highlights { get; set; } = new();
    public List<CollectionMetric> Collections { get; set; } = new();
    public List<TimelineItem> RecentItems { get; set; } = new();
}

public class MetricCard
{
    public string Label { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public class NamedCountItem
{
    public string Name { get; set; } = string.Empty;
    public long Count { get; set; }
    public string Detail { get; set; } = string.Empty;
}

public class TimelineItem
{
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public string Timestamp { get; set; } = string.Empty;
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
