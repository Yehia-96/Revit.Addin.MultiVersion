using System;
using ClosedXML.Excel;
using Revit.Addin._2027.Tables.Models;

namespace Revit.Addin._2027.Tables.Exporters
{
    /// <summary>
    /// Writes a TableData to an .xlsx workbook using ClosedXML. The layout matches the
    /// drafting-view output: title row, header row, then alternating section header rows
    /// + data rows.
    /// </summary>
    public class ExcelExporter : ITableExporter
    {
        public string FormatName => "Excel (.xlsx)";

        public bool RequiresOutputPath => true;

        public string FileExtension => ".xlsx";

        public string FileFilter => "Excel-Arbeitsmappe (*.xlsx)|*.xlsx";

        public ExportResult Export(TableData data, ExportContext context)
        {
            if (data == null) return Failure("Keine Daten zum Exportieren.");
            if (context == null || string.IsNullOrWhiteSpace(context.OutputPath))
                return Failure("Kein Ausgabepfad angegeben.");

            try
            {
                using (var workbook = new XLWorkbook())
                {
                    var ws = workbook.Worksheets.Add(SafeSheetName(data.Title));
                    int colCount = data.Columns.Count;
                    int row = 1;

                    // Three-step grey hierarchy matching the drafting view exporter:
                    // title (darkest) → column headers (medium) → group headers (lightest).
                    const string TitleGrey   = "#A0A0A0";
                    const string HeaderGrey  = "#C8C8C8";
                    const string SectionGrey = "#E6E6E6";

                    // Title row spanning all columns.
                    var titleRange = ws.Range(row, 1, row, colCount);
                    titleRange.Merge();
                    titleRange.Value = data.Title ?? string.Empty;
                    titleRange.Style.Font.Bold = true;
                    titleRange.Style.Font.FontSize = 12;
                    titleRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    titleRange.Style.Fill.BackgroundColor = XLColor.FromHtml(TitleGrey);
                    ApplyAllBorders(titleRange);
                    row++;

                    // Header row.
                    for (int c = 0; c < colCount; c++)
                    {
                        var cell = ws.Cell(row, c + 1);
                        cell.Value = data.Columns[c].Header ?? string.Empty;
                        cell.Style.Font.Bold = true;
                        cell.Style.Fill.BackgroundColor = XLColor.FromHtml(HeaderGrey);
                        cell.Style.Alignment.WrapText = true;
                        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    }
                    ApplyAllBorders(ws.Range(row, 1, row, colCount));
                    row++;

                    // Sections.
                    foreach (var section in data.Sections)
                    {
                        var sectionRange = ws.Range(row, 1, row, colCount);
                        sectionRange.Merge();
                        sectionRange.Value = section.Name ?? string.Empty;
                        sectionRange.Style.Font.Bold = true;
                        sectionRange.Style.Fill.BackgroundColor = XLColor.FromHtml(SectionGrey);
                        ApplyAllBorders(sectionRange);
                        row++;

                        foreach (var dataRow in section.Rows)
                        {
                            for (int c = 0; c < colCount; c++)
                            {
                                var col = data.Columns[c];
                                var cell = ws.Cell(row, c + 1);
                                dataRow.Cells.TryGetValue(col.Header, out var value);
                                cell.Value = value ?? string.Empty;
                                cell.Style.Alignment.WrapText = true;
                                cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                            }
                            ApplyAllBorders(ws.Range(row, 1, row, colCount));
                            row++;
                        }
                    }

                    // Column widths derived from template (mm -> Excel character width).
                    for (int c = 0; c < colCount; c++)
                    {
                        ws.Column(c + 1).Width = MmToExcelWidth(data.Columns[c].Width);
                    }

                    workbook.SaveAs(context.OutputPath);
                }

                return new ExportResult
                {
                    Success = true,
                    Message = $"Tabelle exportiert: {context.OutputPath}",
                    ProducedPath = context.OutputPath,
                };
            }
            catch (Exception ex)
            {
                return Failure($"Excel-Export fehlgeschlagen: {ex.Message}");
            }
        }

        private static void ApplyAllBorders(IXLRange range)
        {
            range.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            range.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        }

        private static double MmToExcelWidth(double mm)
        {
            // Rough mapping; Excel column "width" is in characters, ~2.1mm per unit.
            if (mm <= 0) return 12.0;
            return Math.Max(6.0, mm / 2.1);
        }

        private static string SafeSheetName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Tabelle";
            // Excel sheet name: max 31 chars, no \ / ? * [ ] :
            var trimmed = name.Length > 31 ? name.Substring(0, 31) : name;
            foreach (var bad in new[] { '\\', '/', '?', '*', '[', ']', ':' })
                trimmed = trimmed.Replace(bad, '_');
            return trimmed;
        }

        private static ExportResult Failure(string msg) =>
            new ExportResult { Success = false, Message = msg };
    }
}
