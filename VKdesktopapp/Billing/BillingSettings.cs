using System;
using System.IO;
using System.Text.Json;

namespace CRMRSDesktopApp.Billing;

public class BillingSettings
{
    public string AgencyName       { get; set; } = "";
    public string AgencyAddress    { get; set; } = "";
    public string State            { get; set; } = "";
    public string PanNo            { get; set; } = "";
    public string VendorCode       { get; set; } = "";
    public string Sub              { get; set; } = "Claim of Repossession Charges";
    public string DescriptionGoods { get; set; } = "REPOSESSION CHARGES";
    public string HsnSac           { get; set; } = "";

    public string BankName         { get; set; } = "";
    public string BankBranch       { get; set; } = "";
    public string AcHolderName     { get; set; } = "";
    public string AccountNo        { get; set; } = "";
    public string IfscCode         { get; set; } = "";

    public string HeaderImagePath  { get; set; } = "";

    private static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VKEnterprises");
    private static string FilePath => Path.Combine(Dir, "billing_settings.json");

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
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    public static string CopyHeaderImage(string sourcePath)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var ext = Path.GetExtension(sourcePath);
            var dest = Path.Combine(Dir, "billing_header" + ext);
            File.Copy(sourcePath, dest, true);
            return dest;
        }
        catch { return sourcePath; }
    }
}
