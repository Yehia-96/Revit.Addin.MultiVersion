// File: SeedProjectElementsParameters.cs (cleaned for Revit 2024)
using Autodesk.Revit.DB;
using Revit.Addin._2024.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using RevitApp = Autodesk.Revit.ApplicationServices.Application;
namespace Revit.Addin._2024.Helpers
{
    // Main class to handle seeding parameters and values to project elements and families
    public static class SeedProjectElementsParameters
    {
        // The main function to add parameters and values to elements in the project
        public static Tuple<int, int> AddParametersToElements(
      Document projectDoc,
      Dictionary<string, ParamMetaData> parameterMeta,
      Dictionary<string, Dictionary<string, string>> familyData,
      RevitApp app,
      List<int> selectedBauteilnummer,
      string sharedParameterFilePath)
        {
            // Open shared parameter file
            app.SharedParametersFilename = sharedParameterFilePath;
            var defFile = app.OpenSharedParameterFile();
            if (defFile == null)
                throw new InvalidOperationException("Shared parameter file failed to open.");

            var elementsByBN = GroupElementsByBauteilnummer(projectDoc);

            // Collect: FamilyId -> (Family, MissingParamNamesForThatFamily)
            // Missing is decided at INSTANCE LEVEL (fi.LookupParameter == null)
            var missingParamsPerFamily = new Dictionary<ElementId, (Family Family, HashSet<string> Missing)>();

            // Collect instance value updates
            var allTuples = new List<Tuple<Parameter, ParameterValue>>();

            int totalValuesSet = 0;

            foreach (int bn in selectedBauteilnummer)
            {
                string bnKey = bn.ToString();

                if (!elementsByBN.TryGetValue(bnKey, out var matchingInstances))
                    continue;

                if (!familyData.TryGetValue(bnKey, out var parametersForBn))
                    continue;

                // --- Phase A: decide which params to inject based on INSTANCE missing params ---
                foreach (FamilyInstance fi in matchingInstances)
                {
                    Family fam = fi.Symbol?.Family;
                    if (fam == null) continue;

                    if (!missingParamsPerFamily.TryGetValue(fam.Id, out var entry))
                    {
                        entry = (fam, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                        missingParamsPerFamily[fam.Id] = entry;
                    }

                    foreach (string pName in parametersForBn.Keys)
                    {
                        // instance-level check (your SBIM assumption: one family per instance)
                        if (fi.LookupParameter(pName) == null)
                            entry.Missing.Add(pName);
                    }
                }

                // --- Phase B: collect values (does not apply yet) ---
                totalValuesSet += ApplyValuesToInstances(
                    projectDoc,
                    matchingInstances,
                    parametersForBn,
                    parameterMeta,
                    allTuples);
            }

            // --- Phase A apply: inject missing parameters into each family file ---
            int totalParamsAdded = 0;

            foreach (var kv in missingParamsPerFamily.Values)
            {
                if (kv.Missing.Count == 0)
                    continue;

                // InjectParametersToFamily will still re-check what exists in the family doc
                totalParamsAdded += InjectParametersToFamily(
                    projectDoc,
                    kv.Family,
                    defFile,
                    parameterMeta,
                    kv.Missing.ToList());
            }

            // --- Phase B apply: set instance values in one transaction ---
            if (allTuples.Count > 0)
            {
                using (var tx = new Transaction(projectDoc, "Apply Parameter Values"))
                {
                    tx.Start();
                    Parameter.SetMultiple(allTuples);
                    tx.Commit();
                }
            }

            return Tuple.Create(totalParamsAdded, totalValuesSet);
        }



        // Helper method to group FamilyInstances by their Bauteilnummer parameter
        private static Dictionary<string, List<FamilyInstance>> GroupElementsByBauteilnummer(Document doc)
        {
            return new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .OfType<FamilyInstance>()
                .Where(e => e.LookupParameter("Bauteilnummer")?.HasValue == true)
                .GroupBy(e => e.LookupParameter("Bauteilnummer").AsInteger())
                .ToDictionary(g => g.Key.ToString(), g => g.ToList());
        }
        // Method to inject shared parameters into a family document
        private static int InjectParametersToFamily(
            Document projectDoc,
            Family family,
            DefinitionFile defFile,
            Dictionary<string, ParamMetaData> parameterMeta,
            List<string> parameters)
        {
            // Opens the family document for editing
            Document famDoc = projectDoc.EditFamily(family);
            // Access the FamilyManager to manage family parameters
            FamilyManager fm = famDoc.FamilyManager;
            // Fetch existing parameters in the family using API GetParameters method
            IList<FamilyParameter> existingParams = fm.GetParameters();
            int countPara = 0;
            // Filter the parameters to find those that are not already defined in the family
            List<string> newParamNames = parameters
                .Where(key => !existingParams.Any(p => string.Equals(p.Definition.Name, key, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            // If there are no new parameters to add, close the family document and return
            if (!newParamNames.Any())
            {
                famDoc.Close(false);
                return 0;
            }
            // Start a transaction to modify the family document by
            // adding new parameters from the shared parameter file
            using (Transaction tx = new Transaction(famDoc, "Inject Shared Parameters"))
            {
                tx.Start();
                foreach (string paramName in newParamNames)
                {
                    Debug.WriteLine($"{paramName}");
                    // Find the definition in the shared parameter file that matches the parameter name
                    Definition def = defFile.Groups
                        .SelectMany(g => g.Definitions.Cast<Definition>())
                        .FirstOrDefault(d => string.Equals(d.Name, paramName, StringComparison.OrdinalIgnoreCase));
                    // If the definition is found and it is an ExternalDefinition, add it to the family
                    // using the metadata inside the dictionary parameterMeta containing the ParamMetaData objects
                    if (def is ExternalDefinition extDef &&
                        parameterMeta.TryGetValue(paramName, out var meta))
                    {
                        try
                        {
                            // Add the parameter to the family manager with
                            // the specified metadata using the API AddParameter method
                            fm.AddParameter(extDef, meta.GroupNameRevit, true);
                            countPara++;
                        }
                        catch (Exception ex)
                        {
                            
                            Debug.WriteLine($"Failed to add parameter '{paramName}' to family '{family.Name}': {ex.Message}");
                        }
                    }
                }
                tx.Commit();
            }
            // Ensure the family document is saved before loading it back into the project
            EnsureFamilyIsSaved(famDoc);
            famDoc.LoadFamily(projectDoc, new SimpleLoad());
            famDoc.Close(true);
            return countPara;
        }
        // Method to apply values to FamilyInstances based on the parameters defined in the family data
        // Note: This method assumes that the parameters are already defined in the instances
        // Future improvements will have this method add the values inside the family document 
        // when the parameters are correctly defined in the family
        private static int ApplyValuesToInstances(
     Document projectDoc,
     List<FamilyInstance> instances,
     Dictionary<string, string> parameters,
     Dictionary<string, ParamMetaData> parameterMeta,
     List<Tuple<Parameter, ParameterValue>> allTuples)
        {
            int countValues = 0;

            // Ignore list (make it case-insensitive)
            var paramsToIgnore = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Station", "Station von", "Station bis", "Dicke", "Breite", "Durchmesser"
    };

            foreach (FamilyInstance elem in instances)
            {
                foreach (var kvp in parameters)
                {
                    string name = kvp.Key;

                    // Skip ignored params BEFORE doing anything
                    if (paramsToIgnore.Contains(name))
                        continue;

                    if (!parameterMeta.TryGetValue(name, out var meta))
                        continue;

                    Parameter param = elem.LookupParameter(name);
                    if (param == null || param.IsReadOnly)
                        continue;

                    // Convert value to correct ParameterValue type
                    ParameterValue converted = ParamMetaData.ConvertValue(projectDoc, meta.DataTypeRevit, kvp.Value);

                    // Avoid pointless writes, but do overwrite if different
                    if (!ParamMetaData.AreValuesEqual(param, kvp.Value))
                    {
                        allTuples.Add(Tuple.Create(param, converted));
                        countValues++;
                    }
                }
            }

            return countValues;
        }

        // Method to ensure the family document is saved, if not already saved
        //public static void EnsureFamilyIsSaved(Document famDoc)
        //{
        //    // If the document is not modified, there is nothing to save
        //    if (!famDoc.IsModified)
        //        return;

        //    // CASE A: The family is writable → simple Save()
        //    if (!famDoc.IsReadOnly)
        //    {
        //        if (!famDoc.PathName.EndsWith(".rfa", StringComparison.OrdinalIgnoreCase))
        //        {
        //            string tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.rfa");
        //            famDoc.SaveAs(tempPath, new SaveAsOptions { OverwriteExistingFile = true });
        //        }
        //    }

        //    // CASE B: Family is READ-ONLY → User must choose a location
        //    using (var dialog = new SaveFileDialog())
        //    {
        //        dialog.Title = "Save Modified Family As";
        //        dialog.Filter = "Revit Family (*.rfa)|*.rfa";
        //        dialog.AddExtension = true;

        //        string defaultName =
        //            string.IsNullOrEmpty(famDoc.PathName)
        //                ? famDoc.Title + ".rfa"
        //                : Path.GetFileName(famDoc.PathName);

        //        dialog.FileName = defaultName;

        //        // User cancels → fallback to temp save (so Revit doesn't lose data)
        //        if (dialog.ShowDialog() != DialogResult.OK)
        //        {
        //            string tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.rfa");
        //            famDoc.SaveAs(tempPath, new SaveAsOptions { OverwriteExistingFile = true });
        //            return;
        //        }

        //        // SaveAs to user-selected file
        //        string targetPath = dialog.FileName;

        //        famDoc.SaveAs(targetPath,
        //            new SaveAsOptions { OverwriteExistingFile = true });
        //    }
        //}
        //Old version from the EnsureFamilyIsSaved method TO BE REMOVED LATER
        public static void EnsureFamilyIsSaved(Document famDoc)
        {
           if (!famDoc.PathName.EndsWith(".rfa", StringComparison.OrdinalIgnoreCase))
            {
                string tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.rfa");
                famDoc.SaveAs(tempPath, new SaveAsOptions { OverwriteExistingFile = true });
            }
        }
        //Method to update existing parameters based on changes in the datasource
        public static int UpdateChanges(Document doc, Dictionary<string, Dictionary<string, string>> changes)
        {
            int count = 0;
            if (changes == null || changes.Count == 0)
                return count;

            using (Transaction tx = new Transaction(doc, "Update Parameters"))
            {
                tx.Start();

                foreach (string bn in changes.Keys)
                {
                    if (!int.TryParse(bn, out int bnValue)) continue;
                    // Filter elements by Bauteilnummer, Collect all elements that match the Bauteilnummer
                    IEnumerable<Element> elements = new FilteredElementCollector(doc)
                        .WhereElementIsNotElementType()
                        .Where(e => e.LookupParameter("Bauteilnummer")?.AsInteger() == bnValue);

                    foreach (KeyValuePair<string,string> kvp in changes[bn])
                    {
                        foreach (Element elem in elements)
                        {
                            
                            Parameter param = elem.LookupParameter(kvp.Key);
                            if (param != null && !param.IsReadOnly)
                            {
                                // Set the parameter value based on its storage type
                                switch (param.StorageType)
                                {
                                    case StorageType.String:
                                        param.Set(kvp.Value);
                                        break;
                                    case StorageType.Integer:
                                        if (int.TryParse(kvp.Value, out int intVal)) param.Set(intVal);
                                        break;
                                    case StorageType.Double:
                                        if (double.TryParse(kvp.Value, out double dblVal)) param.Set(dblVal);
                                        break;
                                    default:
                                        param.SetValueString(kvp.Value);
                                        break;
                                }
                                count++;
                            }
                        }
                    }
                }
                tx.Commit();
            }
            

            changes.Clear();
            return count;
        }
    }
}