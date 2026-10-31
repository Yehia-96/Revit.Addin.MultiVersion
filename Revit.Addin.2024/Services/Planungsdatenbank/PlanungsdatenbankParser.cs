// Shared source: keep identical in Revit.Addin.2024 / .2026 / .2027.
// C# 7.3 / net48 floor - see PlanungsdatenbankLayout.cs for the full restriction list.
using System;
using System.Collections.Generic;
using System.IO;
using ClosedXML.Excel;

namespace Revit.Addin._2024.Services.Planungsdatenbank
{
    /// <summary>
    /// What one read of a Planungsdatenbank workbook produced.
    /// <para>
    /// Returned rather than written into <c>ExcelParser</c>'s statics on purpose: this reader is
    /// additive, so nothing that works today can change behaviour because of it. Wiring it into
    /// the command is a separate, later step.
    /// </para>
    /// </summary>
    internal sealed class PlanungsdatenbankParseResult
    {
        /// <summary>Family parameter definitions, keyed by parameter name.</summary>
        internal Dictionary<string, ParamMetaData> ParameterDefinitions { get; private set; }

        /// <summary>Material parameter definitions, keyed by parameter name.</summary>
        internal Dictionary<string, ParamMetaData> MaterialParameterDefinitions { get; private set; }

        /// <summary>
        /// Family values: key is <see cref="PlanungsdatenbankParser.FamilyKey(int,int)"/>, value is
        /// parameter name to value. Only cells carrying a real value are present - skipped cells
        /// are absent rather than empty, so a caller cannot accidentally write a blank over
        /// something the modeller set.
        /// </summary>
        internal Dictionary<string, Dictionary<string, string>> FamilyData { get; private set; }

        /// <summary>Material values, keyed by Material-ID.</summary>
        internal Dictionary<string, Dictionary<string, string>> MaterialData { get; private set; }

        /// <summary>
        /// Always empty for this format, and deliberately so. The old workbook marked whole
        /// parameters as modeller-owned in a dedicated row; this one marks individual cells, and
        /// those are already excluded from <see cref="FamilyData"/>. Kept so a caller can treat
        /// both formats through one shape.
        /// </summary>
        internal HashSet<string> ParametersToIgnore { get; private set; }

        /// <summary>Things that are wrong and a person should see. Empty means a clean read.</summary>
        internal List<string> Problems { get; private set; }

        /// <summary>Things worth knowing that are not wrong.</summary>
        internal List<string> Notes { get; private set; }

        /// <summary>Distinct <c>Datentyp</c> values seen, so unmapped ones can be reported.</summary>
        internal HashSet<string> DataTypesSeen { get; private set; }

        /// <summary>Distinct <c>Parametergruppe</c> values seen.</summary>
        internal HashSet<string> ParameterGroupsSeen { get; private set; }

