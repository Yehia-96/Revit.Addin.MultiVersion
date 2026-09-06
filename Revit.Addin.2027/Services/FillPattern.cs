using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;

namespace Revit.Addin._2027.Services
{
    public static class FillPattern
    {
        public static string libraryPath = @"C:\Yehia\Models_For_Testing\2024\Daniel\MH_Bereich Deckel_d.zens_gelöst.0001.rvt";

        public static Dictionary<string, FillPatternElement> GetFillPatternMap(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(FillPatternElement))
                .Cast<FillPatternElement>()
                .Where(p => p != null && !string.IsNullOrWhiteSpace(p.Name))
                .GroupBy(p => p.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        }

        public static FillPatternElement GetFillPatternElement(Document doc, string patternName)
        {
            if (string.IsNullOrWhiteSpace(patternName))
                return null;

            var key = patternName.Trim();
            return new FilteredElementCollector(doc)
                .OfClass(typeof(FillPatternElement))
                .Cast<FillPatternElement>()
                .FirstOrDefault(p => string.Equals(p.Name, key, StringComparison.OrdinalIgnoreCase));
        }

        public static Dictionary<string, FillPatternElement> EnsureFillPatternsInDocument(
            Document targetDoc,
            IEnumerable<string> patternNames,
            string sourceLibraryPath,
            ImportReport report = null)
        {
            var result = new Dictionary<string, FillPatternElement>(StringComparer.OrdinalIgnoreCase);

            var normalizedNames = (patternNames ?? Enumerable.Empty<string>())
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (normalizedNames.Count == 0)
                return result;

            var existingPatterns = GetFillPatternMap(targetDoc);
            foreach (var name in normalizedNames)
            {
                if (existingPatterns.TryGetValue(name, out var existing))
                    result[name] = existing;
            }

            var missing = normalizedNames
                .Where(name => !result.ContainsKey(name))
                .ToList();

            if (missing.Count == 0)
                return result;

            if (!File.Exists(sourceLibraryPath))
                throw new FileNotFoundException($"The specified fill pattern library path does not exist: {sourceLibraryPath}", nameof(sourceLibraryPath));

            var app = targetDoc.Application;
            var sourceDoc = app.OpenDocumentFile(sourceLibraryPath);

            try
            {
                var sourcePatterns = GetFillPatternMap(sourceDoc);
                var idsToCopy = missing
                    .Where(name => sourcePatterns.ContainsKey(name))
                    .Select(name => sourcePatterns[name].Id)
                    .Distinct()
                    .ToList();

                var unresolved = missing.Where(name => !sourcePatterns.ContainsKey(name)).ToList();
                foreach (var notFound in unresolved)
                    report?.AddWarn($"Pattern '{notFound}' not found in library file.");

                if (idsToCopy.Count > 0)
                {
                    using (var t = new Transaction(targetDoc, "Import Missing Fill Patterns"))
                    {
                        t.Start();
                        ElementTransformUtils.CopyElements(sourceDoc, idsToCopy, targetDoc, Transform.Identity, new CopyPasteOptions());
                        t.Commit();
                    }
                }

                var updatedMap = GetFillPatternMap(targetDoc);
                foreach (var name in normalizedNames)
                {
                    if (updatedMap.TryGetValue(name, out var fp))
                        result[name] = fp;
                }
            }
            finally
            {
                sourceDoc.Close(false);
            }

            return result;
        }


        public static bool IsSolidKeyword(string patternName)
        {
            if (string.IsNullOrWhiteSpace(patternName))
                return false;

            var key = patternName.Trim();
            return key.Equals("Solid", StringComparison.OrdinalIgnoreCase)
                || key.Equals("Solid Fill", StringComparison.OrdinalIgnoreCase);
        }

        public static FillPatternElement GetDefaultSolidFillPatternElement(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(FillPatternElement))
                .Cast<FillPatternElement>()
                .FirstOrDefault(p => p?.GetFillPattern() != null && p.GetFillPattern().IsSolidFill);
        }

        public static bool TrySetFillPatternToMaterial(
            Material material,
            FillPatternElement surfacePattern,
            FillPatternElement cutPattern)
        {
            if (material == null)
                return false;

            bool changed = false;

            if (surfacePattern != null && material.SurfaceForegroundPatternId != surfacePattern.Id)
            {
                material.SurfaceForegroundPatternId = surfacePattern.Id;
                changed = true;
            }

            if (cutPattern != null && material.CutForegroundPatternId != cutPattern.Id)
            {
                material.CutForegroundPatternId = cutPattern.Id;
                changed = true;
            }

            return changed;
        }
    }
}
