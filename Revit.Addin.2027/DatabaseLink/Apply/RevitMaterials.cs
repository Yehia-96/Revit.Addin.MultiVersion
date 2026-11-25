using Autodesk.Revit.DB;
using MH.RevitTools.Database.Changes;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit.Addin._2027.DatabaseLink.Apply
{
    /// <summary>
    /// What the model's elements are made of, as the Excel import sees it: the element's own parameter "Material", and the
    /// model's materials by name. The pane's list and Apply both ask here, so what's listed is what gets set. It never
    /// creates a material or a parameter, and leaves parameters of types alone. One instance per comparison or apply. API
    /// context.
    /// </summary>
    internal sealed class RevitMaterials
    {
        private readonly Document _document;
        private readonly Worksharing _worksharing;
        // Many elements share a material.
        private readonly Dictionary<string, Material> _found = new Dictionary<string, Material>(StringComparer.Ordinal);
        private List<Material> _all;

        public RevitMaterials(Document document)
        {
            _document = document ?? throw new ArgumentNullException(nameof(document));
            _worksharing = new Worksharing(document);
        }

        /// <summary>How the element's material compares with the database's, and whether Apply could set it.</summary>
        public ValueCheck Compare(long elementId, string materialName)
        {
            try
            {
                Element element = _document.GetElement(new ElementId(elementId));
                if (element == null)
                    return ValueCheck.NoParameter;
                Parameter target = ParameterOf(element);
                bool onType = target == null;
                if (onType)
                    target = ParameterOf(_document.GetElement(element.GetTypeId()));
                if (target == null)
                    return ValueCheck.NoParameter;

                if (Has(target, materialName))
                    return ValueCheck.Same;
                if (onType)
                    return ValueCheck.OnType;
                if (target.IsReadOnly)
                    return ValueCheck.ReadOnly;
                if (Find(materialName) == null)
                    return ValueCheck.NoMaterial;

                string owner;
                switch (_worksharing.AccessTo(element.Id, out owner))
                {
                    case ElementAccess.Borrowed:
                        return ValueCheck.Borrowed(owner);
                    case ElementAccess.OutOfDate:
                        return ValueCheck.OutOfDate;
                    default:
                        return ValueCheck.Differs(Shown(target));
                }
            }
            catch (Exception ex)
            {
                // One odd element mustn't stop the whole list: it's named under "Not applied" instead.
                return ValueCheck.CantCompare("Revit couldn't read it (" + ex.Message.TrimEnd('.') + ")");
            }
        }

        /// <summary>The element's own parameter for its material; null when it has none. A type's is found the same way.</summary>
        public static Parameter ParameterOf(Element element) =>
            element == null ? null : element.GetParameters(ObjectMaterial.ParameterName).FirstOrDefault(TakesAMaterial);

        /// <summary>True when it already names the material.</summary>
        public bool Has(Parameter target, string materialName)
        {
            Material current = Current(target);
            return current != null && IsNamed(current, materialName);
        }

        /// <summary>The model's material of that name, whatever its case; null when there's none.</summary>
        public Material Find(string name)
        {
            string key = (name ?? "").Trim();
            Material found;
            if (!_found.TryGetValue(key, out found))
            {
                List<Material> named = All().Where(material => IsNamed(material, key)).ToList();
                found = named.FirstOrDefault(material => string.Equals(material.Name.Trim(), key, StringComparison.Ordinal)) ?? named.FirstOrDefault();
                _found.Add(key, found);
            }
            return found;
        }

        // A family can have a text of the same name.
        private static bool TakesAMaterial(Parameter parameter) =>
            parameter.StorageType == StorageType.ElementId && parameter.Definition.GetDataType() == SpecTypeId.Reference.Material;

        private Material Current(Parameter target)
        {
            ElementId id = target.AsElementId();
            return id == null || id == ElementId.InvalidElementId ? null : _document.GetElement(id) as Material;
        }

        // As the Properties palette shows it: the material's name, or "<By Category>".
        private string Shown(Parameter target)
        {
            Material current = Current(target);
            return current == null ? target.AsValueString() ?? "" : current.Name;
        }

        private static bool IsNamed(Material material, string name) =>
            string.Equals((material.Name ?? "").Trim(), (name ?? "").Trim(), StringComparison.OrdinalIgnoreCase);

        private List<Material> All()
        {
            if (_all == null)
            {
                using (var collector = new FilteredElementCollector(_document))
                    _all = collector.OfClass(typeof(Material)).Cast<Material>().ToList();
            }
            return _all;
        }
    }
}
