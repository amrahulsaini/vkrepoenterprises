using System.Collections.Generic;
using System.IO;
using System.Text;
using VRASDesktopApp.Data;

namespace VRASDesktopApp.Exports;

// Fast CSV writer for vehicle rows. Replaces the old Syncfusion XlsIO path,
// which spent minutes on 70k+ rows because every cell write went through
// IRange marshalling + number-format detection. CSV is pure text streaming —
// a StreamWriter + a per-row StringBuilder — so a 100k-row chunk writes in
// well under a second. Excel, LibreOffice and Google Sheets all open .csv
// natively, so the end user experience is unchanged (double-click → opens in
// Excel) while the export is effectively instant.
//
// The class name stays VehicleExcelWriter so existing callers don't change;
// the output is now .csv (the dialog names files accordingly).
internal static class VehicleExcelWriter
{
    private static readonly string[] Headers =
    {
        "Vehicle No", "Chassis No", "Engine No", "Model", "Agreement No",
        "Customer Name", "Customer Contact", "Customer Address",
        "Finance Name", "Branch Name", "Bucket", "GV", "OD", "Seasoning",
        "TBR Flag", "Sec9 Available", "Sec17 Available",
        "Level1", "Level1 Contact", "Level2", "Level2 Contact",
        "Level3", "Level3 Contact", "Level4", "Level4 Contact",
        "Sender Mail 1", "Sender Mail 2", "Executive Name",
        "POS", "TOSS", "Remark", "Region", "Area", "Created On"
    };

    // File extension this writer produces. The dialog reads it so the file
    // list / save names stay in sync if we ever swap formats again.
    public const string Extension = "csv";

    public static void Write(
        List<DesktopApiClient.ExportVehicleRow> rows,
        string sheetName,           // unused for CSV — kept for call-site compat
        string filePath)
    {
        // UTF-8 BOM so Excel opens Indian names / non-ASCII text correctly
        // instead of mojibake. 1 MB buffer keeps disk writes batched.
        using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20);
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), 1 << 20);

        var sb = new StringBuilder(2048);

        // Header
        AppendRow(sb, Headers);
        writer.Write(sb.ToString());

        foreach (var v in rows)
        {
            sb.Clear();
            AppendRow(sb, new[]
            {
                v.VehicleNo, v.ChassisNo, v.EngineNo, v.Model, v.AgreementNo,
                v.CustomerName, v.CustomerContact, v.CustomerAddress,
                v.Financer, v.BranchName, v.Bucket, v.Gv, v.Od, v.Seasoning,
                v.TbrFlag, v.Sec9, v.Sec17,
                v.Level1, v.Level1Contact, v.Level2, v.Level2Contact,
                v.Level3, v.Level3Contact, v.Level4, v.Level4Contact,
                v.SenderMail1, v.SenderMail2, v.ExecutiveName,
                v.Pos, v.Toss, v.Remark, v.Region, v.Area, v.CreatedOn
            });
            writer.Write(sb.ToString());
        }
    }

    // Builds one CSV record (with trailing CRLF) into sb, escaping per RFC 4180:
    // wrap in double-quotes when the value contains a comma, quote or newline,
    // and double any embedded quotes.
    private static void AppendRow(StringBuilder sb, string?[] cells)
    {
        for (int i = 0; i < cells.Length; i++)
        {
            if (i > 0) sb.Append(',');
            var s = cells[i] ?? "";
            if (NeedsQuoting(s))
            {
                sb.Append('"');
                sb.Append(s.Replace("\"", "\"\""));
                sb.Append('"');
            }
            else
            {
                sb.Append(s);
            }
        }
        sb.Append("\r\n");
    }

    private static bool NeedsQuoting(string s)
    {
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == ',' || c == '"' || c == '\n' || c == '\r') return true;
        }
        return false;
    }
}
