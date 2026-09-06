using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Revit.Addin._2027.Tables.Templates;
using TableData = Revit.Addin._2027.Tables.Models.TableData;
using TableRow = Revit.Addin._2027.Tables.Models.TableRow;
using TableSection = Revit.Addin._2027.Tables.Models.TableSection;

namespace Revit.Addin._2027.Tables.Services
{
    /// <summary>
    /// Pulls elements out of the active view, applies a template to read parameter values,
    /// deduplicates by sort key, then groups + orders the rows into a <see cref="TableData"/>.
    ///
    /// The same Bauteilnummer can occur on many physical elements; the printed table only
    /// shows one row per number, so we deduplicate on the sort key within a section.
    ///
    /// An optional set of Bauteilnummern from the loaded Excel sheet can be appended after
    /// the model pass — useful for elements that are described in the Excel sheet but are
    /// never modelled. Model rows always win: if a Bauteilnummer is already present from the
    /// active view, the Excel row for the same number is skipped.
    /// </summary>
    public static class TableDataBuilder
    {
        public static TableData Build(Document doc, ITableTemplate template)
            => Build(doc, template, null, null, null);

        public static TableData Build(
            Document doc,
            ITableTemplate template,
            IEnumerable<string> extraBauteilnummern,
            IReadOnlyDictionary<string, Dictionary<string, string>> excelFamilyData)
            => Build(doc, template, extraBauteilnummern, excelFamilyData, null);

        public static TableData Build(
            Document doc,
            ITableTemplate template,
            IEnumerable<string> extraBauteilnummern,
            IReadOnlyDictionary<string, Dictionary<string, string>> excelFamilyData,
            IEnumerable<string> excludedBauteilnummern)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (template == null) throw new ArgumentNullException(nameof(template));

            // Sort keys the user explicitly chose to omit from the table. Trimmed +
            // case-insensitive so the comparison matches the values shown in the picker.
            var excludedSet = new HashSet<string>(
                (excludedBauteilnummern ?? Enumerable.Empty<string>())
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .Select(v => v.Trim()),
                StringComparer.OrdinalIgnoreCase);

            var rowsBySection = new Dictionary<string, Dictionary<string, TableRow>>(StringComparer.OrdinalIgnoreCase);

            // -----------------------------
            // Pass 1 — model elements in the active view.
            // -----------------------------
            foreach (var element in CollectActiveViewElements(doc, template))
            {
                string sortKey = ParameterValueReader.GetParameterValue(element, template.SortParameterName);
                if (string.IsNullOrWhiteSpace(sortKey))
                    continue;

                // Honour the user's exclusion list.
                if (excludedSet.Contains(sortKey.Trim()))
                    continue;

                string sectionName = ParameterValueReader.GetParameterValue(element, template.GroupParameterName);
                if (string.IsNullOrWhiteSpace(sectionName))
                    sectionName = "(Ohne Gruppe)";

                if (!rowsBySection.TryGetValue(sectionName, out var byKey))
                {
                    byKey = new Dictionary<string, TableRow>(StringComparer.OrdinalIgnoreCase);
                    rowsBySection[sectionName] = byKey;
                }

                // First element wins per (section, sortKey) — duplicates would render the
                // same row twice.
                if (byKey.ContainsKey(sortKey)) continue;

                byKey[sortKey] = BuildRow(element, template, sortKey);
            }

            // -----------------------------
            // Pass 2 — append rows for Excel-only Bauteilnummern. Model wins: skip if the
            // sort key is already in any section produced from the model view.
            // -----------------------------
            if (extraBauteilnummern != null && excelFamilyData != null)
            {
                var alreadyPresent = new HashSet<string>(
                    rowsBySection.Values.SelectMany(d => d.Keys),
                    StringComparer.OrdinalIgnoreCase);

                foreach (var raw in extraBauteilnummern)
                {
                    string bn = (raw ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(bn)) continue;

                    // Honour the user's exclusion list here too, for consistency.
                    if (excludedSet.Contains(bn))
                        continue;

                    if (!excelFamilyData.TryGetValue(bn, out var values) || values == null)
                        continue;

                    if (alreadyPresent.Contains(bn))
                        continue;

                    string sectionName = null;
                    if (values.TryGetValue(template.GroupParameterName, out var groupValue))
                        sectionName = (groupValue ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(sectionName))
                        sectionName = "(Ohne Gruppe)";

                    if (!rowsBySection.TryGetValue(sectionName, out var byKey))
                    {
                        byKey = new Dictionary<string, TableRow>(StringComparer.OrdinalIgnoreCase);
                        rowsBySection[sectionName] = byKey;
                    }

                    byKey[bn] = BuildExcelRow(template, bn, values);
                    alreadyPresent.Add(bn);
                }
            }

            var data = new TableData
            {
                Title = template.Title,
                Columns = template.Columns.ToList(),
                Sections = OrderSections(rowsBySection, template).ToList(),
            };

            return data;
        }

