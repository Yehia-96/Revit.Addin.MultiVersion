using System.Collections.Generic;
using System.Linq;

namespace Revit.Addin._2027.Tables.Models
{
    /// <summary>
    /// The fully-resolved contents of a table, ready to be handed to any
    /// <c>ITableExporter</c>. Built by <c>TableDataBuilder</c> from an
    /// <c>ITableTemplate</c> plus a Revit element selection.
    /// </summary>
    public class TableData
    {
        public string Title { get; set; }

        public List<TableColumn> Columns { get; set; } = new List<TableColumn>();

        public List<TableSection> Sections { get; set; } = new List<TableSection>();

        public int TotalRowCount => Sections?.Sum(s => s.Rows?.Count ?? 0) ?? 0;
    }
}
