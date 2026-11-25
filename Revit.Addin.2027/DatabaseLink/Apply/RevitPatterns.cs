using Autodesk.Revit.DB;
using MH.RevitTools.Database.Changes;
using System;
using System.Collections.Generic;
using System.Linq;
using ExcelPatterns = Revit.Addin._2027.Services.FillPattern;

namespace Revit.Addin._2027.DatabaseLink.Apply
{
    /// <summary>
    /// The model's fill patterns, found by name as the Excel import finds them ("Solid" is the solid fill), and how the
    /// ones its materials have compare with the database's. Only patterns the model has: none is loaded from another
    /// file. The pane's list, Apply and Create all ask here, so what's listed is what gets set. One instance per
    /// comparison, apply or create. API context.
    /// </summary>
    internal sealed class RevitPatterns
    {
        private readonly Document _document;
        private readonly Worksharing _worksharing;
        private List<FillPatternElement> _all;

        public RevitPatterns(Document document)
        {
            _document = document ?? throw new ArgumentNullException(nameof(document));
            _worksharing = new Worksharing(document);
        }

        /// <summary>How the material's pattern compares with the database's, and whether Apply could set it.</summary>
        public ValueCheck Compare(long materialId, MaterialPattern which, string name)
        {
            try
            {
                var material = _document.GetElement(new ElementId(materialId)) as Material;
                if (material == null)
                    return ValueCheck.CantCompare("Revit doesn't have the material any more");
                FillPatternElement current = Current(material, which);
                if (Is(current, name))
                    return ValueCheck.Same;
                if (Find(which, name) == null)
                    return ValueCheck.NoPattern;

                string owner;
                switch (_worksharing.AccessTo(material.Id, out owner))
                {
                    case ElementAccess.Borrowed:
                        return ValueCheck.Borrowed(owner);
                    case ElementAccess.OutOfDate:
                        return ValueCheck.OutOfDate;
                    default:
                        return ValueCheck.Differs(current == null ? "" : current.Name);
                }
            }
            catch (Exception ex)
            {
                // One odd pattern mustn't stop the whole list: it's named under "Not applied" instead.
                return ValueCheck.CantCompare("Revit couldn't read it (" + ex.Message.TrimEnd('.') + ")");
            }
        }

        /// <summary>True when the material already has it.</summary>
        public bool Has(Material material, MaterialPattern which, string name) => Is(Current(material, which), name);

        /// <summary>The model's pattern of that name; null when there's none that can be used there.</summary>
        public FillPatternElement Find(MaterialPattern which, string name)
        {
            if (ExcelPatterns.IsSolidKeyword(name))
                return ExcelPatterns.GetDefaultSolidFillPatternElement(_document);
            // The cut of a material takes drafting patterns only, and a model pattern can have the same name.
            return All().FirstOrDefault(pattern => IsNamed(pattern, name)
                && (which == MaterialPattern.Surface || pattern.GetFillPattern().Target == FillPatternTarget.Drafting));
        }

        /// <summary>Gives the material the pattern; false when it had it already. Inside a transaction.</summary>
        public static bool Set(Material material, MaterialPattern which, FillPatternElement pattern) =>
            ExcelPatterns.TrySetFillPatternToMaterial(
                material, which == MaterialPattern.Surface ? pattern : null, which == MaterialPattern.Cut ? pattern : null);

        private FillPatternElement Current(Material material, MaterialPattern which)
        {
            ElementId id = which == MaterialPattern.Surface ? material.SurfaceForegroundPatternId : material.CutForegroundPatternId;
            return id == null || id == ElementId.InvalidElementId ? null : _document.GetElement(id) as FillPatternElement;
        }

        private static bool Is(FillPatternElement pattern, string name)
        {
            if (pattern == null)
                return false;
            return ExcelPatterns.IsSolidKeyword(name) ? pattern.GetFillPattern().IsSolidFill : IsNamed(pattern, name);
        }

        private static bool IsNamed(FillPatternElement pattern, string name) =>
            string.Equals((pattern.Name ?? "").Trim(), (name ?? "").Trim(), StringComparison.OrdinalIgnoreCase);

        private List<FillPatternElement> All()
        {
            if (_all == null)
            {
                using (var collector = new FilteredElementCollector(_document))
                    _all = collector.OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>().ToList();
            }
            return _all;
        }
    }
}
