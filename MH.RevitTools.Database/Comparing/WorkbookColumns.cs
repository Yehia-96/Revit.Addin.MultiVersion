using System;
using System.Collections.Generic;
using System.Linq;

namespace MH.RevitTools.Database.Comparing
{
    /// <summary>
    /// The columns of a Planungsdatenbank sheet, to find the dictionary key of a parameter the Excel reader gives.
    /// The reader tells a parameter by its Parameter-ID and its name. Both must fit, so a column whose ID is a copy
    /// of another's isn't taken for that other.
    /// </summary>
    public sealed class WorkbookColumns
    {
        private const string Unknown = "?";

        private readonly List<Column> _columns = new List<Column>();

        /// <summary>A column, by the sheet's label rows: "109", "BAU", "GEO", "Dicke".</summary>
        public void Add(string parameterId, string setCode, string groupCode, string header) =>
            _columns.Add(new Column(parameterId, LoadedProjectBuilder.KeyOf(setCode, groupCode, header), header));

        /// <summary>
        /// The dictionary key, e.g. BAU-GEO-Dicke. Where no column fits, or columns that mean different parameters
        /// do, it's a key the dictionary can't have, so the parameter shows up as a difference instead of being
        /// taken for another one.
        /// </summary>
        /// <param name="name">As the Excel reader names it: the header, and for a repeated one also its set, e.g.
        /// "Beschreibung (Bauteilinformation)".</param>
        public string KeyOf(string parameterId, string name)
        {
            string id = (parameterId ?? "").Trim(), readerName = (name ?? "").Trim();
            List<string> keys = _columns
                .Where(column => Same(column.ParameterId, id) && column.IsNamed(readerName))
                .Select(column => column.Key)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return keys.Count == 1 ? keys[0] : LoadedProjectBuilder.KeyOf(Unknown, Unknown, readerName);
        }

        private static bool Same(string first, string second) => string.Equals(first, second, StringComparison.OrdinalIgnoreCase);

        private sealed class Column
        {
            private readonly string _header;

            public Column(string parameterId, string key, string header)
            {
                ParameterId = (parameterId ?? "").Trim();
                Key = key;
                _header = (header ?? "").Trim();
            }

            public string ParameterId { get; }

            public string Key { get; }

            public bool IsNamed(string readerName) =>
                Same(_header, readerName) || readerName.StartsWith(_header + " (", StringComparison.OrdinalIgnoreCase);
        }
    }
}
