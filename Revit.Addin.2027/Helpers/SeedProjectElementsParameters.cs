using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit.Addin._2027.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using RevitApp = Autodesk.Revit.ApplicationServices.Application;

namespace Revit.Addin._2027.Helpers
{
    public static class SeedProjectElementsParameters
    {
        private static readonly HashSet<string> WarnedInPlaceFamilies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>
        /// Ensures required shared instance parameters exist in families, then populates instance values.
        /// Rule: Write value if parameter is EMPTY OR DIFFERENT from incoming value.
        /// </summary>
        public static Tuple<int, int> AddParametersToElements(
            Document projectDoc,
            Dictionary<string, ParamMetaData> parameterMeta,
            Dictionary<string, Dictionary<string, string>> familyData,
            RevitApp app,
            List<int> selectedBauteilnummer,
            string sharedParameterFilePath)
        {
            if (projectDoc == null) throw new ArgumentNullException(nameof(projectDoc));
            if (parameterMeta == null) throw new ArgumentNullException(nameof(parameterMeta));
            if (familyData == null) throw new ArgumentNullException(nameof(familyData));
            if (app == null) throw new ArgumentNullException(nameof(app));
            if (selectedBauteilnummer == null) throw new ArgumentNullException(nameof(selectedBauteilnummer));
            if (string.IsNullOrWhiteSpace(sharedParameterFilePath)) throw new ArgumentException("Shared parameter file path is empty.", nameof(sharedParameterFilePath));

            // Open shared parameter file
            app.SharedParametersFilename = sharedParameterFilePath;
            DefinitionFile defFile = app.OpenSharedParameterFile();
            if (defFile == null)
                throw new InvalidOperationException("Shared parameter file failed to open.");

            // Cache all shared parameter definitions by name for fast lookup
            var defsByName = defFile.Groups
                .Where(g => !g.Name.Equals("Material", StringComparison.OrdinalIgnoreCase))
                .SelectMany(g => g.Definitions.Cast<Definition>())
                .GroupBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            int parametersAdded = 0;
            int valuesWritten = 0;

            // Collect relevant elements (selected Bauteilnummer only)
            var selectedSet = new HashSet<int>(selectedBauteilnummer);
            List<Element> matchingElements = new FilteredElementCollector(projectDoc)
                .WhereElementIsNotElementType()
                .Where(elem =>
                {
                    Parameter bnParam = elem.LookupParameter("Bauteilnummer");
                    return bnParam != null && bnParam.HasValue && selectedSet.Contains(bnParam.AsInteger());
                })
                .ToList();

            if (matchingElements.Count == 0)
                return Tuple.Create(0, 0);

            List<FamilyInstance> familyInstances = matchingElements
                .OfType<FamilyInstance>()
                .ToList();

            List<Element> otherElements = matchingElements
                .Where(elem => !(elem is FamilyInstance))
                .ToList();

            // -------------------------------
            // PHASE 1: INJECT MISSING PARAMS INTO FAMILIES
            // -------------------------------
            var requiredByFamily = new Dictionary<ElementId, HashSet<string>>();
            var familyById = new Dictionary<ElementId, Family>();

            foreach (FamilyInstance fi in familyInstances)
            {
                int bn = fi.LookupParameter("Bauteilnummer").AsInteger();
                string bnKey = bn.ToString();

                if (!familyData.TryGetValue(bnKey, out var expectedParams))
                    continue;

                Family family = fi.Symbol?.Family;
                if (family == null)
                    continue;

                if (!requiredByFamily.TryGetValue(family.Id, out var set))
                {
                    set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    requiredByFamily[family.Id] = set;
                    familyById[family.Id] = family;
                }

                foreach (string paramName in expectedParams.Keys)
                {
                    if (ExcelParser.ParametersToIgnore.Contains(paramName))
                        continue;

                    if (parameterMeta.ContainsKey(paramName))
                        set.Add(paramName);
                }
            }

            foreach (var kvp in requiredByFamily)
            {
                Family family = familyById[kvp.Key];
                List<string> required = kvp.Value.ToList();

                var famInstances = familyInstances.Where(fi => fi.Symbol.Family.Id == family.Id);
                required = required
                    .Where(pName => !famInstances.Any(fi => fi.LookupParameter(pName) != null))
                    .ToList();

                if (required.Count == 0)
                    continue;

                int added = InjectParametersToFamily(
                    projectDoc,
                    app,
                    family,
                    defsByName,
                    parameterMeta,
                    required);

                parametersAdded += added;
            }

            // -------------------------------
            // PHASE 1B: BIND SHARED PARAMS TO NON-FAMILY CATEGORIES
            // -------------------------------
            var requiredForOtherElements = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var otherCategorySet = app.Create.NewCategorySet();

            foreach (Element elem in otherElements)
            {
                int bn = elem.LookupParameter("Bauteilnummer").AsInteger();
                string bnKey = bn.ToString();

                if (!familyData.TryGetValue(bnKey, out var expectedParams))
                    continue;

                foreach (string paramName in expectedParams.Keys)
                {
                    if (ExcelParser.ParametersToIgnore.Contains(paramName))
                        continue;

                    if (parameterMeta.ContainsKey(paramName))
                        requiredForOtherElements.Add(paramName);
                }

                Category category = elem.Category;
                if (category != null && category.AllowsBoundParameters)
                {
                    try
                    {
                        otherCategorySet.Insert(category);
                    }
                    catch (ArgumentException)
                    {
                        // Category already in set.
                    }
                }
            }

            if (requiredForOtherElements.Count > 0 && otherCategorySet.Size > 0)
            {
                int bound = BindParametersToCategories(
                    projectDoc,
                    app,
                    defsByName,
                    parameterMeta,
                    requiredForOtherElements,
                    otherCategorySet);

                parametersAdded += bound;
            }


            // -------------------------------
            // PHASE 2: APPLY VALUES
            // -------------------------------
            var familyTuples = new List<Tuple<Parameter, ParameterValue>>();
            var otherTuples = new List<Tuple<Parameter, ParameterValue>>();

            foreach (FamilyInstance fi in familyInstances)
            {
                int bn = fi.LookupParameter("Bauteilnummer").AsInteger();
                string bnKey = bn.ToString();

                if (!familyData.TryGetValue(bnKey, out var expectedParams))
                    continue;

                foreach (var kvp in expectedParams)
                {
                    string name = kvp.Key;
                    string incomingValue = kvp.Value;

                    if (ExcelParser.ParametersToIgnore.Contains(name))
                        continue;

                    if (!parameterMeta.TryGetValue(name, out var meta))
                        continue;

                    Parameter p = fi.LookupParameter(name);
                    if (p == null || p.IsReadOnly)
                        continue;

                    // Write if EMPTY OR DIFFERENT
                    if (!IsParameterEmpty(p) && ParamMetaData.AreValuesEqual(p, incomingValue))
                        continue;

                    ParameterValue converted = ParamMetaData.ConvertValue(projectDoc, meta.DataTypeRevit, incomingValue);

                    familyTuples.Add(Tuple.Create(p, converted));
                    valuesWritten++;
                }
            }

            foreach (Element elem in otherElements)
            {
                int bn = elem.LookupParameter("Bauteilnummer").AsInteger();
                string bnKey = bn.ToString();

                if (!familyData.TryGetValue(bnKey, out var expectedParams))
                    continue;

                foreach (var kvp in expectedParams)
                {
                    string name = kvp.Key;
                    string incomingValue = kvp.Value;

                    if (ExcelParser.ParametersToIgnore.Contains(name))
                        continue;

                    if (!parameterMeta.TryGetValue(name, out var meta))
                        continue;

                    Parameter p = elem.LookupParameter(name);
                    if (p == null || p.IsReadOnly)
                        continue;

                    if (!IsParameterEmpty(p) && ParamMetaData.AreValuesEqual(p, incomingValue))
                        continue;

                    ParameterValue converted = ParamMetaData.ConvertValue(projectDoc, meta.DataTypeRevit, incomingValue);
                    otherTuples.Add(Tuple.Create(p, converted));
                    valuesWritten++;
                }
            }

            if (familyTuples.Count > 0)
            {
                using (Transaction tx = new Transaction(projectDoc, "Apply Family Instance Parameter Values"))
                {
                    tx.Start();
#if REVIT2024 || REVIT2026 || REVIT2027
                    Parameter.SetMultiple(familyTuples);
#else
                    foreach (var t in familyTuples)
                    {
                        var p = t.Item1;
                        var v = t.Item2;
                        if (v is StringParameterValue sv) p.Set(sv.Value);
                        else if (v is DoubleParameterValue dv) p.Set(dv.Value);
                        else if (v is IntegerParameterValue iv) p.Set(iv.Value);
                        else if (v is ElementIdParameterValue ev) p.Set(ev.Value);
                    }
#endif
                    tx.Commit();
                }
            }

            if (otherTuples.Count > 0)
            {
                using (Transaction tx = new Transaction(projectDoc, "Apply Non-Family Parameter Values"))
                {
                    tx.Start();
#if REVIT2024 || REVIT2026 || REVIT2027
                    Parameter.SetMultiple(otherTuples);
#else
                    foreach (var t in otherTuples)
                    {
                        var p = t.Item1;
                        var v = t.Item2;
                        if (v is StringParameterValue sv) p.Set(sv.Value);
                        else if (v is DoubleParameterValue dv) p.Set(dv.Value);
                        else if (v is IntegerParameterValue iv) p.Set(iv.Value);
                        else if (v is ElementIdParameterValue ev) p.Set(ev.Value);
                    }
#endif
                    tx.Commit();
                }
            }
            
            return Tuple.Create(parametersAdded, valuesWritten);
        }

