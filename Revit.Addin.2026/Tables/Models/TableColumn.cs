using System.Collections.Generic;

namespace Revit.Addin._2026.Tables.Models
{
    /// <summary>
    /// Defines a single column of a fixed-format table. A column may pull from one or more
    /// Revit parameters; when more than one is supplied the non-empty values are joined by
    /// <see cref="Separator"/>. <see cref="Width"/> is expressed in millimetres and is used by
    /// drafting-view and Excel exporters to size the column.
    /// </summary>
    public class TableColumn
    {
        public string Header { get; set; }

        public IReadOnlyList<string> ParameterNames { get; set; } = new string[0];

        public string Separator { get; set; } = ", ";

        public double Width { get; set; } = 30.0;

        public TableColumn() { }

        public TableColumn(string header, string parameterName, double width)
        {
            Header = header;
            ParameterNames = new[] { parameterName };
            Width = width;
        }

        public TableColumn(string header, IReadOnlyList<string> parameterNames, double width, string separator = ", ")
        {
            Header = header;
            ParameterNames = parameterNames;
            Width = width;
            Separator = separator;
        }
    }
}
