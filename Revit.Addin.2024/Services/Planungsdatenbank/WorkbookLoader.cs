// Shared source: keep identical in Revit.Addin.2024 / .2026 / .2027 apart from the namespace year.
// C# 7.3 / net48 floor - see PlanungsdatenbankLayout.cs for the full restriction list.
using System;
using System.Collections.Generic;
using Revit.Addin._2024.Dtos;

namespace Revit.Addin._2024.Services.Planungsdatenbank
{
    /// <summary>
    /// Loads a parameter workbook of either format into the session state the add-in's commands
    /// already read - <see cref="ExcelParser"/>'s collections, <see cref="MaterialCreation.MaterialsList"/>
    /// and <see cref="MaterialAssignment.ElementMaterial"/>.
    /// <para>
    /// This is the one place that decides which reader runs, which is what replaces the old
    /// <c>isNew</c> flag. That flag was declared and never assigned, so first-run loading used one
    /// reader while the menu's "Add Excel" button used another. Both now come through here, so
    /// they cannot disagree.
    /// </para>
    /// <para>
    /// The Material-Bauteilliste path calls the existing reader exactly as before. Only the
    /// Planungsdatenbank path is new.
    /// </para>
    /// </summary>
    internal static class WorkbookLoader
    {
        /// <summary>
        /// Format of the workbook currently loaded. <see cref="ExcelFormat.Unknown"/> until something
        /// loads. Also decides how elements are matched - pass it to <see cref="ElementKey"/>.
        /// </summary>
        internal static ExcelFormat LoadedFormat { get; private set; }

        /// <summary>The workbook currently loaded, as it was read. Null until something loads, another one after each load.</summary>
        internal static WorkbookFile Loaded { get; private set; }

        /// <summary>Path of the workbook currently loaded, or null.</summary>
        internal static string LoadedPath { get { return Loaded == null ? null : Loaded.FullPath; } }

        /// <summary>The workbook currently loaded, for messages: its name and when it was saved. Null when there's none.</summary>
        internal static string LoadedDescription { get { return Loaded == null ? null : Loaded.Description; } }

        /// <summary>
        /// The parameter the modellers type the key into, for the shared parameter file: Bauteil_ID. Empty for the old
        /// format, whose families carry Bauteilnummer as their own parameter.
        /// </summary>
        internal static Dictionary<string, ParamMetaData> IdentityDefinitions { get; private set; }

        /// <summary>Problems from the last load. Always empty for the old format, which reports nothing.</summary>
        internal static List<string> Problems { get; private set; }

        /// <summary>Notes from the last load.</summary>
        internal static List<string> Notes { get; private set; }

        static WorkbookLoader()
        {
            LoadedFormat = ExcelFormat.Unknown;
            IdentityDefinitions = new Dictionary<string, ParamMetaData>();
            Problems = new List<string>();
            Notes = new List<string>();
        }

        /// <summary>
        /// Loads the workbook again when it was saved since it was read; true when it did. Otherwise a value changed in
        /// Excel would never arrive, as the workbook is read once per Revit session.
        /// </summary>
        /// <exception cref="InvalidOperationException">It has changed, and it can't be read. The next call tries again.</exception>
        internal static bool ReloadIfChanged()
        {
            if (Loaded == null || !Loaded.HasChanged()) return false;

            try
            {
                Load(LoadedPath);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "The Excel file has changed, but it couldn't be read again: " + ex.Message, ex);
            }
            return true;
        }

        /// <summary>
        /// Detects the workbook's format and loads it, from a copy, so it may be open in Excel.
        /// <para>
        /// If reading fails, the exception propagates and whatever was loaded before is left as
        /// it was. Every reader in this add-in clears its state before reading, so a failure part
        /// way through would leave the session half-empty; detection runs first and opens the file
        /// read-only, so most bad inputs - wrong file, locked file - are caught before anything is
        /// cleared.
        /// </para>
        /// </summary>
        internal static ExcelFormat Load(string path)
        {
            ExcelFormat format = ExcelFormat.Unknown;
            WorkbookFile file = WorkbookFile.Read(path, copy => format = LoadFrom(copy));

            LoadedFormat = format;
            Loaded = file;
            return format;
        }

