using System.Collections.Generic;
using Autodesk.Revit.DB;
using Revit.Addin._2024.Tables.Models;
using Revit.Addin._2024.Tables.Services;

namespace Revit.Addin._2024.Tables.Templates
{
    /// <summary>
    /// Fixed-format "Einbauteile" table. Mirrors the printed legend: rows grouped by
    /// "Bateilgruppe" (Fugen, Entwässerung, Elektroausrüstung, Sonstiges, …), sorted within
    /// each group by "Bauteilnummer".
    /// </summary>
    public class EinbauteileTableTemplate : ITableTemplate
    {
        public string TemplateKey => "einbauteile";

        public string Title => "Einbauteile";

        public string GroupParameterName => "Bauteilgruppe";

        public string SortParameterName => "Bauteilnummer";

        public IReadOnlyList<TableColumn> Columns { get; } = new List<TableColumn>
        {
            new TableColumn("Baut.-Nr.",            "Bauteilnummer", 19.9),
            new TableColumn("Bauteil, Teilelement", new[] { "Bauteil", "Teilelement" }, 49.6),
            new TableColumn("Typ",                  "Typ",           29.8),
            new TableColumn("Beschreibung",         "Beschreibung",  125.7),
            new TableColumn("Material",             "Material",      29.8),
            new TableColumn("Norm",                 "Norm",          25.0),
        };

        // The order section headers are rendered in. Sections that are present but not
        // listed here will be appended afterwards in alphabetical order.
        public IEnumerable<string> SectionOrder => new[]
        {
            "Fugen",
            "Entwässerung",
            "Elektroausrüstung",
            "Sonstiges",
        };

        public bool MatchesElement(Element element)
        {
            if (element == null) return false;

            // An element belongs to this table if it carries a Bauteilnummer.
            var p = element.LookupParameter(SortParameterName);
            if (p == null || !p.HasValue) return false;

            var value = ParameterValueReader.GetParameterValue(element, SortParameterName);
            return !string.IsNullOrWhiteSpace(value);
        }
    }
}
