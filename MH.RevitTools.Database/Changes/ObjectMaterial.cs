using System;
using System.Collections.Generic;
using System.Linq;
using MH.RevitTools.Database.Link;

namespace MH.RevitTools.Database.Changes
{
    /// <summary>
    /// Compares what the database says an object is made of with the material an element has in the model, by the
    /// Materialname. Called on Revit's main thread.
    /// </summary>
    public delegate ValueCheck CompareMaterial(long elementId, string materialName);

    /// <summary>An object's material, which the database holds with the object, not among its values.</summary>
    public static class ObjectMaterial
    {
        /// <summary>The elements' parameter for it, as in the Excel import; also what the pane calls it.</summary>
        public const string ParameterName = "Material";

        /// <summary>
        /// True for the ID of a material that belongs to no project: Studio's placeholders where an ID has its project
        /// (MP-9999-99-BET-0101, and 9999-98).
        /// </summary>
        public static bool IsGlobal(string materialId)
        {
            string[] parts = (materialId ?? "").Split('-');
            return parts.Length > 3 && parts[1] == "9999" && (parts[2] == "99" || parts[2] == "98");
        }

        /// <summary>
        /// The one material these rows of one object name, as it was set last; null when they name several, as when two
        /// objects share a Bauteil-ID.
        /// </summary>
        internal static LinkObjectMaterial TheOne(IReadOnlyCollection<LinkObjectMaterial> rows)
        {
            LinkObjectMaterial last = rows.OrderByDescending(row => row.ChangedAtUtc ?? DateTime.MinValue).First();
            return rows.All(row => string.Equals(row.MaterialName.Trim(), last.MaterialName.Trim(), StringComparison.Ordinal)
                && string.Equals(row.MaterialId, last.MaterialId, StringComparison.OrdinalIgnoreCase))
                ? last
                : null;
        }

        /// <summary>The rows per object, by Bauteil-ID, whatever its case.</summary>
        internal static Dictionary<string, List<LinkObjectMaterial>> ByObject(IEnumerable<LinkObjectMaterial> rows) => rows
            .Where(row => !string.IsNullOrWhiteSpace(row.BauteilId))
            .GroupBy(row => row.BauteilId.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
    }
}
