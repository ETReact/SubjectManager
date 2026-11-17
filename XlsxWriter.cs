using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security;
using System.Text;

namespace SubjectManager
{
    /// <summary>
    /// Minimal .xlsx writer (one worksheet) with no external dependencies, so the plugin stays a single DLL.
    /// Bold, frozen header row with auto-filter; strings, numbers and booleans; fixed column widths.
    /// </summary>
    internal static class XlsxWriter
    {
        public static void Write(string path, string sheetName, IList<string> headers, IList<double> widths, IEnumerable<object[]> rows)
        {
            var rowList = rows.ToList();
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                Add(zip, "[Content_Types].xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                    "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                    "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                    "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
                    "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
                    "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
                    "</Types>");

                Add(zip, "_rels/.rels",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                    "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
                    "</Relationships>");

                var name = SanitizeSheetName(sheetName);
                Add(zip, "xl/workbook.xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" " +
                    "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
                    $"<sheets><sheet name=\"{Escape(name)}\" sheetId=\"1\" r:id=\"rId1\"/></sheets>" +
                    "<definedNames>" +
                    $"<definedName name=\"_xlnm._FilterDatabase\" localSheetId=\"0\" hidden=\"1\">'{Escape(name.Replace("'", "''"))}'!$A$1:${ColumnName(headers.Count - 1)}${rowList.Count + 1}</definedName>" +
                    "</definedNames>" +
                    "</workbook>");

                Add(zip, "xl/_rels/workbook.xml.rels",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                    "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
                    "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
                    "</Relationships>");

                // Style 0 = normal, 1 = bold header with grey fill and bottom border
                Add(zip, "xl/styles.xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
                    "<fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts>" +
                    "<fills count=\"3\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill>" +
                    "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFDDEBF7\"/><bgColor indexed=\"64\"/></patternFill></fill></fills>" +
                    "<borders count=\"2\"><border><left/><right/><top/><bottom/><diagonal/></border>" +
                    "<border><left/><right/><top/><bottom style=\"thin\"><color rgb=\"FF8EA9DB\"/></bottom><diagonal/></border></borders>" +
                    "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
                    "<cellXfs count=\"2\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
                    "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"2\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\"/></cellXfs>" +
                    "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>" +
                    "</styleSheet>");

                var sb = new StringBuilder();
                sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
                sb.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
                sb.Append("<sheetViews><sheetView workbookViewId=\"0\">");
                sb.Append("<pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/>");
                sb.Append("<selection pane=\"bottomLeft\" activeCell=\"A2\" sqref=\"A2\"/>");
                sb.Append("</sheetView></sheetViews>");
                sb.Append("<cols>");
                for (var c = 0; c < headers.Count; c++)
                {
                    var w = widths != null && c < widths.Count ? widths[c] : 20;
                    sb.Append($"<col min=\"{c + 1}\" max=\"{c + 1}\" width=\"{w.ToString(CultureInfo.InvariantCulture)}\" customWidth=\"1\"/>");
                }
                sb.Append("</cols><sheetData>");

                AppendRow(sb, 1, headers.Cast<object>().ToArray(), 1);
                for (var r = 0; r < rowList.Count; r++) AppendRow(sb, r + 2, rowList[r], 0);

                sb.Append("</sheetData>");
                sb.Append($"<autoFilter ref=\"A1:{ColumnName(headers.Count - 1)}{rowList.Count + 1}\"/>");
                sb.Append("</worksheet>");
                Add(zip, "xl/worksheets/sheet1.xml", sb.ToString());
            }
        }

        private static void AppendRow(StringBuilder sb, int rowNumber, object[] values, int style)
        {
            sb.Append($"<row r=\"{rowNumber}\">");
            for (var c = 0; c < values.Length; c++)
            {
                var cellRef = ColumnName(c) + rowNumber;
                var s = style > 0 ? $" s=\"{style}\"" : string.Empty;
                switch (values[c])
                {
                    case null:
                        if (style > 0) sb.Append($"<c r=\"{cellRef}\"{s}/>");
                        break;
                    case bool b:
                        sb.Append($"<c r=\"{cellRef}\"{s} t=\"b\"><v>{(b ? 1 : 0)}</v></c>");
                        break;
                    case int _:
                    case long _:
                    case double _:
                    case decimal _:
                        sb.Append($"<c r=\"{cellRef}\"{s}><v>{Convert.ToString(values[c], CultureInfo.InvariantCulture)}</v></c>");
                        break;
                    default:
                        var text = Escape(StripInvalidXmlChars(values[c].ToString()));
                        var preserve = text.Length > 0 && (char.IsWhiteSpace(text[0]) || char.IsWhiteSpace(text[text.Length - 1]))
                            ? " xml:space=\"preserve\"" : string.Empty;
                        sb.Append($"<c r=\"{cellRef}\"{s} t=\"inlineStr\"><is><t{preserve}>{text}</t></is></c>");
                        break;
                }
            }
            sb.Append("</row>");
        }

        /// <summary>0 → A, 25 → Z, 26 → AA ...</summary>
        private static string ColumnName(int index)
        {
            var name = string.Empty;
            index++;
            while (index > 0)
            {
                var m = (index - 1) % 26;
                name = (char)('A' + m) + name;
                index = (index - m) / 26;
            }
            return name;
        }

        private static string SanitizeSheetName(string name)
        {
            var clean = new string((name ?? "Sheet1").Where(ch => "[]:*?/\\".IndexOf(ch) < 0).ToArray()).Trim('\'');
            if (clean.Length == 0) clean = "Sheet1";
            return clean.Length > 31 ? clean.Substring(0, 31) : clean;
        }

        private static string Escape(string s) => SecurityElement.Escape(s) ?? string.Empty;

        private static string StripInvalidXmlChars(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var sb = new StringBuilder(s.Length);
            foreach (var ch in s)
            {
                if (ch == 0x9 || ch == 0xA || ch == 0xD || (ch >= 0x20 && ch <= 0xD7FF) || (ch >= 0xE000 && ch <= 0xFFFD) || char.IsSurrogate(ch))
                    sb.Append(ch);
            }
            return sb.ToString();
        }

        private static void Add(ZipArchive zip, string entryName, string content)
        {
            var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
            using (var w = new StreamWriter(entry.Open(), new UTF8Encoding(false)))
                w.Write(content);
        }
    }
}
