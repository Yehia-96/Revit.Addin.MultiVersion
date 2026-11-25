using ClosedXML.Excel;
using MH.RevitTools.Database.Comparing;
using Revit.Addin._2027.Services;
using Revit.Addin._2027.Services.Planungsdatenbank;
using System;
using System.Collections.Generic;
using System.IO;

namespace Revit.Addin._2027.DatabaseLink.Compare
{
    /// <summary>
    /// A Planungsdatenbank workbook as the Excel import reads it, in the shape that's compared with the database.
    /// The Excel reader itself does the reading (<see cref="PlanungsdatenbankParser"/>); what's loaded for the Excel
    /// import stays as it is. The workbook's label rows give each parameter its dictionary key.
    /// </summary>
    internal static class ExcelLoad
    {
        public const string Source = "Excel";

        /// <param name="convert">Puts a value into Revit's form.</param>
        /// <param name="kindOf">What Revit makes of a data type with a unit, e.g. Length.</param>
        public static LoadedProject Read(string path, ConvertLoaded convert, Func<string, string, string> kindOf)
        {
            // From a copy, so the workbook may stay open in Excel.
            string copy = Path.Combine(Path.GetTempPath(), "MH.RevitTools." + Guid.NewGuid().ToString("N") + ".xlsx");
            try
            {
                using (var from = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (FileStream to = File.Create(copy))
                    from.CopyTo(to);

                PlanungsdatenbankParseResult result = PlanungsdatenbankParser.Read(copy);
                using (var workbook = new XLWorkbook(copy))
                {
                    return Put(result, ColumnsOf(workbook, PlanungsdatenbankLayout.Sheets.Objects),
                        ColumnsOf(workbook, PlanungsdatenbankLayout.Sheets.Materials), convert, kindOf);
                }
            }
            finally
            {
                Delete(copy);
            }
        }

        private static LoadedProject Put(
            PlanungsdatenbankParseResult result, WorkbookColumns objectColumns, WorkbookColumns materialColumns,
            ConvertLoaded convert, Func<string, string, string> kindOf)
        {
            var project = new LoadedProjectBuilder(Source);
            Dictionary<string, string> objectKeys = Parameters(project, result.ParameterDefinitions, objectColumns, kindOf);
            Dictionary<string, string> materialKeys = Parameters(project, result.MaterialParameterDefinitions, materialColumns, kindOf);

            foreach (KeyValuePair<string, Dictionary<string, string>> row in result.FamilyData)
            {
                project.Object(row.Key);
                foreach (KeyValuePair<string, string> value in row.Value)
                    project.ObjectValue(row.Key, objectKeys[value.Key], Convert(convert, result.ParameterDefinitions[value.Key], value.Value));
            }
            // The Materialname it makes of the row's Material-ID; it remarks on an ID it doesn't know.
            foreach (KeyValuePair<string, string> madeOf in result.ElementMaterial)
                project.ObjectMaterial(madeOf.Key, madeOf.Value);
            foreach (KeyValuePair<string, Dictionary<string, string>> row in result.MaterialData)
            {
                // The columns the reader makes the material of. It remarks on those it can't make one of.
                project.Material(row.Key, Cell(row.Value, PlanungsdatenbankLayout.Headers.MaterialName),
                    Cell(row.Value, PlanungsdatenbankLayout.Headers.MaterialGroup),
                    Cell(row.Value, PlanungsdatenbankLayout.Headers.SurfacePattern),
                    Cell(row.Value, PlanungsdatenbankLayout.Headers.CutPattern));
                foreach (KeyValuePair<string, string> value in row.Value)
                    project.MaterialValue(row.Key, materialKeys[value.Key], Convert(convert, result.MaterialParameterDefinitions[value.Key], value.Value));
            }
            foreach (string problem in result.Problems)
                project.Remark(problem);
            return project.Build();
        }

        private static string Cell(Dictionary<string, string> values, string header)
        {
            string value;
            return values.TryGetValue(header, out value) ? value : "";
        }

        // Adds the parameters, and gives each one's key by the name the Excel reader uses.
        private static Dictionary<string, string> Parameters(
            LoadedProjectBuilder project, Dictionary<string, ParamMetaData> definitions, WorkbookColumns columns, Func<string, string, string> kindOf)
        {
            var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, ParamMetaData> definition in definitions)
            {
                // The Excel reader keeps the Parameter-ID as the description.
                string key = columns.KeyOf(definition.Value.Description, definition.Key);
                byName.Add(definition.Key, key);
                project.Parameter(new LoadedParameter(key, definition.Key, kindOf(definition.Value.Datatype, definition.Value.Unit)));
            }
            return byName;
        }

        private static LoadedValue Convert(ConvertLoaded convert, ParamMetaData definition, string raw) =>
            convert(definition.Datatype, definition.Unit, raw);

        private static WorkbookColumns ColumnsOf(XLWorkbook workbook, string sheetName)
        {
            var columns = new WorkbookColumns();
            if (!SheetGrid.HasSheet(workbook, sheetName))
                return columns;

            SheetGrid sheet = SheetGrid.Open(workbook, sheetName);
            int nameRow = sheet.RequireLabelRow(PlanungsdatenbankLayout.Labels.ParameterName);
            int idRow = sheet.RequireLabelRow(PlanungsdatenbankLayout.Labels.ParameterId, PlanungsdatenbankLayout.Labels.ParameterIdTypo);
            int setRow = sheet.RequireLabelRow(PlanungsdatenbankLayout.Labels.PsetId);
            int groupRow = sheet.RequireLabelRow(PlanungsdatenbankLayout.Labels.ParameterGroupId);
            foreach (KeyValuePair<string, int> header in sheet.HeaderColumns(nameRow))
                columns.Add(sheet.Text(idRow, header.Value), sheet.Text(setRow, header.Value), sheet.Text(groupRow, header.Value), header.Key);
            return columns;
        }

        // A copy that can't be deleted only stays in the temp folder.
        private static void Delete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
