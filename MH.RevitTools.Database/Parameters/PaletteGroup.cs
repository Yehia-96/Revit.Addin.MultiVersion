namespace MH.RevitTools.Database.Parameters
{
    /// <summary>Where a parameter shows in Revit's Properties palette.</summary>
    public enum PaletteGroup
    {
        General,
        IdentityData,
        Geometry,
        Constraints,
        Data,
        Graphics,
        Materials,
    }

    /// <summary>
    /// The palette group for a dictionary group code, the same as the Excel route gives the group's name
    /// (ParamMetaData.GroupMap): Kategorisierung and Beschreibung under Identity Data, Geometrie under
    /// Geometry, and so on. Only where a parameter shows, so a group it doesn't know lands under General.
    /// </summary>
    public static class PaletteGroups
    {
        public static PaletteGroup ForGroupCode(string groupCode)
        {
            switch ((groupCode ?? "").Trim().ToUpperInvariant())
            {
                case "KAT": // Kategorisierung
                case "BES": // Beschreibung
                    return PaletteGroup.IdentityData;
                case "GEO": // Geometrie
                    return PaletteGroup.Geometry;
                case "ORT":
                    return PaletteGroup.Constraints;
                case "SPE": // Spezifisch
                    return PaletteGroup.Data;
                case "DAR": // Darstellung
                    return PaletteGroup.Graphics;
                case "BET": // Beton
                case "BOD": // Boden
                case "ERD": // Erdbau
                case "KUN": // Kunststoff
                case "MET": // Metall
                case "MAT": // Material
                    return PaletteGroup.Materials;
                default: // ALL (Allgemein), SON (Sonstiges) and groups the Excel route doesn't name
                    return PaletteGroup.General;
            }
        }
    }
}