        private static int BindParametersToCategories(
            Document projectDoc,
            RevitApp app,
            Dictionary<string, Definition> defsByName,
            Dictionary<string, ParamMetaData> parameterMeta,
            IEnumerable<string> requiredParameterNames,
            CategorySet categories)
        {
            if (requiredParameterNames == null || categories == null || categories.Size == 0)
                return 0;

            BindingMap map = projectDoc.ParameterBindings;
            InstanceBinding binding = app.Create.NewInstanceBinding(categories);

            int bound = 0;

            using (Transaction tx = new Transaction(projectDoc, "Bind shared parameters to categories"))
            {
                tx.Start();

                foreach (string name in requiredParameterNames)
                {
                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    if (!parameterMeta.TryGetValue(name, out var meta))
                        continue;

                    if (!defsByName.TryGetValue(name, out var def))
                        continue;

                    ForgeTypeId groupId = meta.GroupNameRevit ?? GroupTypeId.General;

                    bool ok = map.Insert(def, binding, groupId);
                    if (!ok)
                        ok = map.ReInsert(def, binding, groupId);

                    if (ok)
                        bound++;
                }

                tx.Commit();
            }

            return bound;
        }

        /// <summary>
        /// Injects required shared instance parameters into a family (only the missing ones).
        /// Returns count of parameters added.
        /// </summary>
        private static int InjectParametersToFamily(
            Document projectDoc,
            RevitApp app,
            Family family,
            Dictionary<string, Definition> defsByName,
            Dictionary<string, ParamMetaData> parameterMeta,
            List<string> requiredParameterNames)
        {
            if (requiredParameterNames == null || requiredParameterNames.Count == 0)
                return 0;

            if (family == null)
                return 0;

            if (family.IsInPlace)
            {
                Debug.WriteLine($"[WARN] Skipping in-place family '{family.Name}' (cannot be edited).");
                
                BindParametersForInPlaceFamily(projectDoc, app, defsByName, parameterMeta, requiredParameterNames, family);
                return 0;
            }

            Document famDoc = projectDoc.EditFamily(family);
            FamilyManager fm = famDoc.FamilyManager;

            // Existing family params (case-insensitive)
            var existingNames = new HashSet<string>(
                fm.GetParameters().Select(fp => fp.Definition?.Name).Where(n => !string.IsNullOrWhiteSpace(n)),
                StringComparer.OrdinalIgnoreCase);

            // Determine which required ones are missing in the family
            List<string> toAdd = requiredParameterNames
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(n => !existingNames.Contains(n))
                .ToList();

            if (!toAdd.Any())
            {
                famDoc.Close(false);
                return 0;
            }

            int addedCount = 0;

            using (Transaction tx = new Transaction(famDoc, "Inject Shared Parameters"))
            {
                tx.Start();

                foreach (string name in toAdd)
                {
                    if (!parameterMeta.TryGetValue(name, out var meta))
                        continue;

                    if(ExcelParser.ParametersToIgnore.Contains(name))
                        continue;

                    if (!defsByName.TryGetValue(name, out var def))
                        continue;

                    if (def is ExternalDefinition extDef)
                    {
                        // Add as INSTANCE parameter (true)
                        fm.AddParameter(extDef, meta.GroupNameRevit, true);
                        addedCount++;
                    }
                }

                tx.Commit();
            }


            // Reload into project
            famDoc.LoadFamily(projectDoc, new SimpleLoad());

            famDoc.Close(false);

            return addedCount;
        }

