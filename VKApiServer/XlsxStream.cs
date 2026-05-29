using System.IO.Compression;
using System.Text;
using MySqlConnector;

namespace VKApiServer;

// Streams a real .xlsx straight from a MySqlDataReader into an output stream
// (the HTTP response body), writing rows as they're read from MySQL. An .xlsx
// is a ZIP of XML parts; we emit inline-string worksheet XML so there's no
// shared-string dedup pass and no in-memory document model. Combined with a
// forward-only DataReader this means a multi-hundred-thousand-row export uses
// almost no server memory and starts downloading immediately — the client just
// saves the bytes, so there's no giant-JSON fetch + client-side assembly that
// made the old paginated export slow.
internal static class XlsxStream
{
    // Writes a full workbook with one sheet. `reader` must already be positioned
    // before the first row (freshly executed). `colCount` columns are read per
    // row via GetValue(i).ToString().
    public static async Task WriteAsync(
        Stream dest, string sheetName, string[] headers,
        MySqlDataReader reader, int colCount, CancellationToken ct = default)
    {
        var safeSheet = SanitizeSheetName(sheetName);
        // leaveOpen so ASP.NET owns the response stream lifecycle.
        using var zip = new ZipArchive(dest, ZipArchiveMode.Create, leaveOpen: true);

        WriteText(zip, "[Content_Types].xml", ContentTypes);
        WriteText(zip, "_rels/.rels", Rels);
        WriteText(zip, "xl/workbook.xml", Workbook(safeSheet));
        WriteText(zip, "xl/_rels/workbook.xml.rels", WorkbookRels);
        WriteText(zip, "xl/styles.xml", Styles);

        var sheetEntry = zip.CreateEntry("xl/worksheets/sheet1.xml", CompressionLevel.Fastest);
        await using var s = sheetEntry.Open();
        using var sw = new StreamWriter(s, new UTF8Encoding(false), 1 << 16);

        await sw.WriteAsync("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        await sw.WriteAsync("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");

        // Header row (style 1 = bold).
        WriteRow(sw, 1, headers, headers.Length, styled: true, getCell: i => headers[i]);

        int rowNum = 2;
        while (await reader.ReadAsync(ct))
        {
            WriteRow(sw, rowNum++, null, colCount, styled: false,
                getCell: i => reader.IsDBNull(i) ? "" : reader.GetValue(i)?.ToString() ?? "");
        }

        await sw.WriteAsync("</sheetData></worksheet>");
        await sw.FlushAsync();
    }

    private static void WriteRow(StreamWriter sw, int rowNum, string[]? _unused, int colCount,
                                 bool styled, Func<int, string> getCell)
    {
        sw.Write("<row r=\""); sw.Write(rowNum); sw.Write("\">");
        for (int c = 0; c < colCount; c++)
        {
            var val = getCell(c);
            if (string.IsNullOrEmpty(val)) continue;
            sw.Write("<c r=\""); sw.Write(ColLetter(c)); sw.Write(rowNum);
            if (styled) sw.Write("\" s=\"1");
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
                    if (ch >= 0x20 || ch == '\t' || ch == '\n' || ch == '\r') sw.Write(ch);
                    break;
            }
        }
    }

    private static string ColLetter(int index)
    {
        var sb = new StringBuilder(3);
        index++;
        while (index > 0) { int r = (index - 1) % 26; sb.Insert(0, (char)('A' + r)); index = (index - 1) / 26; }
        return sb.ToString();
    }

    private static void WriteText(ZipArchive zip, string path, string content)
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

    private const string ContentTypes =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
        "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
        "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
        "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
        "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
        "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
        "</Types>";

    private const string Rels =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
        "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
        "</Relationships>";

    private static string Workbook(string sheetName) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" " +
        "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
        "<sheets><sheet name=\"" + XmlAttr(sheetName) + "\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>";

    private const string WorkbookRels =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
        "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
        "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
        "</Relationships>";

    private const string Styles =
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
