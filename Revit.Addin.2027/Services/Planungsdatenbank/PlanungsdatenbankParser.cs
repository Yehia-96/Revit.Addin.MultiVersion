// Shared source: keep identical in Revit.Addin.2024 / .2026 / .2027.
// C# 7.3 / net48 floor - see PlanungsdatenbankLayout.cs for the full restriction list.
using System;
using System.Collections.Generic;
using System.IO;
using ClosedXML.Excel;

namespace Revit.Addin._2027.Services.Planungsdatenbank
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
        /// Family values: key is the row's Bauteil_ID (see <see cref="ElementKey"/>), value is
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

        /// <summary>
        /// One entry per material to create in Revit, in sheet order. Plain records rather than
        /// <c>MaterialsDto</c>, whose constructor throws on bad input - converting happens when
        /// the result is loaded, where a bad row can be reported instead of aborting the read.
        /// </summary>
        internal List<PlanungsdatenbankMaterial> Materials { get; private set; }

        /// <summary>Materialname for each Material-ID - the join between the two sheets.</summary>
        internal Dictionary<string, string> MaterialNamesById { get; private set; }

        /// <summary>
        /// Which material each Bauteil gets: Bauteil_ID
        /// to Materialname. Resolved from the Bauteil's Material-ID, because Revit materials are
        /// named by Materialname - the same convention the old format followed.
        /// </summary>
        internal Dictionary<string, string> ElementMaterial { get; private set; }

        internal PlanungsdatenbankParseResult()
        {
            Materials = new List<PlanungsdatenbankMaterial>();
            MaterialNamesById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            ElementMaterial = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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

    /// <summary>A material as the workbook describes it, before it becomes a Revit material.</summary>
    internal sealed class PlanungsdatenbankMaterial
    {
        internal string MaterialId;
        internal string Name;

        /// <summary>
        /// The Materialgruppe - Beton, Metall, Kunststoff, Boden, Erdbau, Sonstiges. Material
        /// creation maps exactly these words to a Revit material class already.
        /// </summary>
        internal string Type;

        /// <summary>Never null: empty when the workbook has no pattern yet.</summary>
        internal string SurfacePattern = string.Empty;

        /// <summary>Never null: empty when the workbook has no pattern yet.</summary>
        internal string CutPattern = string.Empty;
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

            // Families are read before materials but refer to them by Material-ID, so the IDs are
            // collected here and resolved to names once both sheets are in.
            var materialIdByFamilyKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            using (var workbook = new XLWorkbook(path))
            {
                ReadObjects(workbook, result, materialIdByFamilyKey);
                ReadMaterials(workbook, result);
            }

            ResolveElementMaterials(materialIdByFamilyKey, result);

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

        /// <summary>True when any parameter column of the row holds something.</summary>
        private static bool RowHasAnyValue(SheetGrid sheet, int row, List<Column> columns)
        {
            foreach (Column column in columns)
            {
                if (sheet.Text(row, column.Index).Length > 0) return true;
            }
            return false;
        }

        /// <summary>As <see cref="FindColumn"/>, for a column the read can do without: 0 when absent.</summary>
        private static int TryFindColumn(SheetGrid sheet, int headerRow, string header)
        {
            foreach (KeyValuePair<string, int> candidate in sheet.HeaderColumns(headerRow))
            {
                if (string.Equals(candidate.Key, header, StringComparison.OrdinalIgnoreCase))
                    return candidate.Value;
            }
            return 0;
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

        private static void ReadObjects(
            XLWorkbook workbook,
            PlanungsdatenbankParseResult result,
            Dictionary<string, string> materialIdByFamilyKey)
        {
            SheetGrid sheet = SheetGrid.Open(workbook, PlanungsdatenbankLayout.Sheets.Objects);
            int nameRow = sheet.RequireLabelRow(PlanungsdatenbankLayout.Labels.ParameterName);

            // Bauteil_ID is the connection: the modellers enter the same value on the element in
            // Revit, and that is what the element is matched by. See ElementKey.
            int idColumn = FindColumn(sheet, nameRow, PlanungsdatenbankLayout.Headers.BauteilId);

            // Optional: without it, families still read fine and simply get no material assigned.
            int materialIdColumn = TryFindColumn(sheet, nameRow, PlanungsdatenbankLayout.Headers.MaterialId);

            // Identity columns are not parameters to push into Revit. Bauteil_ID is typed in by
            // hand and must not be overwritten; Bauteilnummer and Bauteilunternummer stay out as
            // they always were - families carry Bauteilnummer as their own integer parameter.
            var identity = new HashSet<int>();
            identity.Add(idColumn);
            int bnColumn = TryFindColumn(sheet, nameRow, PlanungsdatenbankLayout.Headers.Bauteilnummer);
            int bunColumn = TryFindColumn(sheet, nameRow, PlanungsdatenbankLayout.Headers.Bauteilunternummer);
            if (bnColumn > 0) identity.Add(bnColumn);
            if (bunColumn > 0) identity.Add(bunColumn);

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
                string idText = sheet.Text(row, idColumn);
                if (idText.Length == 0)
                {
                    // A row with nothing in it is just the end of the data. A row with values
                    // but no Bauteil_ID cannot be connected to anything - say so.
                    if (RowHasAnyValue(sheet, row, columns))
                    {
                        rowsSkipped++;
                        result.Problems.Add("Row " + row + ": no " + PlanungsdatenbankLayout.Headers.BauteilId +
                            ", so it cannot be matched to an element. Row skipped.");
                    }
                    continue;
                }

                string key = PlanungsdatenbankLayout.IsSkipped(idText)
                    ? null
                    : ElementKey.Normalise(idText, ExcelFormat.Planungsdatenbank);
                if (key == null)
                {
                    rowsSkipped++;
                    result.Problems.Add("Row " + row + ": " + PlanungsdatenbankLayout.Headers.BauteilId +
                        " is \"" + idText + "\", which is a placeholder, not an ID. Row skipped.");
                    continue;
                }

                if (result.FamilyData.ContainsKey(key))
                {
                    rowsSkipped++;
                    result.Problems.Add("Row " + row + ": " + PlanungsdatenbankLayout.Headers.BauteilId +
                        " " + key + " appears more than once. Only the first was kept.");
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

                // [ET] here means an Einbauteil, which has no material - skipped like any marker.
                if (materialIdColumn > 0)
                {
                    string materialId = sheet.Text(row, materialIdColumn);
                    if (!PlanungsdatenbankLayout.IsSkipped(materialId))
                        materialIdByFamilyKey[key] = materialId;
                }
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

                // The material to create in Revit. It is named by Materialname, as the old format
                // named materials from its name column; Material-ID stays the join key between
                // the sheets. Rows without a name or group cannot become a material and are
                // reported rather than created half-specified.
                string name = Value(values, PlanungsdatenbankLayout.Headers.MaterialName);
                string type = Value(values, PlanungsdatenbankLayout.Headers.MaterialGroup);
                if (name.Length == 0)
                {
                    result.Problems.Add("Row " + row + ": material \"" + materialId +
                        "\" has no " + PlanungsdatenbankLayout.Headers.MaterialName + ", so it cannot be created in Revit.");
                    continue;
                }

                string existingId;
                if (NameOwner(result.MaterialNamesById, name, out existingId))
                {
                    result.Problems.Add("Row " + row + ": materials \"" + existingId + "\" and \"" + materialId +
                        "\" share the name \"" + name + "\". Revit materials are named by Materialname, so only the first was kept.");
                    continue;
                }

                result.MaterialNamesById[materialId] = name;
                if (type.Length == 0)
                {
                    // Matches the old reader, which only created materials it had a type for.
                    result.Notes.Add("Material \"" + name + "\" has no " + PlanungsdatenbankLayout.Headers.MaterialGroup +
                        "; its parameters are read, but it is not created as a Revit material.");
                    continue;
                }

                result.Materials.Add(new PlanungsdatenbankMaterial
                {
                    MaterialId = materialId,
                    Name = name,
                    Type = type,
                    // Never null - MaterialsDto calls Trim() on these unguarded.
                    SurfacePattern = Value(values, PlanungsdatenbankLayout.Headers.SurfacePattern),
                    CutPattern = Value(values, PlanungsdatenbankLayout.Headers.CutPattern)
                });
            }

            result.Notes.Add(PlanungsdatenbankLayout.Sheets.Materials + ": " + rowsRead + " materials, " +
                result.MaterialParameterDefinitions.Count + " parameters, " + result.Materials.Count + " to create in Revit.");

            // Patterns are read by label from the start, so this is reporting, not a gate. When
            // the office fills those columns in, the values simply start arriving.
            result.Notes.Add("Fill patterns (" + PlanungsdatenbankLayout.Headers.SurfacePattern + " / " +
                PlanungsdatenbankLayout.Headers.CutPattern + "): " + patternsSeen +
                " values present across " + rowsRead + " materials.");
        }

        /// <summary>
        /// Turns each Bauteil's Material-ID into the Materialname its Revit material carries.
        /// An ID with no matching material is reported - it means the two sheets disagree, and
        /// assigning nothing silently would hide that.
        /// </summary>
        private static void ResolveElementMaterials(
            Dictionary<string, string> materialIdByFamilyKey,
            PlanungsdatenbankParseResult result)
        {
            int resolved = 0;
            foreach (KeyValuePair<string, string> entry in materialIdByFamilyKey)
            {
                string name;
                if (result.MaterialNamesById.TryGetValue(entry.Value, out name))
                {
                    result.ElementMaterial[entry.Key] = name;
                    resolved++;
                }
                else
                {
                    result.Problems.Add("Bauteil " + entry.Key + " refers to " + PlanungsdatenbankLayout.Headers.MaterialId +
                        " \"" + entry.Value + "\", which is not in " + PlanungsdatenbankLayout.Sheets.Materials + ".");
                }
            }
            if (materialIdByFamilyKey.Count > 0)
                result.Notes.Add("Material assignment: " + resolved + " of " + materialIdByFamilyKey.Count + " Bauteile resolved to a material.");
        }

        private static string Value(Dictionary<string, string> values, string key)
        {
            string v;
            return values.TryGetValue(key, out v) && v != null ? v.Trim() : string.Empty;
        }

        /// <summary>True when another Material-ID already owns this name; returns that ID.</summary>
        private static bool NameOwner(Dictionary<string, string> namesById, string name, out string ownerId)
        {
            foreach (KeyValuePair<string, string> entry in namesById)
            {
                if (string.Equals(entry.Value, name, StringComparison.OrdinalIgnoreCase))
                {
                    ownerId = entry.Key;
                    return true;
                }
            }
            ownerId = null;
            return false;
        }
    }
}
