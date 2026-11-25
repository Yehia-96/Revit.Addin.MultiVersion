using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit.Addin._2027.Services;
using Revit.Addin._2027.Services.Planungsdatenbank;
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
        /// <param name="selectedKeys">
        /// The rows to apply, as keys of <paramref name="familyData"/>: Bauteilnummern for the
        /// Material-Bauteilliste, Bauteil-IDs for the Planungsdatenbank.
        /// </param>
        /// <param name="keyFormat">
        /// Which identifier connects an element to its row - see <see cref="ElementKey"/>. Defaults
        /// to the Material-Bauteilliste, matching on Bauteilnummer as this always has.
        /// </param>
        public static ParameterRun AddParametersToElements(
            Document projectDoc,
            Dictionary<string, ParamMetaData> parameterMeta,
            Dictionary<string, Dictionary<string, string>> familyData,
            RevitApp app,
            List<string> selectedKeys,
            string sharedParameterFilePath,
            ExcelFormat keyFormat = ExcelFormat.Bauteilliste)
        {
            if (projectDoc == null) throw new ArgumentNullException(nameof(projectDoc));
            if (parameterMeta == null) throw new ArgumentNullException(nameof(parameterMeta));
            if (familyData == null) throw new ArgumentNullException(nameof(familyData));
            if (app == null) throw new ArgumentNullException(nameof(app));
            if (selectedKeys == null) throw new ArgumentNullException(nameof(selectedKeys));
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
            var notAdded = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // Collect relevant elements (selected keys only), read the same way the data was keyed
            var selectedSet = new HashSet<string>(
                selectedKeys.Select(k => ElementKey.Normalise(k, keyFormat)).Where(k => k != null),
                ElementKey.KeyComparer);
            List<Element> matchingElements = new FilteredElementCollector(projectDoc)
                .WhereElementIsNotElementType()
                .Where(elem =>
                {
                    string key = ElementKey.Read(elem, keyFormat);
                    return key != null && selectedSet.Contains(key);
                })
                .ToList();

            if (matchingElements.Count == 0)
                return new ParameterRun(0, 0, 0);

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
                string bnKey = ElementKey.Read(fi, keyFormat);
                if (bnKey == null) continue;

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
                    required,
                    notAdded);

                parametersAdded += added;
            }

            // -------------------------------
            // PHASE 1B: BIND SHARED PARAMS TO NON-FAMILY CATEGORIES
            // -------------------------------
            // Per parameter, the categories of the elements that lack it. An element that has it, from another
            // shared parameter file say, mustn't get a second one of that name: nobody could tell them apart.
            var lackingCategories = new Dictionary<string, CategorySet>(StringComparer.OrdinalIgnoreCase);

            foreach (Element elem in otherElements)
            {
                string bnKey = ElementKey.Read(elem, keyFormat);
                if (bnKey == null) continue;

                if (!familyData.TryGetValue(bnKey, out var expectedParams))
                    continue;

                Category category = elem.Category;
                if (category == null || !category.AllowsBoundParameters)
                    continue;

                foreach (string paramName in expectedParams.Keys)
                {
                    if (ExcelParser.ParametersToIgnore.Contains(paramName))
                        continue;

                    if (!parameterMeta.ContainsKey(paramName) || HasOwnParameter(elem, paramName))
                        continue;

                    CategorySet lacking;
                    if (!lackingCategories.TryGetValue(paramName, out lacking))
                    {
                        lacking = app.Create.NewCategorySet();
                        lackingCategories.Add(paramName, lacking);
                    }

                    try
                    {
                        lacking.Insert(category);
                    }
                    catch (ArgumentException)
                    {
                        // Category already in set.
                    }
                }
            }

            if (lackingCategories.Count > 0)
            {
                int bound = BindParametersToCategories(
                    projectDoc,
                    app,
                    defsByName,
                    parameterMeta,
                    lackingCategories,
                    notAdded);

                parametersAdded += bound;
            }


            // -------------------------------
            // PHASE 2: APPLY VALUES
            // -------------------------------
            // The loop itself is shared with the database link (ElementValueWriter); the rules are this route's.
            var rules = new ExcelValueRules();
            Func<Element, string> keyOf = elem => ElementKey.Read(elem, keyFormat);
            List<Tuple<Parameter, ParameterValue>> familyTuples = ElementValueWriter.Collect(
                projectDoc, familyInstances, keyOf, familyData, parameterMeta, rules);
            List<Tuple<Parameter, ParameterValue>> otherTuples = ElementValueWriter.Collect(
                projectDoc, otherElements, keyOf, familyData, parameterMeta, rules);
            valuesWritten = familyTuples.Count + otherTuples.Count;

            if (familyTuples.Count > 0)
                ElementValueWriter.Write(projectDoc, "Apply Family Instance Parameter Values", familyTuples);

            if (otherTuples.Count > 0)
                ElementValueWriter.Write(projectDoc, "Apply Non-Family Parameter Values", otherTuples);

            IEnumerable<string> notWritten = NotWritable(projectDoc, matchingElements, keyFormat, familyData, parameterMeta)
                .Where(name => !notAdded.ContainsKey(name));
            return new ParameterRun(matchingElements.Count, parametersAdded, valuesWritten, notAdded.Values, notWritten);
        }

        // The element's parameter of that name, the project's or the family's before Revit's own: in German, "Dicke"
        // and "Typ" are Revit's too.
        private static Parameter Find(Element element, string name)
        {
            Parameter parameter = element.LookupParameter(name);
            if (parameter == null || IsOwn(parameter))
                return parameter;
            return element.GetParameters(name).FirstOrDefault(IsOwn) ?? parameter;
        }

        private static bool IsOwn(Parameter parameter)
        {
            var definition = parameter.Definition as InternalDefinition;
            return definition == null || definition.BuiltInParameter == BuiltInParameter.INVALID;
        }

        private static bool HasOwnParameter(Element element, string name)
        {
            Parameter parameter = Find(element, name);
            return parameter != null && IsOwn(parameter);
        }

        // The workbook's value as the parameter can take it; null when it can't.
        private static ParameterValue ValueFor(Document projectDoc, Parameter parameter, ParamMetaData meta, string value)
        {
            return ParamMetaData.Fitting(
                parameter, ParamMetaData.ConvertValue(projectDoc, meta.DataTypeRevit, value, meta.Unit), value);
        }

        // The parameters an element has a value for in the workbook, and can't take: it lacks them, or has them
        // read-only or of another kind. A blank cell isn't a value; the Material-Bauteilliste's reader gives those too.
        private static IEnumerable<string> NotWritable(
            Document projectDoc,
            IEnumerable<Element> elements,
            ExcelFormat keyFormat,
            Dictionary<string, Dictionary<string, string>> familyData,
            Dictionary<string, ParamMetaData> parameterMeta)
        {
            var names = new SortedSet<string>(ElementKey.NaturalOrder);
            foreach (Element element in elements)
            {
                string key = ElementKey.Read(element, keyFormat);
                Dictionary<string, string> row;
                if (key == null || !familyData.TryGetValue(key, out row))
                    continue;

                foreach (KeyValuePair<string, string> value in row)
                {
                    ParamMetaData meta;
                    if (string.IsNullOrWhiteSpace(value.Value) || names.Contains(value.Key)
                        || ExcelParser.ParametersToIgnore.Contains(value.Key) || !parameterMeta.TryGetValue(value.Key, out meta))
                        continue;

                    Parameter parameter = Find(element, value.Key);
                    if (parameter != null && !IsParameterEmpty(parameter) && ParamMetaData.AreValuesEqual(parameter, value.Value))
                        continue;

                    if (parameter == null || parameter.IsReadOnly || ValueFor(projectDoc, parameter, meta, value.Value) == null)
                        names.Add(value.Key);
                }
            }
            return names;
        }

        /// <summary>
        /// The Excel route's rules: parameters by name, the workbook's ignore row, write if EMPTY OR
        /// DIFFERENT, and the workbook's data type and unit.
        /// </summary>
        private sealed class ExcelValueRules : IValueRules
        {
            public bool IsIgnored(string parameterName) => ExcelParser.ParametersToIgnore.Contains(parameterName);

            public Parameter Find(Element element, string parameterName, ParamMetaData definition) =>
                SeedProjectElementsParameters.Find(element, parameterName);

            public bool NeedsValue(Parameter parameter, string value, ParamMetaData definition) =>
                IsParameterEmpty(parameter) || !ParamMetaData.AreValuesEqual(parameter, value);

            public ParameterValue Convert(Document document, Parameter parameter, string value, ParamMetaData definition) =>
                ValueFor(document, parameter, definition, value);
        }

        private static int BindParametersToCategories(
            Document projectDoc,
            RevitApp app,
            Dictionary<string, Definition> defsByName,
            Dictionary<string, ParamMetaData> parameterMeta,
            IDictionary<string, CategorySet> categoriesByParameter,
            IDictionary<string, string> notAdded)
        {
            int bound = 0;

            using (Transaction tx = new Transaction(projectDoc, "Bind shared parameters to categories"))
            {
                tx.Start();

                foreach (KeyValuePair<string, CategorySet> required in categoriesByParameter)
                {
                    string name = required.Key;
                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    if (!parameterMeta.TryGetValue(name, out var meta))
                        continue;

                    if (!defsByName.TryGetValue(name, out var def))
                        continue;

                    ForgeTypeId groupId = meta.GroupNameRevit ?? GroupTypeId.General;

                    BindingOutcome outcome = ProjectParameterBinder.Add(projectDoc, app, def, required.Value, groupId);
                    if (outcome == BindingOutcome.Added)
                        bound++;

                    string problem = ProjectParameterBinder.Problem(name, outcome);
                    if (problem != null)
                        notAdded[name] = problem;
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
            List<string> requiredParameterNames,
            IDictionary<string, string> notAdded)
        {
            if (requiredParameterNames == null || requiredParameterNames.Count == 0)
                return 0;

            if (family == null)
                return 0;

            if (family.IsInPlace)
            {
                Debug.WriteLine($"[WARN] Skipping in-place family '{family.Name}' (cannot be edited).");
                
                return BindParametersForInPlaceFamily(projectDoc, app, defsByName, parameterMeta, requiredParameterNames, family, notAdded);
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

        private static int BindParametersForInPlaceFamily(
            Document projectDoc,
            RevitApp app,
            Dictionary<string, Definition> defsByName,
            Dictionary<string, ParamMetaData> parameterMeta,
            IEnumerable<string> requiredParameterNames,
            Family family,
            IDictionary<string, string> notAdded)
        {
            if (projectDoc == null || app == null || family == null)
                return 0;

            Category category = family.FamilyCategory;
            if (category == null || !category.AllowsBoundParameters)
                return 0;

            var categorySet = app.Create.NewCategorySet();
            try
            {
                categorySet.Insert(category);
            }
            catch (ArgumentException)
            {
                // Category already in set.
            }

            var names = new Dictionary<string, CategorySet>(StringComparer.OrdinalIgnoreCase);
            foreach (string name in requiredParameterNames ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(name))
                    continue;
                if (ExcelParser.ParametersToIgnore.Contains(name))
                    continue;
                if (parameterMeta.ContainsKey(name))
                    names[name] = categorySet;
            }

            if (names.Count == 0)
                return 0;

            return BindParametersToCategories(projectDoc, app, defsByName, parameterMeta, names, notAdded);
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
