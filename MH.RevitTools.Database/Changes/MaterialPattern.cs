using System.Collections.Generic;
using MH.RevitTools.Database.Link;

namespace MH.RevitTools.Database.Changes
{
    /// <summary>One of a material's two fill patterns. The database holds them with the material, not among its values.</summary>
    public enum MaterialPattern
    {
        Surface,
        Cut,
    }

    /// <summary>
    /// Compares the database's fill pattern with the one a material has in the model, which has it by name. Called on
    /// Revit's main thread.
    /// </summary>
    /// <param name="materialId">Revit's element id.</param>
    /// <param name="patternName">The pattern's name in the database, e.g. "Ortbeton - bewehrt" or "Solid".</param>
    public delegate ValueCheck ComparePattern(long materialId, MaterialPattern which, string patternName);

    /// <summary>What a material's fill patterns are called, and where the database has them.</summary>
    public static class MaterialPatterns
    {
        public static IReadOnlyList<MaterialPattern> All { get; } = new[] { MaterialPattern.Surface, MaterialPattern.Cut };

        /// <summary>What the pane calls it.</summary>
        public static string Name(MaterialPattern which) => which == MaterialPattern.Surface ? "Surface pattern" : "Cut pattern";

        /// <summary>The database's, by the pattern's name; empty for none, and for a placeholder.</summary>
        public static string Of(LinkMaterial material, MaterialPattern which)
        {
            string name = which == MaterialPattern.Surface ? material.SurfacePattern : material.CutPattern;
            return Placeholders.IsPlaceholder(name) ? "" : name.Trim();
        }
    }
}
