namespace MH.RevitTools.Database.Link
{
    /// <summary>One material of a project.</summary>
    public sealed class LinkMaterial
    {
        public LinkMaterial(
            string materialId,
            string name,
            string groupCode,
            string group,
            string surfacePattern,
            string cutPattern)
        {
            MaterialId = materialId ?? "";
            Name = name ?? "";
            GroupCode = groupCode;
            Group = group;
            SurfacePattern = string.IsNullOrWhiteSpace(surfacePattern) ? null : surfacePattern;
            CutPattern = string.IsNullOrWhiteSpace(cutPattern) ? null : cutPattern;
        }

        /// <summary>E.g. MB-1761-01-BET-0031, the same text the objects carry as their Material-ID.</summary>
        public string MaterialId { get; }

        /// <summary>The Materialname, e.g. "31-Beton, C35/45".</summary>
        public string Name { get; }

        /// <summary>E.g. BET.</summary>
        public string GroupCode { get; }

        /// <summary>The Materialgruppe, e.g. Beton.</summary>
        public string Group { get; }

        /// <summary>The surface fill pattern's name; null for none.</summary>
        public string SurfacePattern { get; }

        /// <summary>The cut fill pattern's name; null for none.</summary>
        public string CutPattern { get; }
    }
}
