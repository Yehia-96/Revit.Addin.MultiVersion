using Autodesk.Revit.DB;
using Revit.Addin._2026.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.UI;

namespace Revit.Addin._2026.Helpers
{
    public static class CleanUp
    {

        public static int RemoveEmptyParameters(
    Document doc,
    Application app,
    Dictionary<string, Dictionary<string, string>> familyData,
    List<int> bauteilnummers)
        {
            int removedCount = 0;
            var selectedSet = new HashSet<int>(bauteilnummers);

            List<FamilyInstance> instances = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .OfType<FamilyInstance>()
                .Where(fi =>
                {
                    Parameter bnParam = fi.LookupParameter("Bauteilnummer");
                    return bnParam != null && bnParam.HasValue && selectedSet.Contains(bnParam.AsInteger());
                })
                .Where(fi => fi.Symbol.Family.IsInPlace == false)
                .ToList();

            if (!instances.Any())
                return 0;

            // Group instances by family to process each family only once
            var familyGroups = instances.GroupBy(inst => inst.Symbol.Family.Id);

            foreach (var group in familyGroups)
            {
                var firstInstance = group.First();
                var family = firstInstance.Symbol.Family;
                int btnInt = firstInstance.LookupParameter("Bauteilnummer").AsInteger();
                string btn = Convert.ToString(btnInt);

                if (string.IsNullOrEmpty(btn) || !familyData.ContainsKey(btn))
                    continue;

                // Edit the family
                Document familyDoc = doc.EditFamily(family);
                FamilyManager famMgr = familyDoc.FamilyManager;

                List<string> removedParams = new List<string>();

                using (Transaction tx = new Transaction(familyDoc, "Remove Empty Parameters"))
                {
                    tx.Start();

                    foreach (string paramName in familyData[btn].Keys)
                    {
                        FamilyParameter famParam = famMgr.Parameters
                                       .Cast<FamilyParameter>()
                                       .FirstOrDefault(p => p.Definition != null && string.Equals(p.Definition.Name, paramName, StringComparison.OrdinalIgnoreCase));

                        if (famParam == null)
                            continue;

                        // Check if ALL instances of this family have empty values
                        bool allEmpty = group.All(inst =>
                        {
                            Parameter param = inst.LookupParameter(paramName);

                            if (param == null || !param.HasValue)
                                return true;

                            switch (param.StorageType)
                            {
                                case StorageType.String:
                                    string asString = param.AsString();
                                    string asValueString = param.AsValueString();
                                    return string.IsNullOrWhiteSpace(asString) && string.IsNullOrWhiteSpace(asValueString);

                                case StorageType.Integer:
                                    // treat integer parameters with a value as non-empty
                                    return false;

                                case StorageType.Double:
                                    double val = param.AsDouble();
                                    return Math.Abs(val) < 0.0001;

                                case StorageType.ElementId:
                                    ElementId idVal = param.AsElementId();
                                    return idVal == ElementId.InvalidElementId || idVal.Value == -1;

                                default:
                                    return false;
                            }
                        });

                        if (allEmpty)
                        {
                            try
                            {
                                // Prefer FamilyManager.RemoveParameter which is the supported API
                                famMgr.RemoveParameter(famParam);
                                removedParams.Add(paramName);
                                removedCount++;
                            }
                            catch (Exception ex1)
                            {
                                try
                                {
                                    // Fallback: attempt to delete the parameter element from family document
                                    familyDoc.Delete(famParam.Id);
                                    removedParams.Add(paramName);
                                    removedCount++;
                                }
                                catch (Exception ex2)
                                {
                                    TaskDialog.Show("Error", $"Failed to remove {paramName}: {ex1.Message}; {ex2.Message}");
                                }
                            }
                        }
                    }

                    tx.Commit();
                }

                // Ensure family document changes are saved before creating the temp file
                try
                {
                    if (familyDoc.IsModified)
                    {
                        // Save in-place if possible
                        try { familyDoc.Save(); }
                        catch { /* ignore save failures, SaveAs below will still attempt */ }
                    }
                }
                catch { }

                // Save family with a unique name to force complete reload
                string tempPath = Path.Combine(Path.GetTempPath(), $"tempFamily_{Guid.NewGuid()}.rfa");
                SaveAsOptions saveOptions = new SaveAsOptions { OverwriteExistingFile = true };
                try
                {
                    familyDoc.SaveAs(tempPath, saveOptions);
                }
                catch
                {
                    // If SaveAs fails, try Save then SaveAs again
                    try { familyDoc.Save(); familyDoc.SaveAs(tempPath, saveOptions); }
                    catch (Exception ex) { TaskDialog.Show("Error", $"Failed to save family to temp path: {ex.Message}"); }
                }
                using (Transaction loadTx = new Transaction(doc, "Reload Family"))
                {
                    loadTx.Start();

                    // Use overwrite option to force update
                    IFamilyLoadOptions loadOptions = new FamilyLoadOptions();
                    doc.LoadFamily(tempPath, loadOptions, out Family loadedFamily);

                    loadTx.Commit();
                }
                familyDoc.Close(false);

                // Reload family into project with overwrite option
           

                // Clean up temporary file
                try
                {
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                }
                catch { }
            }

            return removedCount;
        }

        // Custom load options class
        public class FamilyLoadOptions : IFamilyLoadOptions
        {
            public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
            {
                // Overwrite parameter values to force update
                overwriteParameterValues = true;
                return true; // Load the family
            }

            public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse, out FamilySource source, out bool overwriteParameterValues)
            {
                source = FamilySource.Family;
                overwriteParameterValues = true;
                return true;
            }
        }
    }
}
