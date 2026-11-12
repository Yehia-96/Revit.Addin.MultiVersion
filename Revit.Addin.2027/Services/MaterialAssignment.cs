using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Revit.Addin._2027.UI;
namespace Revit.Addin._2027.Services
{
    public static class MaterialAssignment
    {
        public static Dictionary<string, string> ElementMaterial = new Dictionary<string, string>();

        private static Dictionary<string, Material> FetchMaterials(Document doc)
        {
            var targetNames = new HashSet<string>(
                ElementMaterial.Values
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .Select(v => v.Trim()),
                StringComparer.OrdinalIgnoreCase);

            var materialsByName = new FilteredElementCollector(doc)
                .OfClass(typeof(Material))
                .Cast<Material>()
                .Where(m => targetNames.Contains(m.Name))
                .GroupBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var result = ElementMaterial
                .Where(kvp => !string.IsNullOrWhiteSpace(kvp.Value))
                .Where(kvp => materialsByName.ContainsKey(kvp.Value.Trim()))
                .ToDictionary(
                    kvp => kvp.Key,
                    kvp => materialsByName[kvp.Value.Trim()],
                    StringComparer.OrdinalIgnoreCase);

            return result;
        }

        public static ImportReport AssignMaterialsToElements(Document doc)
        {
            var report = new ImportReport();
            var materialDict = FetchMaterials(doc); // Dictionary<string, Material>

            if (doc == null || materialDict == null || materialDict.Count == 0)
                return report;

            // Keys are Bauteilnummern for the Material-Bauteilliste and Bauteil-IDs for the
            // Planungsdatenbank. Each element is read once, the same way the parameter applier
            // reads it, and grouped by its key.
            var keyFormat = Planungsdatenbank.WorkbookLoader.LoadedFormat;
            string keyLabel = Planungsdatenbank.ElementKey.Label(keyFormat);
            var instancesByKey = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .OfClass(typeof(FamilyInstance))
                .Cast<FamilyInstance>()
                .Select(fi => new { Instance = fi, Key = Planungsdatenbank.ElementKey.Read(fi, keyFormat) })
                .Where(x => x.Key != null)
                .GroupBy(x => x.Key, x => x.Instance, Planungsdatenbank.ElementKey.KeyComparer)
                .ToDictionary(g => g.Key, g => g.ToList(), Planungsdatenbank.ElementKey.KeyComparer);

            using (Transaction t = new Transaction(doc, "Assign materials to elements"))
            {
                t.Start();

                foreach (var kvp in materialDict)
                {
                    string key = Planungsdatenbank.ElementKey.Normalise(kvp.Key, keyFormat);
                    if (key == null)
                    {
                        report.AddWarn($"'{kvp.Key}' is not a {keyLabel}; skipped.");
                        continue;
                    }
                    Material mat = kvp.Value;

                    if (mat == null)
                    {
                        report.AddWarn($"No material found in document for {keyLabel} '{kvp.Key}'.");
                        continue;
                    }

                    List<FamilyInstance> elements;
                    if (!instancesByKey.TryGetValue(key, out elements))
                        elements = new List<FamilyInstance>();

                    if (elements.Count == 0)
                    {
                        report.AddWarn($"No elements found with {keyLabel} '{kvp.Key}' for material '{mat.Name}'.");
                        continue;
                    }

                    foreach (var element in elements)
                    {
                        try
                        {
                            Parameter matParam = element.LookupParameter("Material");

                            if (matParam == null)
                            {
                                report.AddError($"No 'Material' parameter found on element ID {element.Id}.");
                                continue;
                            }

                            if (matParam.IsReadOnly)
                            {
                                report.AddError($"'Material' parameter is read-only on element ID {element.Id}.");
                                continue;
                            }

                            if (matParam.StorageType != StorageType.ElementId)
                            {
                                report.AddError($"'Material' parameter on element ID {element.Id} is not an ElementId parameter.");
                                continue;
                            }

                            matParam.Set(mat.Id);
                            report.AddInfo($"Assigned material '{mat.Name}' to element ID {element.Id} with {keyLabel} '{kvp.Key}'.");
                        }
                        catch (Exception ex)
                        {
                            report.AddError($"Error assigning material '{mat.Name}' to element ID {element.Id} with {keyLabel} '{key}': {ex.Message}");
                        }
                    }
                }

                t.Commit();
            }

            return report;
        } 

        public static void PrintItAll(List<Material> Filtered)
        {
            string path = @"C:\Users\Y.Abdelazziz\Desktop\Materials.txt";
            foreach (var item in ElementMaterial)
            {
               File.AppendAllText(path , $"{item.Key} : {item.Value} " + Environment.NewLine, System.Text.Encoding.UTF8);
            }
            foreach (var item in Filtered)
                File.AppendAllText(path, $"{item.Name} " + Environment.NewLine, System.Text.Encoding.UTF8);
        } 
    }
}
