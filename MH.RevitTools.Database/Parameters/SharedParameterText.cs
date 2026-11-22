using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace MH.RevitTools.Database.Parameters
{
    /// <summary>
    /// The lines of a Revit shared parameter file. The <c>*GROUP</c> and <c>*PARAM</c> header lines
    /// name the columns, so their order doesn't matter; lines starting with # are comments.
    /// </summary>
    internal static class SharedParameterText
    {
        /// <summary>The parameters by GUID, or null with the reason as the end of a sentence.</summary>
        public static Dictionary<Guid, MasterFileParameter> Parse(TextReader text, out string problem)
        {
            problem = null;
            var groups = new Dictionary<string, string>(StringComparer.Ordinal);
            var parameters = new Dictionary<Guid, MasterFileParameter>();
            Columns groupColumns = null;
            Columns paramColumns = null;
            bool sawMeta = false;
            int lineNumber = 0;

            string line;
            while ((line = text.ReadLine()) != null)
            {
                lineNumber++;
                if (line.Length == 0 || line[0] == '#')
                    continue;

                string[] cells = line.Split('\t');
                switch (cells[0])
                {
                    case "*META":
                    case "META":
                        sawMeta = true;
                        break;
                    case "*GROUP":
                        groupColumns = Columns.Of(cells, "ID", "NAME");
                        break;
                    case "GROUP":
                        if (groupColumns != null && groupColumns.Fits(cells))
                            groups[cells[groupColumns[0]]] = cells[groupColumns[1]];
                        break;
                    case "*PARAM":
                        paramColumns = Columns.Of(cells, "GUID", "NAME", "DATATYPE", "GROUP");
                        if (paramColumns == null)
                            problem = "has a *PARAM line without the GUID, NAME, DATATYPE and GROUP columns (line " + lineNumber + ").";
                        break;
                    case "PARAM":
                        problem = AddParameter(cells, paramColumns, groups, parameters, lineNumber);
                        break;
                }
                if (problem != null)
                    return null;
            }

            if (!sawMeta)
                problem = "isn't a Revit shared parameter file: it has no META line.";
            return problem == null ? parameters : null;
        }

        private static string AddParameter(
            string[] cells, Columns columns, Dictionary<string, string> groups,
            Dictionary<Guid, MasterFileParameter> parameters, int lineNumber)
        {
            if (columns == null)
                return "has a PARAM line before the *PARAM line that names the columns (line " + lineNumber + ").";
            if (!columns.Fits(cells))
                return "has a PARAM line with too few columns (line " + lineNumber + ").";

            Guid guid;
            if (!Guid.TryParse(cells[columns[0]], out guid))
                return "has a PARAM line without a valid GUID (line " + lineNumber + ").";
            if (parameters.ContainsKey(guid))
                return "has the GUID " + guid.ToString("D", CultureInfo.InvariantCulture) + " twice (line " + lineNumber + ").";

            string group;
            groups.TryGetValue(cells[columns[3]], out group);
            parameters.Add(guid, new MasterFileParameter(guid, cells[columns[1]], cells[columns[2]], group));
            return null;
        }

        // Where the named columns are in the lines under a header, e.g. *PARAM GUID NAME DATATYPE ...
        private sealed class Columns
        {
            private readonly int[] _indexes;

            private Columns(int[] indexes)
            {
                _indexes = indexes;
            }

            public int this[int which] => _indexes[which];

            public static Columns Of(string[] header, params string[] names)
            {
                var indexes = new int[names.Length];
                for (int i = 0; i < names.Length; i++)
                {
                    indexes[i] = Array.IndexOf(header, names[i]);
                    if (indexes[i] < 1)
                        return null;
                }
                return new Columns(indexes);
            }

            public bool Fits(string[] cells)
            {
                foreach (int index in _indexes)
                {
                    if (index >= cells.Length)
                        return false;
                }
                return true;
            }
        }
    }
}