        private static ExcelFormat LoadFrom(string path)
        {
            ExcelFormat format = ExcelFormatDetector.Detect(path);

            if (format == ExcelFormat.Planungsdatenbank)
            {
                PlanungsdatenbankParseResult result = PlanungsdatenbankParser.Read(path);
                Apply(result);
                Problems = new List<string>(result.Problems);
                Notes = new List<string>(result.Notes);
            }
            else
            {
                // NewReadExcelStructured clears every collection it fills except this one, so
                // assignments from the previously loaded workbook would otherwise survive - and
                // after a Planungsdatenbank load those are pair keys this format never wrote.
                // Cleared here rather than in the old reader, which stays untouched.
                MaterialAssignment.ElementMaterial.Clear();

                // Unchanged: the reader first-run loading has always used.
                ExcelParser.NewReadExcelStructured(path);
                IdentityDefinitions = new Dictionary<string, ParamMetaData>();
                Problems = new List<string>();
                Notes = new List<string>();
            }

            return format;
        }

        /// <summary>
        /// One-paragraph summary of the last load, for the message the command already shows
        /// after loading. Problems are listed in full; notes are not, to keep it readable.
        /// </summary>
        internal static string Summary()
        {
            string text = ExcelFormatDetector.DisplayName(LoadedFormat) + " workbook loaded: " +
                          ExcelParser.FamilyData.Count + " Bauteile, " +
                          ExcelParser.ParameterDefinitions.Count + " parameters, " +
                          ExcelParser.MaterialData.Count + " materials.";

            if (Problems.Count == 0) return text;

            text += Environment.NewLine + Environment.NewLine + Problems.Count + " problem(s):";
            int shown = 0;
            foreach (string problem in Problems)
            {
                text += Environment.NewLine + "- " + problem;
                if (++shown >= 12)
                {
                    text += Environment.NewLine + "- ... and " + (Problems.Count - shown) + " more.";
                    break;
                }
            }
            return text;
        }

        /// <summary>
        /// Puts a Planungsdatenbank read into the session state, in the shapes the existing
        /// commands consume.
        /// </summary>
        private static void Apply(PlanungsdatenbankParseResult result)
        {
            ExcelParser.ParameterDefinitions.Clear();
            foreach (KeyValuePair<string, ParamMetaData> d in result.ParameterDefinitions)
                ExcelParser.ParameterDefinitions[d.Key] = d.Value;

            ExcelParser.MaterialParameterDefinitions.Clear();
            foreach (KeyValuePair<string, ParamMetaData> d in result.MaterialParameterDefinitions)
                ExcelParser.MaterialParameterDefinitions[d.Key] = d.Value;

            IdentityDefinitions = new Dictionary<string, ParamMetaData>(result.IdentityDefinitions);

            ExcelParser.FamilyData.Clear();
            foreach (KeyValuePair<string, Dictionary<string, string>> row in result.FamilyData)
                ExcelParser.FamilyData[row.Key] = row.Value;

            // Material values are applied to Revit materials matched by name, so they are re-keyed
            // from Material-ID to Materialname here. The parser keeps them by ID because that is
            // the workbook's identity; Revit's identity for a material is its name.
            ExcelParser.MaterialData.Clear();
            foreach (KeyValuePair<string, Dictionary<string, string>> material in result.MaterialData)
            {
                string name;
                if (result.MaterialNamesById.TryGetValue(material.Key, out name))
                    ExcelParser.MaterialData[name] = material.Value;
            }

            // Empty by design: this format marks modeller-owned values per cell, and those cells
            // were already left out of FamilyData.
            ExcelParser.ParametersToIgnore.Clear();

            MaterialCreation.MaterialsList.Clear();
            foreach (PlanungsdatenbankMaterial m in result.Materials)
            {
                try
                {
                    MaterialCreation.MaterialsList.Add(new MaterialsDto(
                        materialName: m.Name,
                        materialType: m.Type,
                        cutPattern: m.CutPattern ?? string.Empty,
                        surfacePattern: m.SurfacePattern ?? string.Empty));
                }
                catch (ArgumentException ex)
                {
                    result.Problems.Add("Material \"" + m.Name + "\" could not be prepared for creation: " + ex.Message);
                }
            }

            // Keyed by the same pair key as FamilyData; MaterialAssignment reads both shapes.
            MaterialAssignment.ElementMaterial.Clear();
            foreach (KeyValuePair<string, string> assignment in result.ElementMaterial)
                MaterialAssignment.ElementMaterial[assignment.Key] = assignment.Value;
        }
    }
}
