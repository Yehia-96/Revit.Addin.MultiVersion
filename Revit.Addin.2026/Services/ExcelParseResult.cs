// File: ExcelParser.cs (split from ExcelParseResult.cs)
using Autodesk.Revit.UI;
using ClosedXML.Excel;
using System;
using System.Collections.Generic;
using Revit.Addin._2026.Dtos;
using System.IO;
using System.Linq;
using ExcelPackage = OfficeOpenXml.ExcelPackage;
using static Revit.Addin._2026.Services.ExcelLayout;

namespace Revit.Addin._2026.Services
{
    // The main class for parsing Excel files to extract parameter definitions and values.
    public static class ExcelParser
    {
        // Static properties to hold parameter definitions and family data
        public static Dictionary<string, ParamMetaData> ParameterDefinitions { get; private set; } = new Dictionary<string, ParamMetaData>();
        public static Dictionary<string, ParamMetaData> MaterialParameterDefinitions { get; private set; } = new Dictionary<string, ParamMetaData>();

        public static Dictionary<string, Dictionary<string, string>> FamilyData { get; private set; } = new Dictionary<string, Dictionary<string, string>>();
        public static Dictionary<string, Dictionary<string, string>> MaterialData { get; private set; } = new Dictionary<string, Dictionary<string, string>>();

        // Static property to hold previous family data for change detection
        private static Dictionary<string, Dictionary<string, string>> _previousFamilyData = new Dictionary<string, Dictionary<string, string>>();

        public static HashSet<string> ParametersToIgnore = new HashSet<string>();
        