        /// <summary>
        /// Returns the distinct sort keys (Bauteilnummern) of the model elements that the
        /// template matches in the active view, naturally sorted. Used to populate the
        /// "exclude from table" picker so the modeller only sees numbers that would
        /// actually appear in the generated table.
        /// </summary>
        public static IReadOnlyList<string> GetActiveViewSortKeys(Document doc, ITableTemplate template)
        {
            if (doc == null || template == null)
                return new List<string>();

            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var element in CollectActiveViewElements(doc, template))
            {
                string sortKey = ParameterValueReader.GetParameterValue(element, template.SortParameterName);
                if (!string.IsNullOrWhiteSpace(sortKey))
                    keys.Add(sortKey.Trim());
            }

            return keys.OrderBy(k => k, NaturalSortComparer.Instance).ToList();
        }

        /// <summary>
        /// The single source of truth for "which elements does this template pull from the
        /// active view". Shared by <see cref="Build"/> and <see cref="GetActiveViewSortKeys"/>.
        /// </summary>
        private static List<Element> CollectActiveViewElements(Document doc, ITableTemplate template)
        {
            View activeView = doc.ActiveView;
            if (activeView == null)
                return new List<Element>();

            return new FilteredElementCollector(doc, activeView.Id)
                .WhereElementIsNotElementType()
                .Where(e => e.Category != null)
                .Where(e => e.Category.CategoryType == CategoryType.Model)
                .Where(e => !e.ViewSpecific)
                .Where(template.MatchesElement)
                .ToList();
        }

        private static TableRow BuildRow(Element element, ITableTemplate template, string sortKey)
        {
            var row = new TableRow { SortKey = sortKey };

            foreach (var column in template.Columns)
            {
                var parts = column.ParameterNames
                    .Select(p => ParameterValueReader.GetParameterValue(element, p))
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .ToArray();

                row.Cells[column.Header] = parts.Length == 0
                    ? string.Empty
                    : string.Join(column.Separator ?? ", ", parts);
            }

            return row;
        }

        /// <summary>
        /// Builds a row from an Excel <c>FamilyData</c> entry. The outer dictionary key is
        /// the Bauteilnummer, and the Excel parser intentionally drops the Bauteilnummer
        /// column from the inner dictionary — so when a column requests the template's
        /// sort parameter, we substitute the outer key.
        /// </summary>
        private static TableRow BuildExcelRow(
            ITableTemplate template,
            string bauteilnummer,
            Dictionary<string, string> values)
        {
            var row = new TableRow { SortKey = bauteilnummer };

            foreach (var column in template.Columns)
            {
                var parts = column.ParameterNames
                    .Select(p =>
                    {
                        if (string.Equals(p, template.SortParameterName, StringComparison.OrdinalIgnoreCase))
                            return bauteilnummer;

                        return values.TryGetValue(p, out var v)
                            ? (v ?? string.Empty).Trim()
                            : string.Empty;
                    })
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .ToArray();

                row.Cells[column.Header] = parts.Length == 0
                    ? string.Empty
                    : string.Join(column.Separator ?? ", ", parts);
            }

            return row;
        }

        private static IEnumerable<TableSection> OrderSections(
            Dictionary<string, Dictionary<string, TableRow>> rowsBySection,
            ITableTemplate template)
        {
            var preferredOrder = (template.SectionOrder ?? Enumerable.Empty<string>()).ToList();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Preferred order first.
            foreach (var name in preferredOrder)
            {
                if (rowsBySection.TryGetValue(name, out var byKey))
                {
                    seen.Add(name);
                    yield return new TableSection
                    {
                        Name = name,
                        Rows = SortRows(byKey.Values).ToList(),
                    };
                }
            }

            // Anything else, alphabetically.
            foreach (var entry in rowsBySection
                         .Where(kv => !seen.Contains(kv.Key))
                         .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
            {
                yield return new TableSection
                {
                    Name = entry.Key,
                    Rows = SortRows(entry.Value.Values).ToList(),
                };
            }
        }

        private static IEnumerable<TableRow> SortRows(IEnumerable<TableRow> rows)
        {
            // Sort numerically when possible (e.g. "201" < "1001"), fall back to ordinal.
            return rows.OrderBy(r => r.SortKey, NaturalSortComparer.Instance);
        }

        private sealed class NaturalSortComparer : IComparer<string>
        {
            public static readonly NaturalSortComparer Instance = new NaturalSortComparer();

            public int Compare(string x, string y)
            {
                if (ReferenceEquals(x, y)) return 0;
                if (x == null) return -1;
                if (y == null) return 1;

                bool xIsNum = long.TryParse(x, NumberStyles.Integer, CultureInfo.InvariantCulture, out var xn);
                bool yIsNum = long.TryParse(y, NumberStyles.Integer, CultureInfo.InvariantCulture, out var yn);

                if (xIsNum && yIsNum) return xn.CompareTo(yn);
                if (xIsNum) return -1;
                if (yIsNum) return 1;
                return string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
