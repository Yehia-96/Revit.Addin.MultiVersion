using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit.Addin._2024.Helpers;
using Revit.Addin._2024.Services;
using Revit.Addin._2024.Services.Planungsdatenbank;
using Revit.Addin._2024.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Windows.Forms;
using RevitApp = Autodesk.Revit.ApplicationServices.Application;
namespace Revit.Addin._2024.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class AddParametersToFamilies : IExternalCommand
    {
        // 🟢 Static fields to store state across command executions
        private static bool _firstRun = true;
        private string _excelFilePath;

        public static string _sharedParamPath;
        // Which reader runs is decided by WorkbookLoader from the workbook itself. This replaces
        // an isNew flag that was never assigned, which made first-run loading and the menu's
        // "Add Excel" button use two different readers.
        // 🟢 This command is designed to be run once per session, so we use a static field to track that
        public Result Execute(ExternalCommandData cdata, ref string message, ElementSet elements)
        {
            UIApplication uiApp = cdata.Application;
            UIDocument uiDoc = uiApp.ActiveUIDocument;
            Document doc = uiDoc.Document;
            RevitApp app = uiApp.Application;

            try
            {
                // Only load files once per Revit session to prevent repeat prompts
                if (_firstRun)
                {

                    string defaultFolderPath = Environment.ExpandEnvironmentVariables(
                        @"%USERPROFILE%\Müller+Hereth GmbH\1761-01 B10-Ulm - Documents\Objektplanung");

                    var openExcel = new OpenFileDialog
                    {
                        Filter = "Excel Files|*.xlsx;*.xlsm;*.xls",
                        Title = "Select Excel File with Parameter Data",
                        InitialDirectory = defaultFolderPath
                    };
                    // _excelFilePath = ExcelParser.GetExcelFilePathOrThrow();
                    _excelFilePath = null;
                    //bool isOpenForRead = ExcelParser.CanOpenForRead(_excelFilePath);
                    if (_excelFilePath is null)
                    {
                        if (openExcel.ShowDialog() != DialogResult.OK)
                        {
                            TaskDialog.Show("Missing File", "You must select an Excel file to continue.");
                            return Result.Cancelled;
                        }
                        _excelFilePath = openExcel.FileName;

                    }

                    var openTxt = new OpenFileDialog
                    {
                        Filter = "Text Files|*.txt",
                        Title = "Select Shared Parameter File"
                    };
                    if (openTxt.ShowDialog() != DialogResult.OK)
                    {
                        TaskDialog.Show("Missing File", "You must select a Shared Parameter file to continue.");
                        return Result.Cancelled;
                    }
                    _sharedParamPath = openTxt.FileName;

                    WorkbookLoader.Load(_excelFilePath);
                    // Silent on a clean load, as before; a problem in the workbook is shown.
                    if (WorkbookLoader.Problems.Count > 0)
                        TaskDialog.Show("Workbook loaded with problems", WorkbookLoader.Summary());

                    // shared parameter file
                    SharedParameterSeeder.EnsureParametersExist(
                        app,
                        new[] { ExcelParser.ParameterDefinitions, ExcelParser.MaterialParameterDefinitions },
                        _sharedParamPath);

                    _firstRun = false;
                }

                // The menu's selection list is by Bauteilnummer. Planungsdatenbank keys carry the
                // sub-number too ("1101-1"), so read the number out of either shape; several
                // sub-elements of one Bauteil show as that Bauteil once.
                var selectedBauteilnummer = new List<int>();
                var seen = new HashSet<int>();

                foreach (var key in ExcelParser.FamilyData.Keys)
                {
                    int bn, sub;
                    bool hasSub;
                    if (PlanungsdatenbankParser.TryParseFamilyKey(key, out bn, out hasSub, out sub))
                    {
                        if (seen.Add(bn)) selectedBauteilnummer.Add(bn);
                    }
                    else
                        System.Diagnostics.Debug.WriteLine(
                            $"[WARN] Non-numeric Bauteilnummer key in FamilyData: '{key}'");
                }

                // Create the UI object to show the main menu dialog
                AutoParameterUI ui = new AutoParameterUI(selectedBauteilnummer);
                // Flag to track if any work was done
                bool didWork = false;
                // Assign the event handlers for the UI buttons - these will be called when the user interacts with the dialog
                // 🟢 OnAddExcelClicked is triggered when the user clicks the "Add Excel" button
                ui.OnAddExcelClicked = () =>
                {
                    var openExcel = new OpenFileDialog
                    {
                        Filter = "Excel Files|*.xlsx;*.xlsm;*.xls",
                        Title = "Select Excel File with Parameter Data"
                    };
                    if (openExcel.ShowDialog() == DialogResult.OK)
                    {
                        _excelFilePath = openExcel.FileName;
                        WorkbookLoader.Load(_excelFilePath);

                        // Loading here could switch formats, and a Planungsdatenbank brings
                        // parameters the shared-parameter file has never seen. Seed them now, or
                        // "Add Parameters" would find no definitions for them.
                        if (!string.IsNullOrWhiteSpace(_sharedParamPath))
                        {
                            SharedParameterSeeder.EnsureParametersExist(
                                app,
                                new[] { ExcelParser.ParameterDefinitions, ExcelParser.MaterialParameterDefinitions },
                                _sharedParamPath);
                        }

                        TaskDialog.Show("Info", WorkbookLoader.Summary());
                    }
                };
                // 🟢 OnAddSharedParamClicked is triggered when the user clicks the "Add Parameters" button
                ui.OnAutoAddClicked = () =>
                {

                    Tuple<int, int> count = SeedProjectElementsParameters.AddParametersToElements(
                        doc,
                        ExcelParser.ParameterDefinitions,
                        ExcelParser.FamilyData,
                        app,
                        ui.SpecificSelection.SelectedBauteilnummern,
                        _sharedParamPath,
                        WorkbookLoader.KeysIncludeSubNumber);

                    TaskDialog.Show("Info", count.Item1 == 0 && count.Item2 == 0 ? "No matching elements found." : $"{count.Item1} parameters applied.\n{count.Item2} values applied");
                    didWork = count.Item1 > 0 || count.Item2 > 0;
                };

                // 🟢 OnUpdateClicked is triggered when the user clicks the "Update" button
                ui.OnRemoveClicked = () =>
                {
                    FamilyNormParameterHelper rmNorm = new FamilyNormParameterHelper(doc);
                    rmNorm.RemoveNormParameterFromAllFamilies();
                    TaskDialog.Show("Info", "Norm parameter removed from families and reloaded into the project.");
                    LoadFamilies.FamilyLoader(doc);

                };
                // Show the dialog to the user
                ui.OnCleanUpClicked = () =>
                {
                    TaskDialog.Show("Info", "Clean");

                    LoadFamilies.FamilyLoader(doc);
                    TaskDialog.Show("Info", "UP");
                };
                ui.ShowDialog();
                selectedBauteilnummer.Clear();


                return didWork ? Result.Succeeded : Result.Cancelled;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ERROR] {ex}");
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
