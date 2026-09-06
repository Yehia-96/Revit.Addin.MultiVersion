using System.Collections.Generic;

namespace Revit.Addin._2024.Tables.Models
{
    /// <summary>
    /// A group of rows under a shared section header (e.g. "Fugen", "Entwässerung").
    /// </summary>
    public class TableSection
    {
        public string Name { get; set; }

        public List<TableRow> Rows { get; set; } = new List<TableRow>();
    }
}
