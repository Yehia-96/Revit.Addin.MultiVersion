// Shared source: keep identical in Revit.Addin.2024 / .2026 / .2027.
// C# 7.3 / net48 floor - see PlanungsdatenbankLayout.cs for the full restriction list.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using ClosedXML.Excel;

namespace Revit.Addin._2026.Services.Planungsdatenbank
{
    /// <summary>
    /// Label-addressed access to one worksheet.
    /// <para>
    /// Deliberately free of any <c>Autodesk.Revit</c> type: this is the fiddly part - finding
    /// rows and columns by the text in them - and keeping it Revit-free means it can be reasoned
    /// about, and later tested, without a Revit host. The mapping onto Revit's own types happens
    /// one layer up, in the parser.
    /// </para>
    /// </summary>
    internal sealed class SheetGrid
    {
        private readonly IXLWorksheet _sheet;
        private readonly Dictionary<string, int> _labelRows;

        /// <summary>Name of the sheet, for messages.</summary>
        internal string SheetName { get; private set; }

        /// <summary>Last row holding anything at all.</summary>
        internal int LastRow { get; private set; }

        /// <summary>Last column holding anything at all.</summary>
        internal int LastColumn { get; private set; }

        private SheetGrid(IXLWorksheet sheet)
        {
            _sheet = sheet;
            SheetName = sheet.Name;

            IXLRangeAddress used = sheet.RangeUsed() == null ? null : sheet.RangeUsed().RangeAddress;
            LastRow = used == null ? 0 : used.LastAddress.RowNumber;
            LastColumn = used == null ? 0 : used.LastAddress.ColumnNumber;

            _labelRows = ReadLabelRows();
        }

        /// <summary>
        /// Opens a sheet <em>by name</em>, case-insensitively.
        /// </summary>
        /// <exception cref="InvalidDataException">
        /// No such sheet. The message lists what the workbook does contain, because the usual
        /// cause is being handed the wrong workbook, and "sheet not found" alone does not say that.
        /// </exception>
        internal static SheetGrid Open(XLWorkbook workbook, string sheetName)
        {
            if (workbook == null) throw new ArgumentNullException("workbook");
            if (string.IsNullOrWhiteSpace(sheetName)) throw new ArgumentException("Sheet name is empty.", "sheetName");

            foreach (IXLWorksheet candidate in workbook.Worksheets)
            {
                if (string.Equals(candidate.Name, sheetName, StringComparison.OrdinalIgnoreCase))
                    return new SheetGrid(candidate);
            }

            var names = new List<string>();
            foreach (IXLWorksheet w in workbook.Worksheets) names.Add(w.Name);
            throw new InvalidDataException(
                "This workbook has no sheet named \"" + sheetName + "\". It contains: " +
                string.Join(", ", names.ToArray()) + ".");
        }

