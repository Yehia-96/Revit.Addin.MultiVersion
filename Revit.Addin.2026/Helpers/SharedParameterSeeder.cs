// File: SharedParameterSeeder.cs (cleaned for Revit 2024)
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Revit.Addin._2026.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using RevitDialog = Autodesk.Revit.UI.TaskDialog;

namespace Revit.Addin._2026.Helpers
{
    // This class ensures that the parameters from the excel file are added
    // to the shared parameter .txt file if they do not already exist.
    public static class SharedParameterSeeder
    {
        // Main method to ensure parameters exist in the shared parameter file.
        // sayNothingNew: also say so when the file had all of them already.
        public static void EnsureParametersExist(
            Application app,
            IEnumerable<IDictionary<string, ParamMetaData>> metaDicts,
            string spPath,
            bool sayNothingNew = true)
        {
            if (!File.Exists(spPath))
                throw new FileNotFoundException("Shared parameter file not found:", spPath);

            string originalSpFile = app.SharedParametersFilename;
            app.SharedParametersFilename = spPath;

            bool addedAny = false;
            List<string> missing;

            try
            {
                DefinitionFile spFile = app.OpenSharedParameterFile();
                if (spFile == null)
                    throw new InvalidOperationException("Failed to open shared-parameter file.");

                // Across ALL groups, not only the target group (safer), and each name once.
                var names = new SharedParameterNames(NamesIn(spFile));

                foreach (ParamMetaData meta in metaDicts.SelectMany(metaDict => metaDict.Values).Where(m => m != null))
                {
                    string parameterName = meta.Name?.Trim();
                    if (string.IsNullOrWhiteSpace(parameterName))
                    {
                        names.Refused("<empty name>");
                        continue;
                    }

                    if (!names.Take(parameterName))
                        continue;

                    if (TryCreate(spFile.Groups, parameterName, meta))
                        addedAny = true;
                    else
                        names.Refused(parameterName);
                }

                // The file is read again: only what it lacks is worth a warning.
                missing = names.Missing(NamesIn(app.OpenSharedParameterFile()));
            }
            finally
            {
                app.SharedParametersFilename = originalSpFile;
            }

            if (missing.Count > 0)
                RevitDialog.Show("Shared Parameters",
                    $"Some parameters could not be created and were skipped: {string.Join(", ", missing)}");
            else if (!addedAny && sayNothingNew)
                RevitDialog.Show("Shared Parameters", "No new parameters were added. All are already present.");
        }

        // The file's names as it was opened; it doesn't show what's created in it afterwards.
        private static List<string> NamesIn(DefinitionFile file)
        {
            if (file == null)
                return new List<string>();
            return file.Groups
                .Cast<DefinitionGroup>()
                .SelectMany(g => g.Definitions.Cast<Definition>())
                .Select(d => d.Name)
                .ToList();
        }

        // False when Revit refuses it.
        private static bool TryCreate(DefinitionGroups groups, string parameterName, ParamMetaData meta)
        {
            string groupName = string.IsNullOrWhiteSpace(meta.GroupName)
                ? "General"
                : meta.GroupName.Trim();

            try
            {
                DefinitionGroup group = groups.get_Item(groupName) ?? groups.Create(groupName);
                try
                {
                    group.Definitions.Create(OptionsFor(parameterName, meta.DataTypeRevit, meta, ""));
                }
                catch (SEHException)
                {
                    group.Definitions.Create(OptionsFor(parameterName, SpecTypeId.String.Text, meta, " (fallback: text)"));
                }
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WARN] Shared parameter '{parameterName}' was refused: {ex.Message}");
                return false;
            }
        }

        // Use the per-parameter prefix (from the Excel prefix row) as the Revit
        // description. This shared-parameter description is inherited by both the
        // family parameter and the project parameter created from this definition.
        // Fall back to the auto-generated text when no prefix is present.
        private static ExternalDefinitionCreationOptions OptionsFor(
            string parameterName, ForgeTypeId dataType, ParamMetaData meta, string note)
        {
            return new ExternalDefinitionCreationOptions(parameterName, dataType)
            {
                Visible = true,
                UserModifiable = true,
                Description = string.IsNullOrWhiteSpace(meta.Description)
                    ? $"Auto-generated on {DateTime.Now:yyyy-MM-dd}{note}"
                    : meta.Description
            };
        }
    }

}
