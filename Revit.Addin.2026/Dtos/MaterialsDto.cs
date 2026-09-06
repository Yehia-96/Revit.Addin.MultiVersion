using Autodesk.Revit.DB;
using System;
using System.Linq;

namespace Revit.Addin._2026.Dtos
{
    public class MaterialsDto
    {
        public string MaterialName { get; }
        public string MaterialType { get; }

        public string CutPattern { get; set; }
        public string SurfacePattern { get; set; }

        public StructuralAsset StructuralAsset { get; }

        public MaterialsDto(string materialName, string materialType, string cutPattern, string surfacePattern)
        {
            var normalizedName = (materialName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedName))
                throw new ArgumentException("Material name cannot be empty.", nameof(materialName));

            CutPattern = (cutPattern ?? null).Trim();
            SurfacePattern = (surfacePattern ?? null).Trim();

            MaterialName = normalizedName;
            MaterialType = (materialType ?? string.Empty).Trim();
            StructuralAsset = CreateStructuralAsset();
        }

        private StructuralAsset CreateStructuralAsset()
        {
            string type = MaterialType.ToLowerInvariant();
            var safeAssetName = BuildAssetName(MaterialName);

            switch (type)
            {
                case "beton":
                    return new StructuralAsset(safeAssetName, StructuralAssetClass.Concrete);
                case "metall":
                    return new StructuralAsset(safeAssetName, StructuralAssetClass.Metal);
                case "kunststoff":
                    return new StructuralAsset(safeAssetName, StructuralAssetClass.Plastic);
                case "sonstiges":
                case "boden":
                case "erdbau":
                    return new StructuralAsset(safeAssetName, StructuralAssetClass.Generic);
                default:
                    return new StructuralAsset(safeAssetName, StructuralAssetClass.Undefined);
            }
        }

        private static string BuildAssetName(string materialName)
        {
            var invalidChars = new[] { '\\', '/', ':', ';', '|', ',', '[', ']', '{', '}', '<', '>', '?', '*', '"', '\'' };
            var filtered = new string(materialName.Where(c => !invalidChars.Contains(c)).ToArray()).Trim();
            if (string.IsNullOrWhiteSpace(filtered))
                filtered = "Material";

            return $"Properties for {filtered}";
        }
    }
}
