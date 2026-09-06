// File: SharedParameterSeeder.cs (cleaned for Revit 2024)
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Revit.Addin._2024.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using RevitDialog = Autodesk.Revit.UI.TaskDialog;

namespace Revit.Addin._2024.Helpers
{
    // This class ensures that the parameters from the excel file are added
    // to the shared parameter .txt file if they do not already exist.
    public static class SharedParameterSeeder
    {
        // Main method to ensure parameters exist in the shared parameter file
        public static void EnsureParametersExist(
    Application app,
    IEnumerable<IDictionary<string, ParamMetaData>> metaDicts,
    string spPath)
        {
            if (!File.Exists(spPath))
                throw new FileNotFoundException("Shared parameter file not found:", spPath);

            string originalSpFile = app.SharedParametersFilename;
            app.SharedParametersFilename = spPath;

            bool addedAny = false;
            var failedParameters = new List<string>();

            try
            {
                DefinitionFile spFile = app.OpenSharedParameterFile();
                if (spFile == null)
                    throw new InvalidOperationException("Failed to open shared-parameter file.");

                DefinitionGroups groups = spFile.Groups;

                foreach (var metaDict in metaDicts)
                {
                    foreach (ParamMetaData meta in metaDict.Values)
                    {
                        if (meta == null)
                            continue;

                        string parameterName = meta.Name?.Trim();
                        if (string.IsNullOrWhiteSpace(parameterName))
                        {
                            failedParameters.Add("<empty name>");
                            continue;
                        }

          

                        string groupName = string.IsNullOrWhiteSpace(meta.GroupName)
                            ? "General"
                            : meta.GroupName.Trim();

                        DefinitionGroup group = groups.get_Item(groupName) ?? groups.Create(groupName);

                        // IMPORTANT: check across ALL groups, not only the target group (safer)
                        bool existsAnywhere = groups
                            .Cast<DefinitionGroup>()
                            .SelectMany(g => g.Definitions.Cast<Definition>())
                            .Any(d => d.Name.Equals(meta.Name, StringComparison.OrdinalIgnoreCase));

                        if (existsAnywhere)
                            continue;

                        // Use the per-parameter prefix (from the Excel prefix row) as the Revit
                        // description. This shared-parameter description is inherited by both the
                        // family parameter and the project parameter created from this definition.
                        // Fall back to the auto-generated text when no prefix is present.
                        string description = string.IsNullOrWhiteSpace(meta.Description)
                            ? $"Auto-generated on {DateTime.Now:yyyy-MM-dd}"
                            : meta.Description;

                        var opts = new ExternalDefinitionCreationOptions(parameterName, meta.DataTypeRevit)
                        {
                            Visible = true,
                            UserModifiable = true,
                            Description = description
                        };

                        try
                        {
                            group.Definitions.Create(opts);
                            addedAny = true;
                        }
                        catch (SEHException)
                        {
                            try
                            {
                                var textFallbackOpts = new ExternalDefinitionCreationOptions(parameterName, SpecTypeId.String.Text)
                                {
                                    Visible = true,
                                    UserModifiable = true,
                                    Description = string.IsNullOrWhiteSpace(meta.Description)
                                        ? $"Auto-generated on {DateTime.Now:yyyy-MM-dd} (fallback: text)"
                                        : meta.Description
                                };

                                group.Definitions.Create(textFallbackOpts);
                                addedAny = true;
                            }
                            catch
                            {
                                failedParameters.Add(parameterName);
                            }
                        }
                        catch
                        {
                            failedParameters.Add(parameterName);
                        }
                    }
                }
            }
            finally
            {
                app.SharedParametersFilename = originalSpFile;

                if (!addedAny)
                    RevitDialog.Show("Shared Parameters", "No new parameters were added. All are already present.");

                if (failedParameters.Count > 0)
                {
                    string failed = string.Join(", ", failedParameters.Distinct(StringComparer.OrdinalIgnoreCase));
                    RevitDialog.Show("Shared Parameters",
                        $"Some parameters could not be created and were skipped: {failed}");
                }
            }
        }


    }

}
