using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Microsoft.Bot.Schema;
using Revit.Addin._2024.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Revit.Addin._2024.Helpers
{
    public static class ParameterCategoryUnassigner
    {
        private static readonly HashSet<string> ParametersToUnassignFromCategories = new HashSet<string>(
            new[]
            {
                "Beton_Festigkeitsklasse",
                "Beton_Mat-Nr.",
                "Betonstahl_Güte",
                "Betonstahl_Mat-Nr.",
                "Bewehrung"
            },
            StringComparer.OrdinalIgnoreCase);

        private static List<string> _editedFamilyPaths = new List<string>();

        private static List<Document> _famDocs = new List<Document>();

        private static readonly HashSet<string> ParameterNamesToRemoveFromFamily = new HashSet<string>
        {
            "Norm",
            "a"
        };

        public static HashSet<string> GetParametersToRemoveFromFamily()
        {
            return ParameterNamesToRemoveFromFamily;
        }
        public static void UnassignConfiguredParametersFromAllCategories(Document projectDoc)
        {
            if (projectDoc == null)
                return;

            // Collect the exact Definition objects from ParameterBindings
            List<Definition> definitionsToRemove = GetDefinitionsFromBindings(projectDoc);

            if (definitionsToRemove.Count == 0)
            {
                TaskDialog.Show("Info", "No parameters found to unassign.");
                return;
            }

            using (Transaction tx = new Transaction(projectDoc, "Unassign configured parameters from categories"))
            {
                tx.Start();

                foreach (Definition def in definitionsToRemove)
                {
                    try
                    {
                        // Try to remove the binding
                        bool removed = projectDoc.ParameterBindings.Remove(def);

                        if (removed)
                        {
                            continue;
                        }

                        // If simple Remove fails, try ReInsert with empty CategorySet
                        try
                        {
                            CategorySet emptySet = projectDoc.Application.Create.NewCategorySet();
                            InstanceBinding emptyBinding = projectDoc.Application.Create.NewInstanceBinding(emptySet);

                            bool reinserted = projectDoc.ParameterBindings.ReInsert(def, emptyBinding, null);
                            if (reinserted)
                            {
                                continue;
                            }
                        }
                        catch (Exception reInsertEx)
                        {
                            TaskDialog.Show("Error", $"ReInsert failed for '{def.Name}': {reInsertEx.Message}");
                        }

                        // Last attempt: try Remove again
                        projectDoc.ParameterBindings.Remove(def);

                        TaskDialog.Show("Warning", $"Parameter binding for '{def.Name}' may not have been completely removed. It may be locked or in use in this project.");
                    }
                    catch (Exception ex)
                    {
                        TaskDialog.Show("Error", $"Exception removing parameter binding for '{def.Name}': {ex.Message}");
                    }
                }

                tx.Commit();
            }

            TaskDialog.Show("Info", "Parameter category unassignment completed. Verify results in Manage > Project Parameters.");
        }

        private static List<Definition> GetDefinitionsFromBindings(Document projectDoc)
        {
            var result = new List<Definition>();

            if (projectDoc == null)
                return result;

            BindingMap map = projectDoc.ParameterBindings;
            DefinitionBindingMapIterator iterator = map.ForwardIterator();
            iterator.Reset();

            while (iterator.MoveNext())
            {
                Definition def = iterator.Key as Definition;

                if (def != null &&
                    !string.IsNullOrWhiteSpace(def.Name) &&
                    ParametersToUnassignFromCategories.Contains(def.Name))
                {
                    result.Add(def);
                }
            }

            return result;
        }

        public static void RemoveParameterFromFamily(Document doc)
        {
            if (doc == null)
                return;

            var errors = new List<string>();
            int processedCount = 0;
            int removedCount = 0;

            _editedFamilyPaths.Clear();

            FilteredElementCollector collector = new FilteredElementCollector(doc)
                .OfClass(typeof(Family));

            foreach (Family family in collector.Cast<Family>())
            {
                if (family == null || family.IsInPlace || !family.IsEditable)
                    continue;

                Document familyDoc = null;

                familyDoc = doc.EditFamily(family);
                var famMgr = familyDoc.FamilyManager;
                if (famMgr == null)
                    continue;

                var parametersToRemove = famMgr.Parameters
                    .Cast<FamilyParameter>()
                    .Where(p => p?.Definition != null &&
                                ParameterNamesToRemoveFromFamily.Contains(p.Definition.Name))
                    .ToList();

                if (parametersToRemove.Count == 0)
                    continue;

                using (var ft = new Transaction(familyDoc, $"Remove parameters from {family.Name}"))
                {
                    ft.Start();

                    foreach (var param in parametersToRemove)
                    {
                            famMgr.RemoveParameter(param);
                            removedCount++;    
                    }

                    ft.Commit();
                }

                // string tempPath = Path.Combine(Path.GetTempPath(), $"{family.Name}.rfa");
                // familyDoc.SaveAs(tempPath, new SaveAsOptions { OverwriteExistingFile = true });
                // _editedFamilyPaths.Add(tempPath);
                // processedCount++;

                try
                {
                    Family loaded = familyDoc.LoadFamily(doc, new SimpleLoad());
                    TaskDialog.Show("Fam", $"name: {loaded.Name} {removedCount}");
                }
                catch (Exception ex)
                {
                    TaskDialog.Show("Test", "I stepped into the exception ");
                    TaskDialog.Show("Reload failed", ex.ToString());
                }

                if (familyDoc != null)
                {
                    TaskDialog.Show("Test", "I stepped into the final F.block");

                    familyDoc.Close(true);

                    TaskDialog.Show("Test", "I stepped into the second final F.block");
                }
            }
        }

        public static void LoadEdited(Document doc)
        {
            try
            {
                bool loaded = doc.LoadFamily("FamilyRemoval.rfa", new SimpleLoad(), out Family fam);
                TaskDialog.Show("Family", $"Family state: {loaded}");
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Error", $"Exception {ex.Message}");
            }
        }

        public static void LoadEditedFamiliesIntoProject(Document doc)
        {
            if (doc == null || _editedFamilyPaths.Count == 0)
            {
                TaskDialog.Show("Load Families", "No edited families to load.");
                return;
            }

            var errors = new List<string>();
            int loadedCount = 0;

            foreach (string familyPath in _editedFamilyPaths)
            {
                if (!File.Exists(familyPath))
                {
                    errors.Add($"File not found: {familyPath}");
                    continue;
                }
                TaskDialog.Show("FamilyPath", $"Path: {familyPath}");
                try
                {
                    string symbolName = Path.GetFileNameWithoutExtension(familyPath); // or iterate symbols
                    // LoadFamily from path handles its own transaction internally
                    bool loaded = doc.LoadFamily("FamilyRemoval.rfa");
                   // doc.LoadFamily(familyPath, new SimpleLoad(), out Family loadedFamily);
                    TaskDialog.Show("Family", $"Family state: {loaded}");
                    loadedCount++;
                }
                catch (Exception ex)
                {
                    errors.Add($"Failed to load '{Path.GetFileName(familyPath)}': {ex.Message}");
                }
            }

            // Clean up temp files after loading
            foreach (string familyPath in _editedFamilyPaths)
            {
                try
                {
                    if (File.Exists(familyPath))
                        File.Delete(familyPath);
                }
                catch { }
            }

            _editedFamilyPaths.Clear();

            string summary = $"Successfully loaded {loadedCount} families into the project.";
            string detail = errors.Count > 0 ? "\n\nWarnings:\n" + string.Join("\n", errors) : string.Empty;
            TaskDialog.Show("Load Families", summary + detail);
        }
        private static List<FamilyParameter> GetDefinitionsFromFamily(Family family)
        {
            var result = new List<FamilyParameter>();
            if (family == null)
                return result;
            foreach (FamilyParameter param in family.Parameters)
            {
                if (param != null &&
                    ParameterNamesToRemoveFromFamily.Contains(param.Definition.Name))
                {
                    result.Add(param);
                }
            }
            return result;
        }

        public static void PopulateRemovalHashSet(string paramName)
        {
            if (string.IsNullOrWhiteSpace(paramName))
                return;
            ParameterNamesToRemoveFromFamily.Add(paramName);
        }
        public static void Clear()
        {
            ParameterNamesToRemoveFromFamily.Clear();
        }
    }
}
