using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TableData = Revit.Addin._2027.Tables.Models.TableData;

namespace Revit.Addin._2027.Tables.Exporters
{
    /// <summary>
    /// Generates the drafting view via <see cref="RevitDraftingViewExporter"/>, then uses
    /// Revit's native PDF export (Revit 2022+) to write the view to a PDF file.
    ///
    /// We reuse the drafting-view exporter on purpose: the on-paper layout stays identical
    /// across "in-Revit" and "PDF" outputs.
    /// </summary>
    public class PdfExporter : ITableExporter
    {
        public string FormatName => "PDF";

        public bool RequiresOutputPath => true;

        public string FileExtension => ".pdf";

        public string FileFilter => "PDF-Datei (*.pdf)|*.pdf";

        public ExportResult Export(TableData data, ExportContext context)
        {
            if (data == null) return Failure("Keine Daten zum Exportieren.");
            if (context?.Document == null) return Failure("Kein aktives Revit-Dokument.");
            if (string.IsNullOrWhiteSpace(context?.OutputPath))
                return Failure("Kein Ausgabepfad angegeben.");

            // Remember the active view: the drafting exporter switches to the view it
            // creates, and Revit refuses to delete the active view — so we restore this one
            // before removing the temporary view during cleanup.
            ElementId previousActiveViewId =
                context.UIDocument?.ActiveView?.Id ?? ElementId.InvalidElementId;

            // Step 1 — produce the drafting view inside the document.
            var inner = new RevitDraftingViewExporter();
            var draftingResult = inner.Export(data, context);
            if (!draftingResult.Success)
                return draftingResult;

            // Find the view we just created (its name was returned in ProducedPath).
            ElementId viewId = FindViewIdByName(context.Document, draftingResult.ProducedPath);
            if (viewId == null || viewId == ElementId.InvalidElementId)
                return Failure("Erstellte Plansicht konnte nicht gefunden werden.");

            try
            {
                // Step 2 — call Revit's PDF export for that single view.
                string folder = Path.GetDirectoryName(context.OutputPath);
                string fileName = Path.GetFileNameWithoutExtension(context.OutputPath);
                if (string.IsNullOrEmpty(folder)) folder = Path.GetTempPath();
                if (string.IsNullOrEmpty(fileName)) fileName = data.Title ?? "Tabelle";

                try
                {
                    if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

                    var options = new PDFExportOptions
                    {
                        FileName = fileName,
                        Combine = true,
                        StopOnError = true,
                        PaperFormat = ExportPaperFormat.Default,
                        PaperOrientation = PageOrientationType.Landscape,
                        HideCropBoundaries = true,
                        HideScopeBoxes = true,
                        HideReferencePlane = true,
                        HideUnreferencedViewTags = true,
                        MaskCoincidentLines = true,
                    };

                    bool ok = context.Document.Export(folder, new List<ElementId> { viewId }, options);
                    if (!ok)
                        return Failure("Revit hat den PDF-Export abgelehnt.");
                }
                catch (Exception ex)
                {
                    return Failure($"PDF-Export fehlgeschlagen: {ex.Message}");
                }

                string producedPath = Path.Combine(folder, fileName + ".pdf");
                return new ExportResult
                {
                    Success = true,
                    Message = $"PDF erstellt: {producedPath}",
                    ProducedPath = producedPath,
                };
            }
            finally
            {
                // The drafting view was only a vehicle for the PDF; remove it so repeated
                // exports don't accumulate "Einbauteile (2)", "(3)"… views in the model.
                TryDeleteTemporaryView(context.UIDocument, context.Document, viewId, previousActiveViewId);
            }
        }

        /// <summary>
        /// Removes the temporary drafting view created for the PDF. Best-effort: restores the
        /// view that was active before export (Revit cannot delete the active view), then
        /// deletes the temp view in its own transaction. Never throws — a lingering view must
        /// not turn a successful PDF into a failure.
        /// </summary>
        private static void TryDeleteTemporaryView(
            UIDocument uiDoc, Document doc, ElementId viewId, ElementId previousActiveViewId)
        {
            if (doc == null || viewId == null || viewId == ElementId.InvalidElementId)
                return;

            try
            {
                if (uiDoc?.ActiveView != null && uiDoc.ActiveView.Id == viewId
                    && previousActiveViewId != null
                    && previousActiveViewId != ElementId.InvalidElementId)
                {
                    if (doc.GetElement(previousActiveViewId) is View prev
                        && prev.IsValidObject && prev.Id != viewId)
                    {
                        uiDoc.ActiveView = prev;
                    }
                }

                using (var tx = new Transaction(doc, "Temporäre Tabellen-Plansicht entfernen"))
                {
                    tx.Start();
                    doc.Delete(viewId);
                    tx.Commit();
                }
            }
            catch
            {
                // Best-effort cleanup — never fail the export because the temp view lingered.
            }
        }

        private static ElementId FindViewIdByName(Document doc, string viewName)
        {
            if (string.IsNullOrEmpty(viewName)) return ElementId.InvalidElementId;
            var collector = new FilteredElementCollector(doc).OfClass(typeof(View));
            foreach (View v in collector)
            {
                if (string.Equals(v.Name, viewName, StringComparison.OrdinalIgnoreCase))
                    return v.Id;
            }
            return ElementId.InvalidElementId;
        }

        private static ExportResult Failure(string msg) =>
            new ExportResult { Success = false, Message = msg };
    }
}