        // Helper method to fill in previous data and call the main reading method 
        // To fill in the FamilyData dictionary with the new data
        public static void UpdateData(string filePath)
        {
            _previousFamilyData = FamilyData.ToDictionary(
                kvp => kvp.Key,
                kvp => new Dictionary<string, string>(kvp.Value));

            FamilyData.Clear();
            ReadExcelStructured(filePath);
        }
        // Method to get changes between the previous and current family data
        public static Dictionary<string, Dictionary<string, string>> GetChanges()
        {
            var changes = new Dictionary<string, Dictionary<string, string>>();

            foreach (string bn in FamilyData.Keys)
            {
                if (!_previousFamilyData.TryGetValue(bn, out var oldValues))
                    continue;

                var newValues = FamilyData[bn];

                foreach (string param in newValues.Keys)
                {
                    if (!oldValues.TryGetValue(param, out var oldVal))
                        continue;

                    string newVal = newValues[param];
                    // Normalize values to handle case insensitivity and whitespace
                    // Checks if the new value is different from the old value
                    if (!string.Equals(newVal, oldVal, StringComparison.OrdinalIgnoreCase))
                    {
                        if (!changes.ContainsKey(bn))
                            changes[bn] = new Dictionary<string, string>();
                        // Store the change in the dictionary
                        changes[bn][param] = newVal;
                    }
                }
            }

            return changes;
        }
        // Method to clear the static dictionaries to reset the state
        public static void Clear()
        {
            ParameterDefinitions.Clear();
            FamilyData.Clear();
        }
        // Main method to read the structured Excel file and populate the dictionaries
        public static void ReadExcelStructured(string filePath)
        {
            //Set the license for EPPlus to use non-commercial personal license
            ExcelPackage.License.SetNonCommercialPersonal("Yehia");

            using (ExcelPackage pkg = new ExcelPackage(new FileInfo(filePath)))
            {
                // Fetch the first worksheet from the Excel file
                var ws = pkg.Workbook.Worksheets[0];
                // Define variables to hold the starting column and row indices
                // and the specific rows for headers, groups, types, and data
                int colStart = 5, rowHeader = 7, rowGroup = 8, rowType = 9, rowData = 10;
                // Variables to hold the number of rows and columns in the worksheet
                int colCount = ws.Dimension.Columns;
                int rowCount = ws.Dimension.Rows;
                // Loop through the columns starting from the specified column
                for (int col = colStart; col <= colCount; col++)
                {
                    // Fix the col index and read in the parameter name, group name, and data type
                    // Using the row indices defined above
                    string paramName = ws.Cells[rowHeader, col].Text?.Trim();

                    // Skip the first column as it contains Bauteilnummer
                    if (string.IsNullOrEmpty(paramName) || paramName.Equals("Bauteilnummer", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string groupName = ws.Cells[rowGroup, col].Text?.Trim();
                    string dataType = ws.Cells[rowType, col].Text?.Trim();
                    // Build the ParameterDefinitions dictionary with creating new ParamMetaData objects
                    ParameterDefinitions[paramName] = new ParamMetaData(paramName, groupName, dataType);
                }
                // This loop iterates through the rows to populate the familyData dictionary 
                // With the values for each parameter defined above
                for (int row = rowData; row <= rowCount; row++)
                {
                    // The Bauteilnummer is expected to be in the first column of each row
                    string bn = ws.Cells[row, colStart].Text?.Trim();
                    if (string.IsNullOrEmpty(bn)) continue;

                    var paramValues = new Dictionary<string, string>();
                    // Fix the row index and iterate through the columns 
                    // Fetching the parameter values for each Bauteilnummer
                    for (int col = colStart + 1; col <= colCount; col++)
                    {
                        // The dictionary is populated by using the parameter name as the key
                        // and the value from the cell as the value, removes commas from the parameter name
                        string paramName = ws.Cells[rowHeader, col].Text?.Trim().Replace(",","");
                        if (string.IsNullOrEmpty(paramName) || !ParameterDefinitions.ContainsKey(paramName))
                            continue;

                        string value = ws.Cells[row, col].Text?.Trim();
                        // This conidition checks if the value is supposed to be set inside the model itself
                        if (string.Equals(value, "aus Modell", StringComparison.OrdinalIgnoreCase))
                            paramValues[paramName] = "";
                        // This condition checks if the parameter should be set for this specific bauteilnummer
                        // The dashed value indicates that the parameter should not be set for this bauteilnummer
                        // consquently, it is not added and skipped
                        else if (!string.IsNullOrWhiteSpace(value) && value != "-")
                            paramValues[paramName] = value;
                    }
                    // Adds the dictionary to the FamilyData which is a dictionary of dictionaries 
                    // The key is the Bauteilnummer and the value is the dictionary of parameter names and values
                    if (paramValues.Count > 0)
                        FamilyData[bn] = paramValues;
                }
            }
        }

        public static void NewReadExcelStructured(string ExcelPath)
        {
            ParameterDefinitions.Clear();
            MaterialParameterDefinitions.Clear();
            FamilyData.Clear();
            MaterialData.Clear();
            ParametersToIgnore.Clear();
            MaterialCreation.MaterialsList.Clear();

            using (var workbook = new XLWorkbook(ExcelPath))
            {
                var ws = workbook.Worksheet(2);
                var wsMaterial = workbook.Worksheet(3);

                // Row/column positions live in ExcelLayout (Columns / FamilyRows / MaterialRows).

                // ---------- FAMILY PARAM DEFINITIONS ----------
                int lastColFamily = ws.Row(FamilyRows.Header).LastCellUsed()?.Address.ColumnNumber ?? 0;

                for (int col = Columns.FamilyStart; col <= lastColFamily; col++)
                {
                    string paramName = ws.Cell(FamilyRows.Header, col).GetString().Trim();
                    if (string.IsNullOrEmpty(paramName))
                        continue;

                    if(ws.Cell(FamilyRows.Ignore, col).GetString().Trim().Equals("Modeller", StringComparison.OrdinalIgnoreCase)
                        || ws.Cell(FamilyRows.Ignore, col).GetString().Trim().Equals("SBIM", StringComparison.OrdinalIgnoreCase))
                    {
                        ParametersToIgnore.Add(paramName);

                    }

                    if (string.Equals(paramName, "Bauteilnummer", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string paramGroup = ws.Cell(FamilyRows.Group, col).GetString().Trim();
                    if (string.Equals(paramGroup, "Material", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string paramDataType = ws.Cell(FamilyRows.DataType, col).GetString().Trim();

                    // Prefix row -> Revit parameter description. Blank cells get a random
                    // placeholder prefix (testing only) so every parameter has a visible value.
                    string paramPrefix = ws.Cell(FamilyRows.Prefix, col).GetString().Trim();
                    if (string.IsNullOrWhiteSpace(paramPrefix))
                        paramPrefix = GenerateRandomPrefix();

                    var meta = new ParamMetaData(paramName, paramGroup, paramDataType)
                    {
                        Target = ParamMetaData.ParamTarget.FamilyElement,
                        Description = paramPrefix
                    };

                    ParameterDefinitions[paramName] = meta;
                }

                // ---------- MATERIAL PARAM DEFINITIONS ----------
                int lastColMaterial = wsMaterial.Row(MaterialRows.Header).LastCellUsed()?.Address.ColumnNumber ?? 0;

                for (int col = Columns.MaterialStart; col <= lastColMaterial; col++)
                {
                    string paramName = wsMaterial.Cell(MaterialRows.Header, col).GetString().Trim();
                    if (string.IsNullOrEmpty(paramName))
                        continue;

                    if (string.Equals(paramName, "Materialname", StringComparison.OrdinalIgnoreCase))
                        continue;


                    string paramGroup = wsMaterial.Cell(MaterialRows.Group, col).GetString().Trim();
                    string paramDataType = wsMaterial.Cell(MaterialRows.DataType, col).GetString().Trim();
                    string paramMaterialType = wsMaterial.Cell(MaterialRows.MaterialType, col).GetString().Trim();

                    var meta = new ParamMetaData(paramName, paramGroup, paramDataType)
                    {
                        Target = ParamMetaData.ParamTarget.Material,
                        MaterialType = paramMaterialType
                    };

                    MaterialParameterDefinitions[paramName] = meta;
                }
                


                // ---------- MATERIAL DATA ----------
                foreach (var row in wsMaterial.RowsUsed().Where(r => r.RowNumber() >= MaterialRows.DataStart))
                {
                    string materialName = row.Cell(Columns.MaterialName).GetString().Trim(); // material name column (current assumption)
                    string materialType = row.Cell(Columns.MaterialType).GetString().Trim(); // material type column (current assumption)
                    string materialSurfacePattern = row.Cell(Columns.SurfacePattern).GetString().Trim();
                    string materialCutPattern = row.Cell(Columns.CutPattern).GetString().Trim();

                    if (string.IsNullOrEmpty(materialName))
                        continue;

                    if (!string.IsNullOrEmpty(materialType))
                    {
                        MaterialsDto material = new MaterialsDto(
                            materialName: materialName,
                            materialType: materialType,
                            cutPattern: materialCutPattern,
                            surfacePattern: materialSurfacePattern);
                        MaterialCreation.MaterialsList.Add(material);
                    }
                    var values = new Dictionary<string, string>();

                    for (int col = Columns.MaterialStart; col <= lastColMaterial; col++)
                    {
                        string header = wsMaterial.Cell(MaterialRows.Header, col).GetString().Trim();
                        if (string.IsNullOrEmpty(header))
                            continue;

                        if (!MaterialParameterDefinitions.TryGetValue(header.Replace(",", ""), out var meta))
                            continue;
              
                       
                        string v = row.Cell(col).GetString().Trim();

                
                        // store only meaningful values (same style as family)
                        if (!string.IsNullOrWhiteSpace(v) && v != "-")
                            values[meta.Name] = v;
                    }

                    if (values.Count > 0)
                        MaterialData[materialName] = values;
                }

                // ---------- FAMILY DATA ----------
                foreach (var row in ws.RowsUsed().Where(r => r.RowNumber() >= FamilyRows.DataStart))
                {
                    string bn = row.Cell(Columns.Bauteilnummer).GetString().Trim();
                    if (string.IsNullOrEmpty(bn))
                        continue;

                    var values = new Dictionary<string, string>();

                    for (int col = Columns.FamilyStart; col <= lastColFamily; col++)
                    {
                        string header = ws.Cell(FamilyRows.Header, col).GetString().Trim();
                        if (string.IsNullOrEmpty(header))
                            continue;

                        if(string.Equals(header, "Materialname", StringComparison.OrdinalIgnoreCase))
                        {
                            MaterialAssignment.ElementMaterial[bn] = row.Cell(col).GetString().Trim();
                            continue;
                        }

                        if (ws.Cell(FamilyRows.Ignore, col).GetString().Trim().Equals("Modeller", StringComparison.OrdinalIgnoreCase)
                        || ws.Cell(FamilyRows.Ignore, col).GetString().Trim().Equals("SBIM", StringComparison.OrdinalIgnoreCase))
                            continue;

                        if (string.Equals(header, "Bauteilnummer", StringComparison.OrdinalIgnoreCase))
                            continue;

                        string group = ws.Cell(FamilyRows.Group, col).GetString().Trim();
                        if (string.Equals(group, "Material", StringComparison.OrdinalIgnoreCase))
                            continue;

                        if (!ParameterDefinitions.TryGetValue(header.Replace(",", ""), out var meta))
                            continue;

                        string v = row.Cell(col).GetString().Trim();

                         if (string.IsNullOrWhiteSpace(v))
                            values[meta.Name] = "";
                        else
                            values[meta.Name] = v;
                    }

                    if (values.Count > 0)
                        FamilyData[bn] = values;
                }

            }
        }

        // Shared RNG for placeholder prefixes when the Excel prefix cell is blank (testing aid).
        private static readonly Random _prefixRandom = new Random();

        // Generates a short random placeholder prefix, e.g. "RND-4821".
        private static string GenerateRandomPrefix()
        {
            return "RND-" + _prefixRandom.Next(1000, 9999);
        }

        public static bool AskYesNo(string title, string question)
        {
            TaskDialog td = new TaskDialog(title)
            {
                MainInstruction = question,
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                DefaultButton = TaskDialogResult.No
            };

            TaskDialogResult result = td.Show();

            return result == TaskDialogResult.Yes;
        }

        public static bool CanOpenForRead(string path)
        {
            try
            {
                using (var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    return true;
            }
            catch
            {
                return false;
            }
        }

        public static string ListFolderContents(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
                return "Folder path is empty.";

            if (!Directory.Exists(folderPath))
                return $"Folder not found:\n{folderPath}";

            try
            {
                var entries = Directory.EnumerateFileSystemEntries(folderPath)
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                    .Select(Path.GetFileName)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .ToList();

                if (entries.Count == 0)
                    return $"Folder is empty:\n{folderPath}";

                return $"Folder:\n{folderPath}\n\nContents:\n- {string.Join("\n- ", entries)}";
            }
            catch (Exception ex)
            {
                return $"Failed to read folder contents:\n{folderPath}\n\nError: {ex.Message}";
            }
        }

public static string GetExcelFilePathOrThrow()
{
    // Keep your existing relative structure
    var relativePath = Path.Combine(
        "1761-01 B10-Ulm - Documents",
        "Objektplanung",
        "1761-01.Material-Bauteilliste1.xlsx"
    );

    var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    var oneDrive = Environment.GetEnvironmentVariable("OneDriveCommercial")
               ?? Environment.GetEnvironmentVariable("OneDrive");

    // Move the local function to a private static method to avoid C# 8.0+ features
    var candidates = new[]
    {
        // Your original pattern
        Path.Combine(userProfile, "Müller+Hereth GmbH", relativePath),

        // If the library is synced under the OneDrive root
        string.IsNullOrWhiteSpace(oneDrive) ? null : Path.Combine(oneDrive, relativePath),

        // Fallback: if the company folder sits next to the OneDrive folder
        string.IsNullOrWhiteSpace(oneDrive) ? null : TryParentCompanyFolder(oneDrive, "Müller+Hereth GmbH", relativePath),
    }
    .Where(p => !string.IsNullOrWhiteSpace(p))
    .ToArray();
            TaskDialog.Show("Excel File Search", $"Searching for Excel file in the following locations:\n\n- {string.Join("\n- ", candidates)}");
            var found = candidates.FirstOrDefault(File.Exists);
            if (found != null) return found;


            return null;
}

// Move this out of the method and remove nullable reference type syntax
private static string TryParentCompanyFolder(string oneDrivePath, string companyFolder, string rel)
{
    try
    {
        var parent = Directory.GetParent(oneDrivePath);
        if (parent == null || string.IsNullOrWhiteSpace(parent.FullName)) return null;
        return Path.Combine(parent.FullName, companyFolder, rel);
    }
    catch
    {
        return null;
    }
}
}

}