        internal PlanungsdatenbankParseResult()
        {
            ParameterDefinitions = new Dictionary<string, ParamMetaData>(StringComparer.OrdinalIgnoreCase);
            MaterialParameterDefinitions = new Dictionary<string, ParamMetaData>(StringComparer.OrdinalIgnoreCase);
            FamilyData = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            MaterialData = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            ParametersToIgnore = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Problems = new List<string>();
            Notes = new List<string>();
            DataTypesSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            ParameterGroupsSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Reads the Planungsdatenbank workbook: family parameters from <c>Objektdatenbank</c> and
    /// material parameters from <c>Materialdatenbank</c>.
    /// <para>
    /// The two sheets share one layout - label column B, label rows 2-11, data below - so one
    /// routine reads both and only the identity column differs.
    /// </para>
    /// </summary>
    internal static class PlanungsdatenbankParser
    {
        /// <summary>
        /// The family key, built from the pair that actually identifies a row.
        /// <para>
        /// Bauteilnummer alone is not unique - one Bauteil can have several sub-elements - so
        /// using it alone would let the last row read silently win. Both the reader and whatever
        /// matches Revit elements must build this key the same way, which is why it lives here
        /// as one function rather than being formatted at each call site.
        /// </para>
        /// </summary>
        internal static string FamilyKey(int bauteilnummer, int bauteilunternummer)
        {
            return bauteilnummer.ToString(System.Globalization.CultureInfo.InvariantCulture) + "-" +
                   bauteilunternummer.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Reads the workbook at <paramref name="path"/>.
        /// </summary>
        /// <exception cref="InvalidDataException">A required sheet or label row is missing.</exception>
        internal static PlanungsdatenbankParseResult Read(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path is empty.", "path");
            if (!File.Exists(path)) throw new FileNotFoundException("Workbook not found.", path);

            var result = new PlanungsdatenbankParseResult();

            using (var workbook = new XLWorkbook(path))
            {
                ReadObjects(workbook, result);
                ReadMaterials(workbook, result);
            }

            if (result.ParameterDefinitions.Count == 0)
                result.Problems.Add("No family parameters were found in " + PlanungsdatenbankLayout.Sheets.Objects + ".");
            if (result.FamilyData.Count == 0)
                result.Problems.Add("No family rows were found in " + PlanungsdatenbankLayout.Sheets.Objects + ".");

            return result;
        }

        // --- Objektdatenbank ------------------------------------------------------------------

        private static void ReadObjects(XLWorkbook workbook, PlanungsdatenbankParseResult result)
        {
            SheetGrid sheet = SheetGrid.Open(workbook, PlanungsdatenbankLayout.Sheets.Objects);

            int nameRow = sheet.RequireLabelRow(PlanungsdatenbankLayout.Labels.ParameterName);
            int groupRow = sheet.RequireLabelRow(PlanungsdatenbankLayout.Labels.ParameterGroup);
            int typeRow = sheet.RequireLabelRow(PlanungsdatenbankLayout.Labels.DataType);
            int idRow = sheet.LabelRow(
                PlanungsdatenbankLayout.Labels.ParameterId,
                PlanungsdatenbankLayout.Labels.ParameterIdTypo);

            List<string> duplicateHeaders;
            Dictionary<string, int> columns = sheet.HeaderColumns(nameRow, out duplicateHeaders);
            foreach (string duplicate in duplicateHeaders)
            {
                result.Problems.Add(PlanungsdatenbankLayout.Sheets.Objects +
                    " has more than one \"" + duplicate + "\" column; only the first was read.");
            }

            int bnColumn, bunColumn;
            if (!columns.TryGetValue(PlanungsdatenbankLayout.Headers.Bauteilnummer, out bnColumn))
            {
                throw new InvalidDataException(PlanungsdatenbankLayout.Sheets.Objects +
                    " has no \"" + PlanungsdatenbankLayout.Headers.Bauteilnummer + "\" column.");
            }
            if (!columns.TryGetValue(PlanungsdatenbankLayout.Headers.Bauteilunternummer, out bunColumn))
            {
                throw new InvalidDataException(PlanungsdatenbankLayout.Sheets.Objects +
                    " has no \"" + PlanungsdatenbankLayout.Headers.Bauteilunternummer + "\" column.");
            }

            // Definitions. The two key columns are identity, not parameters to push into Revit.
            foreach (KeyValuePair<string, int> column in columns)
            {
                if (column.Value == bnColumn || column.Value == bunColumn) continue;

                string group = sheet.Text(groupRow, column.Value);
                string type = sheet.Text(typeRow, column.Value);
                result.DataTypesSeen.Add(type.Length == 0 ? "(blank)" : type);
                result.ParameterGroupsSeen.Add(group.Length == 0 ? "(blank)" : group);

                var meta = new ParamMetaData(column.Key, group, type);
                meta.Target = ParamMetaData.ParamTarget.FamilyElement;

                // Revit's parameter Description. The old format carries a prefix row for this;
                // this workbook has none, so the stable Parameter-ID is used instead. It is a
                // real identifier rather than the random placeholder the old reader falls back to.
                if (idRow > 0) meta.Description = sheet.Text(idRow, column.Value);

                result.ParameterDefinitions[column.Key] = meta;
            }

            // Values.
            int firstDataRow = sheet.FirstDataRow();
            if (firstDataRow <= 0)
            {
                result.Problems.Add(PlanungsdatenbankLayout.Sheets.Objects + " has no data rows below its labels.");
                return;
            }

            int rowsRead = 0, rowsSkipped = 0;
            for (int row = firstDataRow; row <= sheet.LastRow; row++)
            {
                string bnText = sheet.Text(row, bnColumn);
                string bunText = sheet.Text(row, bunColumn);
                if (bnText.Length == 0 && bunText.Length == 0) continue;

                int bn, bun;
                if (!SheetGrid.TryInt(bnText, out bn))
                {
                    rowsSkipped++;
                    result.Problems.Add("Row " + row + ": \"" + PlanungsdatenbankLayout.Headers.Bauteilnummer +
                        "\" is \"" + bnText + "\", which is not a whole number. Row skipped.");
                    continue;
                }
                // A missing sub-number is normal and means 0, not an error.
                if (!SheetGrid.TryInt(bunText, out bun)) bun = 0;

                string key = FamilyKey(bn, bun);
                if (result.FamilyData.ContainsKey(key))
                {
                    result.Problems.Add("Row " + row + ": " + PlanungsdatenbankLayout.Headers.Bauteilnummer +
                        " " + bn + " / " + PlanungsdatenbankLayout.Headers.Bauteilunternummer + " " + bun +
                        " appears more than once. Only the first was kept.");
                    continue;
                }

                var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (KeyValuePair<string, int> column in columns)
                {
                    if (column.Value == bnColumn || column.Value == bunColumn) continue;

                    string raw = sheet.Text(row, column.Value);
                    // Absent rather than blank: a skipped cell must never be written over
                    // whatever the modeller already put in Revit.
                    if (PlanungsdatenbankLayout.IsSkipped(raw)) continue;
                    values[column.Key] = raw;
                }

                result.FamilyData[key] = values;
                rowsRead++;
            }

            result.Notes.Add(PlanungsdatenbankLayout.Sheets.Objects + ": " + rowsRead + " rows, " +
                result.ParameterDefinitions.Count + " parameters" +
                (rowsSkipped > 0 ? ", " + rowsSkipped + " rows skipped" : "") + ".");
        }

        // --- Materialdatenbank ----------------------------------------------------------------

        private static void ReadMaterials(XLWorkbook workbook, PlanungsdatenbankParseResult result)
        {
            if (!SheetGrid.HasSheet(workbook, PlanungsdatenbankLayout.Sheets.Materials))
            {
                result.Notes.Add("This workbook has no " + PlanungsdatenbankLayout.Sheets.Materials +
                    " sheet, so no material data was read.");
                return;
            }

            SheetGrid sheet = SheetGrid.Open(workbook, PlanungsdatenbankLayout.Sheets.Materials);

            int nameRow = sheet.RequireLabelRow(PlanungsdatenbankLayout.Labels.ParameterName);
            int groupRow = sheet.RequireLabelRow(PlanungsdatenbankLayout.Labels.ParameterGroup);
            int typeRow = sheet.RequireLabelRow(PlanungsdatenbankLayout.Labels.DataType);
            int idRow = sheet.LabelRow(
                PlanungsdatenbankLayout.Labels.ParameterId,
                PlanungsdatenbankLayout.Labels.ParameterIdTypo);

            List<string> duplicateHeaders;
            Dictionary<string, int> columns = sheet.HeaderColumns(nameRow, out duplicateHeaders);
            foreach (string duplicate in duplicateHeaders)
            {
                result.Problems.Add(PlanungsdatenbankLayout.Sheets.Materials +
                    " has more than one \"" + duplicate + "\" column; only the first was read.");
            }

            int idColumn;
            if (!columns.TryGetValue(PlanungsdatenbankLayout.Headers.MaterialId, out idColumn))
            {
                throw new InvalidDataException(PlanungsdatenbankLayout.Sheets.Materials +
                    " has no \"" + PlanungsdatenbankLayout.Headers.MaterialId + "\" column.");
            }

            foreach (KeyValuePair<string, int> column in columns)
            {
                if (column.Value == idColumn) continue;

                string group = sheet.Text(groupRow, column.Value);
                string type = sheet.Text(typeRow, column.Value);
                result.DataTypesSeen.Add(type.Length == 0 ? "(blank)" : type);
                result.ParameterGroupsSeen.Add(group.Length == 0 ? "(blank)" : group);

                var meta = new ParamMetaData(column.Key, group, type);
                meta.Target = ParamMetaData.ParamTarget.Material;

                // The old format carries material type in a dedicated per-column row. Here the
                // Parametergruppe row does that job: a column in group "Beton" belongs to
                // concrete materials, "Metall" to metals, and so on.
                meta.MaterialType = group;

                if (idRow > 0) meta.Description = sheet.Text(idRow, column.Value);

                result.MaterialParameterDefinitions[column.Key] = meta;
            }

            int firstDataRow = sheet.FirstDataRow();
            if (firstDataRow <= 0)
            {
                result.Problems.Add(PlanungsdatenbankLayout.Sheets.Materials + " has no data rows below its labels.");
                return;
            }

            int rowsRead = 0, patternsSeen = 0;
            for (int row = firstDataRow; row <= sheet.LastRow; row++)
            {
                string materialId = sheet.Text(row, idColumn);
                if (materialId.Length == 0) continue;
                if (PlanungsdatenbankLayout.IsSkipped(materialId)) continue;

                if (result.MaterialData.ContainsKey(materialId))
                {
                    result.Problems.Add("Row " + row + ": " + PlanungsdatenbankLayout.Headers.MaterialId +
                        " \"" + materialId + "\" appears more than once. Only the first was kept.");
                    continue;
                }

                var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (KeyValuePair<string, int> column in columns)
                {
                    if (column.Value == idColumn) continue;

                    string raw = sheet.Text(row, column.Value);
                    if (PlanungsdatenbankLayout.IsSkipped(raw)) continue;
                    values[column.Key] = raw;

                    if (string.Equals(column.Key, PlanungsdatenbankLayout.Headers.SurfacePattern, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(column.Key, PlanungsdatenbankLayout.Headers.CutPattern, StringComparison.OrdinalIgnoreCase))
                    {
                        patternsSeen++;
                    }
                }

                result.MaterialData[materialId] = values;
                rowsRead++;
            }

            result.Notes.Add(PlanungsdatenbankLayout.Sheets.Materials + ": " + rowsRead + " materials, " +
                result.MaterialParameterDefinitions.Count + " parameters.");

            // Patterns are read by label from the start, so this is reporting, not a gate. When
            // the office fills those columns in, the values simply start arriving.
            result.Notes.Add("Fill patterns (" + PlanungsdatenbankLayout.Headers.SurfacePattern + " / " +
                PlanungsdatenbankLayout.Headers.CutPattern + "): " + patternsSeen +
                " values present across " + rowsRead + " materials.");
        }
    }
}
