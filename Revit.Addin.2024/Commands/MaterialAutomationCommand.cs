using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.ApplicationServices;
using Revit.Addin._2024.Helpers;
using Revit.Addin._2024.Services;
using Revit.Addin._2024.UI;
using System;

namespace Revit.Addin._2024.Commands
{
    [Transaction(TransactionMode.Manual)]

    public class MaterialAutomationCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiApp = commandData.Application;
            UIDocument uiDoc = uiApp.ActiveUIDocument;
            Document doc = uiDoc.Document;
            Application app = uiApp.Application;
            string sharedParamPath = app.SharedParametersFilename;

            var materialsUI = new MaterialsUI
            {
                PopulateMaterialsAction = () =>
                {
                    try
                    {
                        var report = new ImportReport();

                        // If you can’t/won’t modify your methods, just store the counts:
                        report.BoundParameters = SeedMaterialParameters.BindSharedParametersToMaterials(
                            doc, app, sharedParamPath, ExcelParser.MaterialParameterDefinitions);

                        report.WrittenValues = SeedMaterialParameters.ApplyValuesByMaterialName(
                            doc, ExcelParser.MaterialData);

                        // Optional: add any high-level notes you already know
                        report.AddInfo($"Shared parameter file: {sharedParamPath}");

                        ReportUi.ShowImportReportTaskDialog(report, "Material Population Report");
                    }
                    catch (Exception ex)
                    {
                        TaskDialog.Show("Error", $"Failed to populate materials: {ex.Message}");
                    }
                },
                CreateMaterialsAction = () =>
                {
                    try
                    {
                        var report = new ImportReport();

                        var creationReport = MaterialCreation.MaterialGeneration(doc);
                        report.WrittenValues = creationReport.WrittenValues;

                        foreach (var info in creationReport.Info) report.AddInfo(info);
                        foreach (var warning in creationReport.Warnings) report.AddWarn(warning);
                        foreach (var error in creationReport.Errors) report.AddError(error);

                        ReportUi.ShowImportReportTaskDialog(report, "Material Creation Report");

                        var reports = MaterialAssignment.AssignMaterialsToElements(doc);
                        ReportUi.ShowImportReportTaskDialog(reports, "Material Assignment");
                    }
                    catch (Exception ex)
                    {
                        TaskDialog.Show("Error", $"Failed to create materials: {ex.Message}");
                    }
                },

               AssignPatternAction = () =>
                {
                    try
                    {
                        var report = MaterialCreation.AssignFillPatterns(doc);
                        ReportUi.ShowImportReportTaskDialog(report, "Material Pattern Assignment Report");
                    }
                    catch (Exception ex)
                    {
                        TaskDialog.Show("Error", $"Failed to assign fill patterns: {ex.Message}");
                    }
                }
            };
            materialsUI.ShowDialog();
            return Result.Succeeded;
        }
    }
}
