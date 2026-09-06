using System.Collections.Generic;
using Autodesk.Revit.DB;
using Revit.Addin._2026.Tables.Models;

namespace Revit.Addin._2026.Tables.Templates
{
    /// <summary>
    /// Contract for a fixed-format table. A template defines (a) which elements belong to the
    /// table, (b) the parameter that groups them, (c) the parameter that orders them within a
    /// group, and (d) the columns to project. To add a new fixed table, implement this interface
    /// and register the implementation with <see cref="TableTemplateRegistry"/>.
    /// </summary>
    public interface ITableTemplate
    {
        string TemplateKey { get; }

        string Title { get; }

        string GroupParameterName { get; }

        string SortParameterName { get; }

        IReadOnlyList<TableColumn> Columns { get; }

        bool MatchesElement(Element element);

        IEnumerable<string> SectionOrder { get; }
    }
}
