using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit.Addin._2024.Services;
using Revit.Addin._2024.Tables.Exporters;
using Revit.Addin._2024.Tables.Services;
using Revit.Addin._2024.Tables.Templates;
using Revit.Addin._2024.Tables.UI;

namespace Revit.Addin._2024.Tables.Commands
{
    /// <summary>
    /// Ribbon entry point. Pipeline:
    ///  1. Show dialog → pick template + format.
    ///  2. Collect elements in active view that match template, group by Bateilgruppe,
    ///     sort by Bauteilnummer.
    ///  3. Hand <see cref="Models.TableData"/> to the chosen <see cref="ITableExporter"/>.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class GenerateTableCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiDoc = commandData.Application.ActiveUIDocument;
            if (uiDoc == null)
            {
                message = "Kein aktives Revit-Dokument.";
                return Result.Failed;
            }

            var doc = uiDoc.Document;

            var templates = TableTemplateRegistry.GetAll();
            var exporters = TableExporterRegistry.GetAll();
            if (templates.Count == 0 || exporters.Count == 0)
            {
                message = "Keine Tabellenvorlagen oder Exportformate registriert.";
                return Result.Failed;
            }

            ITableTemplate template;
            ITableExporter exporter;
            IReadOnlyList<string> excelBauteilnummern;
            IReadOnlyList<string> excludedBauteilnummern;
            using (var dlg = new TableExportDialog(
                       templates,
                       exporters,
                       t => TableDataBuilder.GetActiveViewSortKeys(doc, t)))
            {
                if (dlg.ShowDialog() != DialogResult.OK)
                    return Result.Cancelled;

                template = dlg.SelectedTemplate;
                exporter = dlg.SelectedExporter;
                excelBauteilnummern = dlg.SelectedExcelBauteilnummern;
                excludedBauteilnummern = dlg.SelectedExcludedBauteilnummern;
            }

            if (template == null || exporter == null)
                return Result.Cancelled;

            // Build the table data — mixing the active-view elements with any Bauteilnummern
            // the user picked from the loaded Excel sheet, minus any the user chose to exclude.
            var data = TableDataBuilder.Build(
                doc, template, excelBauteilnummern, ExcelParser.FamilyData, excludedBauteilnummern);
            if (data.TotalRowCount == 0)
            {
                TaskDialog.Show(template.Title,
                    $"Es wurden weder in der aktiven Ansicht noch in der Excel-Auswahl Elemente " +
                    $"mit dem Parameter '{template.SortParameterName}' gefunden.");
                return Result.Cancelled;
            }

            var ctx = new ExportContext { Document = doc, UIDocument = uiDoc };

            // File-output exporters need a target path.
            if (exporter.RequiresOutputPath)
            {
                using (var sfd = new SaveFileDialog
                {
                    Title = $"{template.Title} – {exporter.FormatName}",
                    Filter = exporter.FileFilter ?? "Alle Dateien (*.*)|*.*",
                    FileName = SanitizeFileName(template.Title) + (exporter.FileExtension ?? string.Empty),
                    OverwritePrompt = true,
                    AddExtension = true,
                    DefaultExt = (exporter.FileExtension ?? string.Empty).TrimStart('.'),
                })
                {
                    if (sfd.ShowDialog() != DialogResult.OK)
                        return Result.Cancelled;
                    ctx.OutputPath = sfd.FileName;
                }
            }

            ExportResult result;
            try
            {
                result = exporter.Export(data, ctx);
            }
            catch (Exception ex)
            {
                message = $"{exporter.FormatName}-Export fehlgeschlagen: {ex.Message}";
                return Result.Failed;
            }

            if (!result.Success)
            {
                message = result.Message ?? "Export fehlgeschlagen.";
                return Result.Failed;
            }

            TaskDialog.Show(template.Title,
                result.Message ?? $"{exporter.FormatName} erfolgreich erstellt.");
            return Result.Succeeded;
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Tabelle";
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }
    }
}
