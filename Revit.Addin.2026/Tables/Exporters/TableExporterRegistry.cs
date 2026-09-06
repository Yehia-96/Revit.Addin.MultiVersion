using System.Collections.Generic;
using System.Linq;

namespace Revit.Addin._2026.Tables.Exporters
{
    /// <summary>
    /// Registry of all table output formats. Add a new format by listing its exporter here.
    /// </summary>
    public static class TableExporterRegistry
    {
        public static IReadOnlyList<ITableExporter> GetAll()
        {
            return new ITableExporter[]
            {
                new RevitDraftingViewExporter(),
                new ExcelExporter(),
                new PdfExporter(),
            };
        }

        public static ITableExporter FindByFormatName(string name)
        {
            return GetAll().FirstOrDefault(e => e.FormatName == name);
        }
    }
}