        private static void BindParametersForInPlaceFamily(
            Document projectDoc,
            RevitApp app,
            Dictionary<string, Definition> defsByName,
            Dictionary<string, ParamMetaData> parameterMeta,
            IEnumerable<string> requiredParameterNames,
            Family family)
        {
            if (projectDoc == null || app == null || family == null)
                return;

            Category category = family.FamilyCategory;
            if (category == null || !category.AllowsBoundParameters)
                return;

            var categorySet = app.Create.NewCategorySet();
            try
            {
                categorySet.Insert(category);
            }
            catch (ArgumentException)
            {
                // Category already in set.
            }

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string name in requiredParameterNames ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(name))
                    continue;
                if (ExcelParser.ParametersToIgnore.Contains(name))
                    continue;
                if (parameterMeta.ContainsKey(name))
                    names.Add(name);
            }

            if (names.Count == 0)
                return;

            BindParametersToCategories(projectDoc, app, defsByName, parameterMeta, names, categorySet);
        }

        /// <summary>
        /// Empty means: no value OR (string is null/empty/whitespace).
        /// Numeric 0 is treated as a valid value (not empty).
        /// </summary>
        private static bool IsParameterEmpty(Parameter p)
        {
            if (p == null) return true;
            if (!p.HasValue) return true;

            if (p.StorageType == StorageType.String)
                return string.IsNullOrWhiteSpace(p.AsString());

            return false;
        }
        private static void EnsureFamilyIsSaved(Document famDoc)
        {
            if (famDoc == null) throw new ArgumentNullException(nameof(famDoc));

            // If the doc has never been saved, PathName is often empty
            bool hasRealPath = !string.IsNullOrWhiteSpace(famDoc.PathName)
                               && famDoc.PathName.EndsWith(".rfa", StringComparison.OrdinalIgnoreCase);

            var sao = new SaveAsOptions { OverwriteExistingFile = true };

            try
            {
                if (!hasRealPath)
                {
                    // Create a guaranteed .rfa path
                    string tempDir = Path.Combine(Path.GetTempPath(), "RevitParamSeed");
                    Directory.CreateDirectory(tempDir);

                    string tempPath = Path.Combine(tempDir, $"{Guid.NewGuid():N}.rfa");
                    famDoc.SaveAs(tempPath, sao);
                }
                else
                {
                    // Save existing file path
                    famDoc.Save();
                }
            }
            catch (Exception ex)
            {
                // Make the failure visible; otherwise you think it saved but it didn't
                throw new InvalidOperationException(
                    $"Family save failed. Path='{famDoc.PathName}'. {ex.Message}", ex);
            }
        }


        public static int UpdateChanges(Document doc, Dictionary<string, Dictionary<string, string>> changes)
        {
            int count = 0;
            if (doc == null) return 0;
            if (changes == null || changes.Count == 0) return 0;

            using (Transaction tx = new Transaction(doc, "Update Parameters"))
            {
                tx.Start();

                foreach (var bnEntry in changes)
                {
                    if (!int.TryParse(bnEntry.Key, out int bnValue))
                        continue;

                    // Collect matching elements once for this BN
                    List<Element> elements = new FilteredElementCollector(doc)
                        .WhereElementIsNotElementType()
                        .Where(e => e.LookupParameter("Bauteilnummer") != null
                                    && e.LookupParameter("Bauteilnummer").HasValue
                                    && e.LookupParameter("Bauteilnummer").AsInteger() == bnValue)
                        .ToList();

                    if (elements.Count == 0)
                        continue;

                    List<FamilyInstance> familyInstances = elements
                        .OfType<FamilyInstance>()
                        .ToList();

                    List<Element> otherElements = elements
                        .Where(elem => !(elem is FamilyInstance))
                        .ToList();

                    foreach (var paramEntry in bnEntry.Value)
                    {
                        string paramName = paramEntry.Key;
                        string value = paramEntry.Value;

                        if (ExcelParser.ParametersToIgnore.Contains(paramName))
                            continue;

                        foreach (Element elem in familyInstances)
                        {
                            Parameter param = elem.LookupParameter(paramName);
                            if (param == null || param.IsReadOnly)
                                continue;

                            switch (param.StorageType)
                            {
                                case StorageType.String:
                                    param.Set(value);
                                    count++;
                                    break;

                                case StorageType.Integer:
                                    if (int.TryParse(value, out int intVal))
                                    {
                                        param.Set(intVal);
                                        count++;
                                    }
                                    break;

                                case StorageType.Double:
                                    if (double.TryParse(value, out double dblVal))
                                    {
                                        param.Set(dblVal);
                                        count++;
                                    }
                                    break;

                                default:
                                    // Fallback for ElementId or other types
                                    param.SetValueString(value);
                                    count++;
                                    break;
                            }
                        }

                        foreach (Element elem in otherElements)
                        {
                            Parameter param = elem.LookupParameter(paramName);
                            if (param == null || param.IsReadOnly)
                                continue;

                            switch (param.StorageType)
                            {
                                case StorageType.String:
                                    param.Set(value);
                                    count++;
                                    break;

                                case StorageType.Integer:
                                    if (int.TryParse(value, out int intVal))
                                    {
                                        param.Set(intVal);
                                        count++;
                                    }
                                    break;

                                case StorageType.Double:
                                    if (double.TryParse(value, out double dblVal))
                                    {
                                        param.Set(dblVal);
                                        count++;
                                    }
                                    break;

                                default:
                                    // Fallback for ElementId or other types
                                    param.SetValueString(value);
                                    count++;
                                    break;
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
