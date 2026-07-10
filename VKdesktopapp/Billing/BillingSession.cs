using System.Collections.Generic;

namespace CRMRSDesktopApp.Billing;

public class BillingSession
{
    public long MemberId { get; set; }
    public string MemberName { get; set; } = "";
    public List<int> FinanceIds { get; set; } = new();
}
