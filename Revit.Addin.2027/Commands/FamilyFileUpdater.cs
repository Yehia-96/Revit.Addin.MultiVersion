using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit.Addin._2027.Services.FamilyUpdate;
using Revit.Addin._2027.UI;
using RevitApp = Autodesk.Revit.ApplicationServices.Application;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;

namespace Revit.Addin._2027.Commands
{
    /// <summary>
    /// Batch-updates the .rfa files in a directory tree.
    /// </summary>
    /// <remarks>
    /// The command itself only wires the pieces together: the options dialog collects the run
    /// settings, <see cref="FamilyUpdateRunner"/> does the work, and the progress form drives it.
    /// It deliberately does not touch — or require — an open project document, and it never opens
    /// a transaction: every family is processed as its own standalone document.
    /// </remarks>
    [Transaction(TransactionMode.Manual)]
    public class FamilyFileUpdater : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiApp = commandData.Application;
            RevitApp app = uiApp.Application;
            var owner = new RevitWindowOwner(uiApp.MainWindowHandle);

            try
            {
                FamilyUpdateOptions options;
                IList<IFamilyFileStep> steps;

                using (var optionsForm = new FamilyUpdateOptionsForm(FamilyUpdateStepCatalog.CreateDefault()))
                {
                    if (optionsForm.ShowDialog(owner) != DialogResult.OK)
                    {
                        return Result.Cancelled;
                    }

                    options = optionsForm.Options;
                    steps = optionsForm.SelectedSteps;
                }

                var runner = new FamilyUpdateRunner(app, options, steps);

                FamilyUpdateReport report;
                using (var progressForm = new FamilyUpdateProgressForm(runner))
                {
                    DialogResult progressResult = progressForm.ShowDialog(owner);

                    if (progressForm.FoundNoFiles)
                    {
                        TaskDialog.Show(
                            "Update Family Files",
                            "No family files were found in the selected folder."
                            + Environment.NewLine + Environment.NewLine
                            + "Check the folder, and whether \"Include subfolders\" should be ticked.");
                        return Result.Cancelled;
                    }

                    if (progressResult != DialogResult.OK || progressForm.Report == null)
                    {
                        return Result.Failed;
                    }

                    report = progressForm.Report;
                }

                ShowSummary(report, runner.ReportFolder);

                // Individual file failures are reported in the summary and the CSV; they do not
                // make the command itself a failure, which would only show Revit's generic error.
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[ERROR] FamilyFileUpdater: " + ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static void ShowSummary(FamilyUpdateReport report, string reportFolder)
        {
            string csvPath = report.TryWriteCsv(reportFolder);

            var dialog = new TaskDialog("Update Family Files")
            {
                MainInstruction = BuildHeadline(report),
                MainContent = report.BuildSummary(),
                CommonButtons = TaskDialogCommonButtons.Close
            };

            if (csvPath != null)
            {
                dialog.AddCommandLink(
                    TaskDialogCommandLinkId.CommandLink1,
                    "Open the report",
                    csvPath);
            }

            if (dialog.Show() == TaskDialogResult.CommandLink1 && csvPath != null)
            {
                RevealInExplorer(csvPath);
            }
        }

        private static string BuildHeadline(FamilyUpdateReport report)
        {
            if (report.WasCancelled)
            {
                return string.Format("Cancelled after updating {0} of {1} files.",
                    report.UpdatedCount, report.TotalCount);
            }

            if (report.FailedCount > 0)
            {
                return string.Format("{0} files updated, {1} failed.",
                    report.UpdatedCount, report.FailedCount);
            }

            return string.Format("{0} family files updated.", report.UpdatedCount);
        }

        private static void RevealInExplorer(string path)
        {
            try
            {
                Process.Start("explorer.exe", "/select,\"" + Path.GetFullPath(path) + "\"");
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[WARN] Could not open the report location: " + ex.Message);
            }
        }
    }
}
