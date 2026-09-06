using Autodesk.Revit.UI;
using TableData = Revit.Addin._2027.Tables.Models.TableData;
using RevitDocument = Autodesk.Revit.DB.Document;

namespace Revit.Addin._2027.Tables.Exporters
{
    /// <summary>
    /// Output format for a built <see cref="TableData"/>. Implementations are pluggable —
    /// register new formats by adding a class that implements this interface and listing it
    /// in <see cref="TableExporterRegistry"/>.
    /// </summary>
    public interface ITableExporter
    {
        string FormatName { get; }

        /// <summary>True for exporters that write a file outside Revit (Excel, PDF). False for
        /// exporters that produce content inside the Revit document (drafting view).</summary>
        bool RequiresOutputPath { get; }

        /// <summary>Default file extension including dot, or null for in-Revit exporters.</summary>
        string FileExtension { get; }

        /// <summary>Suggested file-save dialog filter (e.g. "Excel files (*.xlsx)|*.xlsx").</summary>
        string FileFilter { get; }

        ExportResult Export(TableData data, ExportContext context);
    }

    public class ExportContext
    {
        public RevitDocument Document { get; set; }
        public UIDocument UIDocument { get; set; }

        /// <summary>Absolute path the user picked, or null for in-Revit exporters.</summary>
        public string OutputPath { get; set; }
    }

    public class ExportResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public string ProducedPath { get; set; }
    }
}
