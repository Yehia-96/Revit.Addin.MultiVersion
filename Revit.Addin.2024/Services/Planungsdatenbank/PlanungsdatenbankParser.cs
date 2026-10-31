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
        /// Qualifies a column name with the Parameterset it belongs to, for the columns whose
        /// short name is not unique within a sheet.
        /// <para>
        /// The workbook reuses short names across sets: Objektdatenbank has a
        /// <c>Beschreibung</c> in both <c>Bauteilinformation</c> and <c>Produktinformation</c>,
        /// and Materialdatenbank has two <c>Kategorie</c> columns and two <c>Bezeichnung</c>
        /// columns. Revit identifies a shared parameter by name alone, so those cannot both
        /// exist unqualified.
        /// </para>
        /// <para>
        /// Both sides of a clash are qualified, not just the second one - picking a winner by
        /// column order would be arbitrary, and whichever lost would silently disappear.
        /// </para>
        /// </summary>
        internal static string QualifiedName(string columnName, string parameterSet)
        {
            if (string.IsNullOrWhiteSpace(parameterSet)) return columnName;
            return columnName + " (" + parameterSet.Trim() + ")";
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

        // --- shared column handling -----------------------------------------------------------

        /// <summary>One parameter column: where it is, and the name Revit will see.</summary>
        private sealed class Column
        {
            internal string Name;
            internal int Index;
        }

        /// <summary>
        /// Column number of a required header, by its exact text in the header row.
        /// </summary>
        /// <exception cref="InvalidDataException">
        /// The column is absent, which means the sheet is not the shape we expect - better to
        /// say so than to read a sheet that cannot be keyed.
        /// </exception>
        private static int FindColumn(SheetGrid sheet, int headerRow, string header)
        {
            foreach (KeyValuePair<string, int> candidate in sheet.HeaderColumns(headerRow))
            {
                if (string.Equals(candidate.Key, header, StringComparison.OrdinalIgnoreCase))
                    return candidate.Value;
            }
            throw new InvalidDataException("Sheet \"" + sheet.SheetName + "\" has no \"" + header + "\" column.");
        }

        /// <summary>
        /// Turns a sheet's header row into the parameter columns to read, with names made unique,
        /// and registers a <see cref="ParamMetaData"/> for each.
        /// <para>
        /// Both sheets share one layout, so this serves both; only the identity columns and the
        /// parameter target differ.
        /// </para>
        /// </summary>
        private static List<Column> PrepareColumns(
            SheetGrid sheet,
            string sheetName,
            HashSet<int> identityColumns,
            ParamMetaData.ParamTarget target,
            Dictionary<string, ParamMetaData> definitions,
            PlanungsdatenbankParseResult result)
        {
            int nameRow = sheet.RequireLabelRow(PlanungsdatenbankLayout.Labels.ParameterName);
            int groupRow = sheet.RequireLabelRow(PlanungsdatenbankLayout.Labels.ParameterGroup);
            int typeRow = sheet.RequireLabelRow(PlanungsdatenbankLayout.Labels.DataType);
            int setRow = sheet.LabelRow(PlanungsdatenbankLayout.Labels.ParameterSet);
            int unitRow = sheet.LabelRow(PlanungsdatenbankLayout.Labels.Unit);
            int idRow = sheet.LabelRow(
                PlanungsdatenbankLayout.Labels.ParameterId,
                PlanungsdatenbankLayout.Labels.ParameterIdTypo);

            List<KeyValuePair<string, int>> headers = sheet.HeaderColumns(nameRow);
            HashSet<string> duplicates = SheetGrid.DuplicateHeaders(headers);

            var prepared = new List<Column>();
            var unknownTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var unknownGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (KeyValuePair<string, int> header in headers)
            {
                if (identityColumns.Contains(header.Value)) continue;

                string parameterSet = setRow > 0 ? sheet.Text(setRow, header.Value) : string.Empty;
                string group = sheet.Text(groupRow, header.Value);

                // A name reused across Parametersets is qualified on both sides, so neither is
                // dropped and neither silently wins.
                string name = duplicates.Contains(header.Key)
                    ? QualifiedName(header.Key, parameterSet)
                    : header.Key;

                // Parameterset is not always enough: Materialdatenbank has two "Kategorie"
                // columns both in the "Material" set, told apart only by their Parametergruppe
                // (Kunststoff and Boden). Fall back to including the group for those.
                if (definitions.ContainsKey(name) && group.Length > 0)
                {
                    name = QualifiedName(header.Key, parameterSet + " / " + group.Trim());
                }

                if (definitions.ContainsKey(name))
                {
                    result.Problems.Add(sheetName + ": two columns both resolve to the parameter name \"" +
                        name + "\", so the later one was skipped. They share a Parameterset and a " +
                        "Parametergruppe, so only renaming one in the workbook can separate them.");
                    continue;
                }

                string type = sheet.Text(typeRow, header.Value);
                string unit = unitRow > 0 ? sheet.Text(unitRow, header.Value) : string.Empty;

                result.DataTypesSeen.Add(type.Length == 0 ? "(blank)" : type);
                result.ParameterGroupsSeen.Add(group.Length == 0 ? "(blank)" : group);
                if (!ParamMetaData.IsKnownDatatype(type)) unknownTypes.Add(type.Length == 0 ? "(blank)" : type);
                if (!ParamMetaData.IsKnownGroup(group)) unknownGroups.Add(group.Length == 0 ? "(blank)" : group);

                var meta = new ParamMetaData(name, group, type);
                meta.Target = target;
                meta.Unit = unit;

                // Revit's parameter Description. The old format carries a prefix row for this;
                // this workbook has none, so the stable Parameter-ID is used instead - a real
                // identifier rather than the random placeholder the old reader falls back to.
                if (idRow > 0) meta.Description = sheet.Text(idRow, header.Value);

                // Material family, e.g. Beton or Metall. The old format states this in a
                // dedicated per-column row; here the Parametergruppe row does the same job.
                if (target == ParamMetaData.ParamTarget.Material) meta.MaterialType = group;

                definitions[name] = meta;
                prepared.Add(new Column { Name = name, Index = header.Value });
            }

            if (duplicates.Count > 0)
            {
                var list = new List<string>(duplicates);
                result.Notes.Add(sheetName + ": qualified " + duplicates.Count +
                    " reused column name(s) with their Parameterset - " + string.Join(", ", list.ToArray()) + ".");
            }

            // An unmapped datatype is the dangerous one: the parameter would be created as text
            // and the run would still report success.
            if (unknownTypes.Count > 0)
            {
                var list = new List<string>(unknownTypes);
                result.Problems.Add(sheetName + ": datatype(s) not recognised and treated as text - " +
                    string.Join(", ", list.ToArray()) + ".");
            }
            if (unknownGroups.Count > 0)
            {
                var list = new List<string>(unknownGroups);
                result.Notes.Add(sheetName + ": parameter group(s) not recognised, placed under General - " +
                    string.Join(", ", list.ToArray()) + ".");
            }

            return prepared;
        }

        // --- Objektdatenbank ------------------------------------------------------------------

        private static void ReadObjects(XLWorkbook workbook, PlanungsdatenbankParseResult result)
        {
            SheetGrid sheet = SheetGrid.Open(workbook, PlanungsdatenbankLayout.Sheets.Objects);
            int nameRow = sheet.RequireLabelRow(PlanungsdatenbankLayout.Labels.ParameterName);

            int bnColumn = FindColumn(sheet, nameRow, PlanungsdatenbankLayout.Headers.Bauteilnummer);
            int bunColumn = FindColumn(sheet, nameRow, PlanungsdatenbankLayout.Headers.Bauteilunternummer);

            // The key columns identify a row; they are not parameters to push into Revit.
            var identity = new HashSet<int>();
            identity.Add(bnColumn);
            identity.Add(bunColumn);

            List<Column> columns = PrepareColumns(
                sheet, PlanungsdatenbankLayout.Sheets.Objects, identity,
                ParamMetaData.ParamTarget.FamilyElement, result.ParameterDefinitions, result);

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
                foreach (Column column in columns)
                {
                    string raw = sheet.Text(row, column.Index);
                    // Absent rather than blank: a skipped cell must never be written over
                    // whatever the modeller already put in Revit.
                    if (PlanungsdatenbankLayout.IsSkipped(raw)) continue;
                    values[column.Name] = raw;
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
            int idColumn = FindColumn(sheet, nameRow, PlanungsdatenbankLayout.Headers.MaterialId);

            var identity = new HashSet<int>();
            identity.Add(idColumn);

            List<Column> columns = PrepareColumns(
                sheet, PlanungsdatenbankLayout.Sheets.Materials, identity,
                ParamMetaData.ParamTarget.Material, result.MaterialParameterDefinitions, result);

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
                foreach (Column column in columns)
                {
                    string raw = sheet.Text(row, column.Index);
                    if (PlanungsdatenbankLayout.IsSkipped(raw)) continue;
                    values[column.Name] = raw;

                    if (string.Equals(column.Name, PlanungsdatenbankLayout.Headers.SurfacePattern, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(column.Name, PlanungsdatenbankLayout.Headers.CutPattern, StringComparison.OrdinalIgnoreCase))
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
