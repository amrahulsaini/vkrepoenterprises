using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using VRASDesktopApp.Data;

namespace VRASDesktopApp.Exports;

// Instant, real .xlsx writer for vehicle rows — no third-party library and no
// Syncfusion. An .xlsx is just a ZIP of XML parts, so we stream the worksheet
// XML straight into a ZipArchive entry using inline strings (t="inlineStr").
// Inline strings skip the shared-string-table dedup pass entirely, so writing
// is a single linear stream with no in-memory document model — a 100k-row,
// 34-column sheet writes in ~1-2s instead of the minutes Syncfusion's
// cell-by-cell API took.
//
// Output is a genuine .xlsx that Excel / LibreOffice / Google Sheets open
// natively (header row bold, numbers preserved as text so chassis/engine
// numbers aren't mangled into scientific notation).
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

    // Real Excel workbook now.
    public const string Extension = "xlsx";

    public static void Write(
        List<DesktopApiClient.ExportVehicleRow> rows,
        string sheetName,
        string filePath)
    {
        var safeSheet = SanitizeSheetName(sheetName);

        using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);

        WriteEntry(zip, "[Content_Types].xml", ContentTypesXml);
        WriteEntry(zip, "_rels/.rels", RelsXml);
        WriteEntry(zip, "xl/workbook.xml", WorkbookXml(safeSheet));
        WriteEntry(zip, "xl/_rels/workbook.xml.rels", WorkbookRelsXml);
        WriteEntry(zip, "xl/styles.xml", StylesXml);

        // Worksheet — streamed row by row so we never hold the whole sheet in memory.
        var sheetEntry = zip.CreateEntry("xl/worksheets/sheet1.xml", CompressionLevel.Fastest);
        using var sw = new StreamWriter(sheetEntry.Open(), new UTF8Encoding(false), 1 << 20);
        sw.Write("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sw.Write("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");

        // Header row (style s="1" = bold).
        WriteRow(sw, 1, Headers, styleIndex: 1);

        // Data rows.
        var buf = new string[Headers.Length];
        int r = 2;
        foreach (var v in rows)
        {
            buf[0]=v.VehicleNo; buf[1]=v.ChassisNo; buf[2]=v.EngineNo; buf[3]=v.Model; buf[4]=v.AgreementNo;
            buf[5]=v.CustomerName; buf[6]=v.CustomerContact; buf[7]=v.CustomerAddress;
            buf[8]=v.Financer; buf[9]=v.BranchName; buf[10]=v.Bucket; buf[11]=v.Gv; buf[12]=v.Od; buf[13]=v.Seasoning;
            buf[14]=v.TbrFlag; buf[15]=v.Sec9; buf[16]=v.Sec17;
            buf[17]=v.Level1; buf[18]=v.Level1Contact; buf[19]=v.Level2; buf[20]=v.Level2Contact;
            buf[21]=v.Level3; buf[22]=v.Level3Contact; buf[23]=v.Level4; buf[24]=v.Level4Contact;
            buf[25]=v.SenderMail1; buf[26]=v.SenderMail2; buf[27]=v.ExecutiveName;
            buf[28]=v.Pos; buf[29]=v.Toss; buf[30]=v.Remark; buf[31]=v.Region; buf[32]=v.Area; buf[33]=v.CreatedOn;
            WriteRow(sw, r++, buf, styleIndex: 0);
        }

        sw.Write("</sheetData></worksheet>");
    }

    // ── Row writer (inline strings) ─────────────────────────────────────────
    private static void WriteRow(StreamWriter sw, int rowNum, string?[] cells, int styleIndex)
    {
        sw.Write("<row r=\""); sw.Write(rowNum); sw.Write("\">");
        for (int c = 0; c < cells.Length; c++)
        {
            var val = cells[c];
            if (string.IsNullOrEmpty(val)) continue;   // skip empty cells — smaller + faster
            sw.Write("<c r=\""); sw.Write(ColLetter(c)); sw.Write(rowNum);
            if (styleIndex != 0) { sw.Write("\" s=\""); sw.Write(styleIndex); }
            sw.Write("\" t=\"inlineStr\"><is><t xml:space=\"preserve\">");
            WriteEscaped(sw, val);
            sw.Write("</t></is></c>");
        }
        sw.Write("</row>");
    }

    private static void WriteEscaped(StreamWriter sw, string s)
    {
        foreach (var ch in s)
        {
            switch (ch)
            {
                case '&': sw.Write("&amp;");  break;
                case '<': sw.Write("&lt;");   break;
                case '>': sw.Write("&gt;");   break;
                case '"': sw.Write("&quot;"); break;
                default:
                    // Strip control chars that are illegal in XML 1.0.
                    if (ch >= 0x20 || ch == '\t' || ch == '\n' || ch == '\r') sw.Write(ch);
                    break;
            }
        }
    }

    // 0-based column index → Excel column letters (A, B, ..., Z, AA, AB...).
    private static string ColLetter(int index)
    {
        var sb = new StringBuilder(3);
        index++;
        while (index > 0)
        {
            int rem = (index - 1) % 26;
            sb.Insert(0, (char)('A' + rem));
            index = (index - 1) / 26;
        }
        return sb.ToString();
    }

    private static void WriteEntry(ZipArchive zip, string path, string content)
    {
        var e = zip.CreateEntry(path, CompressionLevel.Fastest);
        using var w = new StreamWriter(e.Open(), new UTF8Encoding(false));
        w.Write(content);
    }

    private static string SanitizeSheetName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Records";
        foreach (var c in new[] { '\\', '/', '?', '*', '[', ']', ':' }) name = name.Replace(c, ' ');
        name = name.Trim();
        if (name.Length == 0) return "Records";
        return name.Length > 31 ? name[..31] : name;
    }

    // ── Static OpenXML parts ────────────────────────────────────────────────
    private const string ContentTypesXml =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
        "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
        "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
        "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
        "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
        "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
        "</Types>";

    private const string RelsXml =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
        "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
        "</Relationships>";

    private static string WorkbookXml(string sheetName) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" " +
        "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
        "<sheets><sheet name=\"" + XmlAttr(sheetName) + "\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>";

    private const string WorkbookRelsXml =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
        "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
        "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
        "</Relationships>";

    // 2 fonts (normal, bold) → cellXfs index 1 = bold header.
    private const string StylesXml =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
        "<fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font>" +
        "<font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts>" +
        "<fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill>" +
        "<fill><patternFill patternType=\"gray125\"/></fill></fills>" +
        "<borders count=\"1\"><border/></borders>" +
        "<cellStyleXfs count=\"1\"><xf/></cellStyleXfs>" +
        "<cellXfs count=\"2\"><xf/><xf fontId=\"1\" applyFont=\"1\"/></cellXfs>" +
        "</styleSheet>";

    private static string XmlAttr(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
