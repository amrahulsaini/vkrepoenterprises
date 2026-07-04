using System;
using System.IO;
using System.Text.Json;

namespace CRMRSDesktopApp.Billing;

public class BillingSettings
{
    public string AgencyName      { get; set; } = "";
    public string HeaderAddress   { get; set; } = "";
    public string HeaderContact   { get; set; } = "";
    public string HeaderEmail     { get; set; } = "";
    public string PanNo           { get; set; } = "";
    public string GstState        { get; set; } = "";
    public string BankAccountName { get; set; } = "";
    public string AccountNo       { get; set; } = "";
    public string IfscCode        { get; set; } = "";
    public string BankBranch      { get; set; } = "";
    public string ParkingYard     { get; set; } = "";
    public string PaymentName     { get; set; } = "";
    public string FooterLine      { get; set; } = "";

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VKEnterprises", "billing_settings.json");

    public static BillingSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<BillingSettings>(File.ReadAllText(FilePath)) ?? new BillingSettings();
        }
        catch { }
        return new BillingSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
