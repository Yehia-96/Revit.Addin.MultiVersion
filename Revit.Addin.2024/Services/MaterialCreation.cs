using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Revit.Addin._2024.Dtos;

namespace Revit.Addin._2024.Services
{
    public static class MaterialCreation
    {
        public static List<MaterialsDto> MaterialsList = new List<MaterialsDto>();

        public static ImportReport MaterialGeneration(Document doc)
        {
            var report = new ImportReport();

            if (doc == null)
            {
                report.AddError("Document was null. Material creation aborted.");
                return report;
            }

            if (MaterialsList == null || MaterialsList.Count == 0)
            {
                report.AddWarn("No materials were loaded from Excel. Nothing to create.");
                return report;
            }

            var existingMaterials = new FilteredElementCollector(doc)
                .OfClass(typeof(Material))
                .Cast<Material>()
                .ToList();

            var existingNames = new HashSet<string>(
                existingMaterials.Select(m => m.Name),
                StringComparer.OrdinalIgnoreCase);

            var uniqueInput = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (MaterialsDto mat in MaterialsList)
            {
                if (mat == null)
                {
                    report.AddWarn("Encountered an empty material row in the input list.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(mat.MaterialName))
                {
                    report.AddWarn("A material with empty name was skipped.");
                    continue;
                }

                var materialName = mat.MaterialName.Trim();
                if (!uniqueInput.Add(materialName))
                {
                    report.AddWarn($"Duplicate input material '{materialName}' skipped.");
                    continue;
                }

                if (existingNames.Contains(materialName))
                {
                    report.AddInfo($"Material '{materialName}' already exists and was not recreated.");
                    continue;
                }

                try
                {
                   

                    using (var transaction = new Transaction(doc, $"Create material '{materialName}'"))
                    {
                        transaction.Start();

                        var materialId = Material.Create(doc, materialName);
                        var material = doc.GetElement(materialId) as Material;

                        if (material == null)
                            throw new InvalidOperationException($"Revit returned null for created material '{materialName}'.");

                        material.MaterialClass = GetRevitMaterialClass(mat.MaterialType);

                        var template = FindTemplateMaterial(existingMaterials, mat.MaterialType, material.MaterialClass);
                        if (template != null)
                        {
                            CopyVisualProperties(material, template);
                            report.AddInfo($"Applied visual template '{template.Name}' to '{materialName}'.");
                        }
                        else
                        {
                            report.AddWarn($"No matching template found for '{materialName}'. Revit defaults were used.");
                        }

                        var structuralAsset = mat.StructuralAsset ?? new StructuralAsset($"Properties for {materialName}", StructuralAssetClass.Generic);
                        var pse = PropertySetElement.Create(doc, structuralAsset);
                        material.SetMaterialAspectByPropertySet(MaterialAspect.Structural, pse.Id);

                        existingMaterials.Add(material);
                        existingNames.Add(materialName);
                        report.WrittenValues++;


                        transaction.Commit();
                    }

                }
                catch (Exception ex)
                {
                    report.AddError($"Failed to create '{materialName}': {ex.Message}");
                }
            }

            if (report.WrittenValues == 0 && report.Errors.Count == 0)
                report.AddWarn("No materials were created.");

            return report;
        }


        public static ImportReport AssignFillPatterns(Document doc, string sourceLibraryPath = null)
        {
            var report = new ImportReport();

            if (doc == null)
            {
                report.AddError("Document was null. Pattern assignment aborted.");
                return report;
            }

            if (MaterialsList == null || MaterialsList.Count == 0)
            {
                report.AddWarn("No materials were loaded from Excel. Nothing to assign.");
                return report;
            }

            sourceLibraryPath = string.IsNullOrWhiteSpace(sourceLibraryPath)
                ? FillPattern.libraryPath
                : sourceLibraryPath;

            var materialMap = new FilteredElementCollector(doc)
                .OfClass(typeof(Material))
                .Cast<Material>()
                .Where(m => m != null && !string.IsNullOrWhiteSpace(m.Name))
                .GroupBy(m => m.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var allPatternNames = MaterialsList
                .Where(m => m != null)
                .SelectMany(m => new[] { m.SurfacePattern, m.CutPattern })
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n.Trim())
                .Where(n => !FillPattern.IsSolidKeyword(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            Dictionary<string, FillPatternElement> patternMap;
            try
            {
                patternMap = FillPattern.EnsureFillPatternsInDocument(doc, allPatternNames, sourceLibraryPath, report);
            }
            catch (Exception ex)
            {
                report.AddError($"Could not import required fill patterns: {ex.Message}");
                return report;
            }

            var defaultSolidPattern = FillPattern.GetDefaultSolidFillPatternElement(doc);

            int updated = 0;
            using (var t = new Transaction(doc, "Assign Fill Patterns To Materials"))
            {
                t.Start();

                foreach (var mat in MaterialsList)
                {
                    if (mat == null || string.IsNullOrWhiteSpace(mat.MaterialName))
                        continue;

                    var matName = mat.MaterialName.Trim();
                    if (!materialMap.TryGetValue(matName, out var rvtMaterial))
                    {
                        report.AddWarn($"Material '{matName}' was not found in document.");
                        continue;
                    }

                    var surfacePatternName = (mat.SurfacePattern ?? string.Empty).Trim();
                    var cutPatternName = (mat.CutPattern ?? string.Empty).Trim();

                    FillPatternElement surfacePattern = null;
                    FillPatternElement cutPattern = null;

                    if (FillPattern.IsSolidKeyword(surfacePatternName))
                        surfacePattern = defaultSolidPattern;
                    else
                        patternMap.TryGetValue(surfacePatternName, out surfacePattern);

                    if (FillPattern.IsSolidKeyword(cutPatternName))
                        cutPattern = defaultSolidPattern;
                    else
                        patternMap.TryGetValue(cutPatternName, out cutPattern);

                    if (FillPattern.IsSolidKeyword(surfacePatternName) && surfacePattern == null)
                        report.AddWarn($"Material '{matName}': default solid surface pattern was not found in document.");
                    else if (!string.IsNullOrWhiteSpace(surfacePatternName) && surfacePattern == null)
                        report.AddWarn($"Material '{matName}': surface pattern '{surfacePatternName}' was not found.");

                    if (FillPattern.IsSolidKeyword(cutPatternName) && cutPattern == null)
                        report.AddWarn($"Material '{matName}': default solid cut pattern was not found in document.");
                    else if (!string.IsNullOrWhiteSpace(cutPatternName) && cutPattern == null)
                        report.AddWarn($"Material '{matName}': cut pattern '{cutPatternName}' was not found.");

                    if (FillPattern.TrySetFillPatternToMaterial(rvtMaterial, surfacePattern, cutPattern))
                    {
                        updated++;
                        report.AddInfo($"Assigned patterns to '{matName}'.");
                    }
                }

                t.Commit();
            }

            report.WrittenValues = updated;
            if (updated == 0 && report.Errors.Count == 0)
                report.AddWarn("No material patterns were updated.");

            return report;
        }

        private static Material FindTemplateMaterial(List<Material> materials, string materialType, string materialClass)
        {
            string normalizedType = (materialType ?? string.Empty).Trim().ToLowerInvariant();

            string[] preferredNames;
            switch (normalizedType)
            {
                case "beton":
                    preferredNames = new[] { "Concrete", "Beton", "Concrete, Cast-in-Place gray" };
                    break;
                case "kunststoff":
                    preferredNames = new[] { "Plastic", "Kunststoff", "Plastic - White" };
                    break;
                case "metall":
                    preferredNames = new[] { "Metal", "Steel", "Aluminum" };
                    break;
                case "boden":
                case "erdbau":
                    preferredNames = new[] { "Earth", "Soil" };
                    break;
                default:
                    preferredNames = new[] { "Default", "Generic" };
                    break;
            }

            var exact = materials.FirstOrDefault(m =>
                preferredNames.Any(n => string.Equals(m.Name, n, StringComparison.OrdinalIgnoreCase)));
            if (exact != null)
                return exact;

            var contains = materials.FirstOrDefault(m =>
                preferredNames.Any(n => m.Name.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0));
            if (contains != null)
                return contains;

            return materials.FirstOrDefault(m =>
                string.Equals(m.MaterialClass, materialClass, StringComparison.OrdinalIgnoreCase));
        }

        private static void CopyVisualProperties(Material target, Material source)
        {
            target.Color = source.Color;
            target.Transparency = source.Transparency;
            target.Shininess = source.Shininess;
            target.Smoothness = source.Smoothness;
            target.UseRenderAppearanceForShading = source.UseRenderAppearanceForShading;

            target.CutForegroundPatternId = source.CutForegroundPatternId;
            target.CutForegroundPatternColor = source.CutForegroundPatternColor;
            target.CutBackgroundPatternId = source.CutBackgroundPatternId;
            target.CutBackgroundPatternColor = source.CutBackgroundPatternColor;

            target.SurfaceForegroundPatternId = source.SurfaceForegroundPatternId;
            target.SurfaceForegroundPatternColor = source.SurfaceForegroundPatternColor;
            target.SurfaceBackgroundPatternId = source.SurfaceBackgroundPatternId;
            target.SurfaceBackgroundPatternColor = source.SurfaceBackgroundPatternColor;

            if (source.AppearanceAssetId != ElementId.InvalidElementId)
                target.AppearanceAssetId = source.AppearanceAssetId;
        }

        private static string GetRevitMaterialClass(string materialType)
        {
            string type = (materialType ?? string.Empty).Trim().ToLowerInvariant();

            switch (type)
            {
                case "beton":
                    return "Concrete";
                case "metall":
                    return "Metal";
                case "kunststoff":
                    return "Plastic";
                case "boden":
                case "erdbau":
                    return "Earth";
                case "sonstiges":
                    return "Generic";
                default:
                    return "Generic";
            }
        }

      
    }
}
