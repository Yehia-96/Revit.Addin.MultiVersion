using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Revit.Addin._2026.Helpers
{
    /// <summary>
    /// Reloads edited family .rfa files back into the active project and reapplies writable
    /// instance parameter values for instances that belong to those edited families.
    /// </summary>
    public static class EditedFamilyReloadService
    {
        public sealed class ReloadSummary
        {
            public int RequestedPaths { get; internal set; }
            public int UniquePaths { get; internal set; }
            public int SuccessfullyReloadedFamilies { get; internal set; }
            public int SkippedPaths { get; internal set; }
            public int AffectedInstances { get; internal set; }
            public int OverwrittenInstanceValues { get; internal set; }
            public int Errors { get; internal set; }

            public IReadOnlyList<string> ErrorMessages => _errorMessages;

            private readonly List<string> _errorMessages = new List<string>();

            internal void AddError(string message)
            {
                Errors++;
                _errorMessages.Add(message);
            }
        }

        /// <summary>
        /// Reloads edited families from temp .rfa paths and forces overwrite behavior during load.
        /// Then, for instances of reloaded families, re-applies writable parameter values to ensure
        /// instance data is refreshed in the project model.
        /// </summary>
        public static ReloadSummary ReloadEditedFamiliesAndOverwriteInstances(Document doc, IEnumerable<string> editedFamilyPaths)
        {
            if (doc == null)
            {
                throw new ArgumentNullException(nameof(doc));
            }

            if (editedFamilyPaths == null)
            {
                throw new ArgumentNullException(nameof(editedFamilyPaths));
            }

            var summary = new ReloadSummary();
            var distinctPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var reloadedFamilyIds = new HashSet<ElementId>();

            foreach (string rawPath in editedFamilyPaths)
            {
                summary.RequestedPaths++;

                if (string.IsNullOrWhiteSpace(rawPath))
                {
                    summary.SkippedPaths++;
                    continue;
                }

                string normalizedPath = rawPath.Trim();
                if (!distinctPaths.Add(normalizedPath))
                {
                    summary.SkippedPaths++;
                    continue;
                }
            }

            summary.UniquePaths = distinctPaths.Count;

            using (var reloadTx = new Transaction(doc, "Reload edited families"))
            {
                reloadTx.Start();

                foreach (string familyPath in distinctPaths)
                {
                    try
                    {
                        if (!File.Exists(familyPath))
                        {
                            summary.SkippedPaths++;
                            summary.AddError($"Family file does not exist: {familyPath}");
                            continue;
                        }

                        // Revit constraint: to force values from the incoming family file,
                        // IFamilyLoadOptions must return overwriteParameterValues = true.
                        var loadOptions = new OverwriteFamilyLoadOptions();
                        bool loaded = doc.LoadFamily(familyPath, loadOptions, out Family loadedFamily);

                        if (!loaded || loadedFamily == null)
                        {
                            summary.SkippedPaths++;
                            summary.AddError($"Failed to load family: {familyPath}");
                            continue;
                        }

                        reloadedFamilyIds.Add(loadedFamily.Id);
                        summary.SuccessfullyReloadedFamilies++;
                    }
                    catch (Exception ex)
                    {
                        summary.AddError($"Error reloading family '{familyPath}': {ex.Message}");
                    }
                }

                reloadTx.Commit();
            }

            if (reloadedFamilyIds.Count == 0)
            {
                return summary;
            }

            using (var overwriteTx = new Transaction(doc, "Overwrite instance values for reloaded families"))
            {
                overwriteTx.Start();

                IEnumerable<FamilyInstance> affectedInstances = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .OfClass(typeof(FamilyInstance))
                    .Cast<FamilyInstance>()
                    .Where(fi => fi?.Symbol?.Family != null && reloadedFamilyIds.Contains(fi.Symbol.Family.Id));

                foreach (FamilyInstance instance in affectedInstances)
                {
                    summary.AffectedInstances++;

                    foreach (Parameter parameter in instance.Parameters.Cast<Parameter>())
                    {
                        if (parameter == null || parameter.IsReadOnly)
                        {
                            continue;
                        }

                        try
                        {
                            if (!parameter.HasValue)
                            {
                                continue;
                            }

                            bool overwritten = OverwriteParameterWithCurrentValue(parameter);
                            if (overwritten)
                            {
                                summary.OverwrittenInstanceValues++;
                            }
                        }
                        catch (Exception ex)
                        {
                            summary.AddError($"Instance {instance.Id.Value}, parameter '{parameter.Definition?.Name ?? "<unknown>"}' failed: {ex.Message}");
                        }
                    }
                }

                overwriteTx.Commit();
            }

            return summary;
        }

        private static bool OverwriteParameterWithCurrentValue(Parameter parameter)
        {
            switch (parameter.StorageType)
            {
                case StorageType.String:
                    return parameter.Set(parameter.AsString());

                case StorageType.Integer:
                    return parameter.Set(parameter.AsInteger());

                case StorageType.Double:
                    return parameter.Set(parameter.AsDouble());

                case StorageType.ElementId:
                    ElementId idValue = parameter.AsElementId();
                    if (idValue == null)
                    {
                        return false;
                    }

                    return parameter.Set(idValue);

                case StorageType.None:
                default:
                    return false;
            }
        }

        private sealed class OverwriteFamilyLoadOptions : IFamilyLoadOptions
        {
            public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
            {
                overwriteParameterValues = true;
                return true;
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
