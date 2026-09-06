using System.Collections.Generic;

namespace Revit.Addin._2027.Tables.Models
{
    /// <summary>
    /// A single row of a generated table. <see cref="Cells"/> is keyed by the column header
    /// from the <see cref="TableColumn.Header"/> for predictable lookup at export time.
    /// </summary>
    public class TableRow
    {
        public string SortKey { get; set; }

        public Dictionary<string, string> Cells { get; set; } = new Dictionary<string, string>();
    }
}
