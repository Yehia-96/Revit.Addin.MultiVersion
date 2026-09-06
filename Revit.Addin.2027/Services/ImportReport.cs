using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

public sealed class ImportReport
{
    public int BoundParameters { get; set; }
    public int WrittenValues { get; set; }

    public List<string> Info { get; } = new List<string>();
    public List<string> Warnings { get; } = new List<string>();
    public List<string> Errors { get; } = new List<string>();

    public bool HasIssues => Errors.Count > 0 || Warnings.Count > 0;

    public void AddInfo(string s) { if (!string.IsNullOrWhiteSpace(s)) Info.Add(s); }
    public void AddWarn(string s) { if (!string.IsNullOrWhiteSpace(s)) Warnings.Add(s); }
    public void AddError(string s) { if (!string.IsNullOrWhiteSpace(s)) Errors.Add(s); }
}

public static class ReportUi
{
    public static void ShowImportReportTaskDialog(ImportReport r, string title = "Material Parameter Import")
    {
        string status = r.Errors.Count > 0 ? "Completed with errors"
                      : r.Warnings.Count > 0 ? "Completed with warnings"
                      : "Completed successfully";

        string mainContent =
            $"Shared parameters bound: {r.BoundParameters}\n" +
            $"Values written: {r.WrittenValues}\n" +
            $"Warnings: {r.Warnings.Count}\n" +
            $"Errors: {r.Errors.Count}";

        // Put detailed log in ExpandedContent (trim to avoid huge dialogs)
        var sb = new StringBuilder();

        void AppendSection(string header, List<string> lines)
        {
            if (lines.Count == 0) return;
            sb.AppendLine(header);
            foreach (var line in lines.Take(80)) sb.AppendLine("• " + line);
            if (lines.Count > 80) sb.AppendLine($"• …({lines.Count - 80} more)");
            sb.AppendLine();
        }

        AppendSection("Errors", r.Errors);
        AppendSection("Warnings", r.Warnings);
        AppendSection("Info", r.Info);

        var td = new TaskDialog(title)
        {
            TitleAutoPrefix = false,
            MainInstruction = status,
            MainContent = mainContent,
            ExpandedContent = sb.Length == 0 ? "No additional details." : sb.ToString(),
            FooterText = $"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}"
        };

        td.CommonButtons = TaskDialogCommonButtons.Ok;
        td.DefaultButton = TaskDialogResult.Ok;
        td.Show();
    }
}
