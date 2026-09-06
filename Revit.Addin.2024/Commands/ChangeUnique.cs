using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OfficeOpenXml;
using Revit.Addin._2024.Services;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using Autodesk.Revit.Attributes;
namespace Revit.Addin._2024.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class ChangeUnique : IExternalCommand
    {

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                Dictionary<int, int> uniqueBauteilnummerChanges = new Dictionary<int, int>();

                UIDocument uiDoc = commandData.Application.ActiveUIDocument;
                Document doc = uiDoc.Document;

                ElementClassFilter filter = new ElementClassFilter(typeof(FamilyInstance));
                FilteredElementCollector collector = new FilteredElementCollector(doc);
                FilteredElementCollector filterResult = collector.WherePasses(filter);

                Parameter Bauteilnummer = filterResult.FirstOrDefault(e => e.LookupParameter("Bauteilnummer") != null)?.LookupParameter("Bauteilnummer");
                FilterRule parameterRule = ParameterFilterRuleFactory.CreateHasValueParameterRule(Bauteilnummer.Id);
                ElementParameterFilter parameterFilter = new ElementParameterFilter(parameterRule);
                FilteredElementCollector filteredCollector = new FilteredElementCollector(doc).WherePasses(parameterFilter);
                HashSet<int> uniqueBauteilnummers = new HashSet<int>();
                foreach (Element element in filteredCollector)
                {
                    Parameter BauteilnummerEle = element.get_Parameter(Bauteilnummer.GUID);

                    if (BauteilnummerEle.StorageType == StorageType.Integer)
                        uniqueBauteilnummers.Add(BauteilnummerEle.AsInteger());

                    else
                        uniqueBauteilnummers.Add(int.Parse(BauteilnummerEle.AsString()));
                }
                TaskDialog.Show("Info", $"Found {uniqueBauteilnummers.Count} unique Bauteilnummer(s).");
                ExcelPackage.License.SetNonCommercialPersonal("Yehia");
                using (ExcelPackage package = new ExcelPackage(new System.IO.FileInfo(@"D:\Database_parameters\ReadInParameterData\ParameterData.xlsx")))
                {
                    ExcelWorksheet ws = package.Workbook.Worksheets[1];
                    int rowStart = 2;
                    int rowEnd = ws.Dimension.End.Row;
                    string firstCellValue = ws.Cells[1, 1].GetCellValue<string>();
                    int secondCellValue = ws.Cells[2, 2].GetCellValue<int>();
                    for (int i = rowStart; i <= rowEnd; i++)
                    {
                        int oldValue = ws.Cells[i, 1].GetCellValue<int>();
                        int newValue = ws.Cells[i, 2].GetCellValue<int>();

                        uniqueBauteilnummerChanges[oldValue] = newValue;
                    }
                }
            ;
                if (uniqueBauteilnummerChanges.Count == 0)
                {
                    TaskDialog.Show("Info", "No changes to apply.");
                    return Result.Succeeded;
                }
                foreach (var kvp in uniqueBauteilnummerChanges)
                {
                    TaskDialog.Show("Info", $"{kvp.Key} and {kvp.Value}");
                    int oldValue = kvp.Key;
                    int newValue = kvp.Value;
                    FilterNumericEquals filterNumericEquals = new FilterNumericEquals();
                    ParameterValueProvider parameterValueProvider = new ParameterValueProvider(Bauteilnummer.Id);
                    FilterValueRule valueRule = new FilterIntegerRule(parameterValueProvider, filterNumericEquals, oldValue);
                    IList<FilterRule> rules = new List<FilterRule> { parameterRule, valueRule };
                    ElementParameterFilter elementParameterFilter = new ElementParameterFilter(rules);

                    filteredCollector = new FilteredElementCollector(doc).WherePasses(elementParameterFilter);
                    using (Transaction transaction = new Transaction(doc, "Change Bauteilnummer"))
                    {
                        transaction.Start();
                        foreach( Element element in filteredCollector)
                        {
                            Parameter BauteilnummerEle = element.get_Parameter(Bauteilnummer.GUID);
                            if (BauteilnummerEle.StorageType == StorageType.Integer)
                            {
                                BauteilnummerEle.Set(newValue);
                            }
                            else
                            {
                                BauteilnummerEle.Set(newValue.ToString());
                            }
                        }
                        transaction.Commit();
                    }

                }
                TaskDialog.Show("Info", $"Changed {uniqueBauteilnummerChanges.Count} Bauteilnummer(s).");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("Error", "An error occurred: " + message);
                return Result.Failed;
            }
        }
    }
}
