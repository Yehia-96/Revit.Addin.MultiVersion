using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit.Addin._2026.Helpers;
using Revit.Addin._2026.Services;
using Revit.Addin._2026.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Windows.Forms;
using RevitApp = Autodesk.Revit.ApplicationServices.Application;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;
namespace Revit.Addin._2026.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class AddParametersToFamilies : IExternalCommand
    {
        // 🟢 Static fields to store state across command executions
        private static bool _firstRun = true;
        private string _excelFilePath;

        public static string _sharedParamPath;
        private static bool isNew;
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

                    ExcelParser.NewReadExcelStructured(_excelFilePath);


                    // shared parameter file
                    SharedParameterSeeder.EnsureParametersExist(
                        app,
                        new[] { ExcelParser.ParameterDefinitions, ExcelParser.MaterialParameterDefinitions },
                        _sharedParamPath);

                    _firstRun = false;
                }

                // Convert the List of strings to ints of selected Bauteilnummernfrom the Excel data
                //List<int> selectedBauteilnummer = ExcelParser.FamilyData.Keys
                //                              .Select(int.Parse)
                //                              .ToList();

                var selectedBauteilnummer = new List<int>();

                foreach (var key in ExcelParser.FamilyData.Keys)
                {
                    if (int.TryParse(key, out int bn))
                        selectedBauteilnummer.Add(bn);
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
                        if (isNew)
                        {
                            ExcelParser.NewReadExcelStructured(_excelFilePath);
                        }
                        else
                        {
                            ExcelParser.ReadExcelStructured(_excelFilePath);
                        }
                        TaskDialog.Show("Info", "Excel file loaded successfully.");
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
                        _sharedParamPath);

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