        /// <summary>True when the workbook has a sheet with this name. Used for format detection.</summary>
        internal static bool HasSheet(XLWorkbook workbook, string sheetName)
        {
            if (workbook == null) return false;
            foreach (IXLWorksheet candidate in workbook.Worksheets)
            {
                if (string.Equals(candidate.Name, sheetName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>Trimmed text of a cell. Never null.</summary>
        /// <remarks>
        /// Numeric cells are formatted with the invariant culture. <c>GetString()</c> uses the
        /// current culture, so on a German-locale machine - which is every machine in this office -
        /// a thickness of 0.5 would come back as "0,5", and every later
        /// <c>double.Parse(..., InvariantCulture)</c> would fail on it.
        /// </remarks>
        internal string Text(int row, int column)
        {
            if (row < 1 || column < 1) return string.Empty;

            IXLCell cell = _sheet.Cell(row, column);
            if (cell.DataType == XLDataType.Number)
            {
                double number;
                if (cell.TryGetValue(out number))
                    return number.ToString("R", CultureInfo.InvariantCulture);
            }

            string raw = cell.GetString();
            return raw == null ? string.Empty : raw.Trim();
        }

        /// <summary>
        /// Row number carrying one of the given labels in the label column, or 0 if none of them
        /// is present. Several labels are accepted so a known misspelling in the workbook can be
        /// tolerated without silently accepting anything.
        /// </summary>
        internal int LabelRow(params string[] labels)
        {
            if (labels == null) return 0;
            for (int i = 0; i < labels.Length; i++)
            {
                if (string.IsNullOrEmpty(labels[i])) continue;
                int row;
                if (_labelRows.TryGetValue(labels[i].Trim(), out row)) return row;
            }
            return 0;
        }

        /// <summary>
        /// Row number for a required label.
        /// </summary>
        /// <exception cref="InvalidDataException">The label is missing - the sheet is not the shape we expect.</exception>
        internal int RequireLabelRow(params string[] labels)
        {
            int row = LabelRow(labels);
            if (row > 0) return row;

            string wanted = labels == null ? "(none)" : string.Join("\" or \"", labels);
            throw new InvalidDataException(
                "Sheet \"" + SheetName + "\" has no \"" + wanted + "\" row in column " +
                PlanungsdatenbankLayout.LabelColumn + " within the first " +
                PlanungsdatenbankLayout.MaxLabelRow + " rows. " +
                "Either this is not a Planungsdatenbank workbook, or its layout changed.");
        }

        /// <summary>
        /// Every parameter column in <paramref name="headerRow"/>, in column order, header text
        /// paired with column number.
        /// <para>
        /// Duplicated header names are kept, not collapsed: this workbook reuses short names
        /// across Parametersets, and the caller is the one that can disambiguate them. Dropping
        /// them here would silently lose a column.
        /// </para>
        /// </summary>
        internal List<KeyValuePair<string, int>> HeaderColumns(int headerRow)
        {
            var columns = new List<KeyValuePair<string, int>>();
            for (int col = PlanungsdatenbankLayout.FirstDataColumn; col <= LastColumn; col++)
            {
                string header = Text(headerRow, col);
                if (header.Length == 0) continue;
                columns.Add(new KeyValuePair<string, int>(header, col));
            }
            return columns;
        }

        /// <summary>
        /// Header names that appear more than once in <paramref name="columns"/>, compared
        /// case-insensitively.
        /// </summary>
        internal static HashSet<string> DuplicateHeaders(List<KeyValuePair<string, int>> columns)
        {
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, int> column in columns)
            {
                int n;
                counts[column.Key] = counts.TryGetValue(column.Key, out n) ? n + 1 : 1;
            }

            var duplicates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, int> entry in counts)
            {
                if (entry.Value > 1) duplicates.Add(entry.Key);
            }
            return duplicates;
        }

        /// <summary>
        /// First row below the last label row - where data begins. Derived rather than hardcoded,
        /// so inserting a label row does not shift the reader off the data.
        /// </summary>
        internal int FirstDataRow()
        {
            int lowestLabel = 0;
            foreach (KeyValuePair<string, int> entry in _labelRows)
            {
                if (entry.Value > lowestLabel) lowestLabel = entry.Value;
            }
            if (lowestLabel == 0) return 0;

            // The workbook leaves a blank row between the labels and the data; do not assume it.
            for (int row = lowestLabel + 1; row <= LastRow; row++)
            {
                if (!IsRowEmpty(row)) return row;
            }
            return 0;
        }

        private bool IsRowEmpty(int row)
        {
            for (int col = PlanungsdatenbankLayout.FirstDataColumn; col <= LastColumn; col++)
            {
                if (Text(row, col).Length > 0) return false;
            }
            return true;
        }

        private Dictionary<string, int> ReadLabelRows()
        {
            var found = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int limit = Math.Min(PlanungsdatenbankLayout.MaxLabelRow, LastRow);

            for (int row = 1; row <= limit; row++)
            {
                string label = Text(row, PlanungsdatenbankLayout.LabelColumn);
                if (label.Length == 0) continue;
                if (!found.ContainsKey(label)) found.Add(label, row);
            }
            return found;
        }

        /// <summary>
        /// Parses an integer cell. The workbook writes these as numbers, so a stored "1101.0"
        /// must not become 0; invariant culture, because the file is German-authored but the
        /// numbers are plain.
        /// </summary>
        internal static bool TryInt(string text, out int value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;

            string trimmed = text.Trim();
            if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                return true;

            double asDouble;
            if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out asDouble))
            {
                value = (int)Math.Round(asDouble);
                return true;
            }
            return false;
        }
    }
}
