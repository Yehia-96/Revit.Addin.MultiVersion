using Autodesk.Revit.DB;
using MH.RevitTools.Database.Changes;
using MH.RevitTools.Database.Link;
using Revit.Addin._2027.Services;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Revit.Addin._2027.DatabaseLink.Pane
{
    /// <summary>
    /// Compares one database value with the model's: the element's or material's parameter with the
    /// database parameter's GUID, never its name; on the element itself, else on its type. Numbers are
    /// converted the way the Excel route converts them (<see cref="ParamMetaData.ConvertValue(Document, ForgeTypeId, string, string)"/>),
    /// from the database's unit into Revit's, so 0.3 m and 300 mm are the same value. A value Revit can't
    /// read is a problem the pane names, never a stop. One instance per comparison: it remembers elements
    /// and converted values, as many elements share them. Only reads. API context.
    /// </summary>
    internal sealed class RevitValueCompare
    {
        // Revit keeps lengths in feet; a millionth of the value is far below anything a model shows.
        private const double Tolerance = 1e-6;

        private readonly Document _document;
        private readonly Dictionary<long, Element> _elements = new Dictionary<long, Element>();
        private readonly Dictionary<string, double?> _numbers = new Dictionary<string, double?>(StringComparer.Ordinal);

        public RevitValueCompare(Document document)
        {
            _document = document ?? throw new ArgumentNullException(nameof(document));
        }

        public ValueCheck Compare(long elementId, LinkParameter parameter, string databaseValue)
        {
            try
            {
                Element element = ElementFor(elementId);
                if (element == null || parameter.Guid == Guid.Empty)
                    return ValueCheck.NoParameter;
                Parameter target = element.get_Parameter(parameter.Guid) ?? OnItsType(element, parameter.Guid);
                if (target == null)
                    return ValueCheck.NoParameter;

                string value = (databaseValue ?? "").Trim();
                switch (target.StorageType)
                {
                    case StorageType.String:
                        string text = target.AsString() ?? "";
                        return string.Equals(text.Trim(), value, StringComparison.Ordinal) ? ValueCheck.Same : ValueCheck.Differs(text);
                    case StorageType.Integer:
                        return CompareWholeNumber(target, value);
                    case StorageType.Double:
                        return CompareNumber(target, parameter, value);
                    case StorageType.ElementId:
                        return ValueCheck.CantCompare("Revit keeps an element here, such as a material; the link doesn't fill those");
                    default:
                        return ValueCheck.CantCompare("Revit can't hold a value in it");
                }
            }
            catch (Exception ex)
            {
                // One odd value mustn't stop the whole list: it's named under the changes instead.
                return ValueCheck.CantCompare("Revit couldn't read it (" + ex.Message.TrimEnd('.') + ")");
            }
        }

        private Element ElementFor(long id)
        {
            Element element;
            if (!_elements.TryGetValue(id, out element))
            {
                element = _document.GetElement(new ElementId(id));
                _elements.Add(id, element);
            }
            return element;
        }

        // A family can carry the parameter on its type rather than on each element.
        private Parameter OnItsType(Element element, Guid guid)
        {
            ElementId typeId = element.GetTypeId();
            if (typeId == null || typeId == ElementId.InvalidElementId)
                return null;
            Element type = ElementFor(typeId.Value);
            return type == null ? null : type.get_Parameter(guid);
        }

        private static ValueCheck CompareWholeNumber(Parameter target, string value)
        {
            int wanted;
            if (target.Definition.GetDataType() == SpecTypeId.Boolean.YesNo)
            {
                bool? yes = ChangeFinder.YesNo(value);
                if (!yes.HasValue)
                    return ValueCheck.CantCompare("\"" + value + "\" isn't Yes or No");
                wanted = yes.Value ? 1 : 0;
            }
            else if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out wanted))
            {
                return ValueCheck.CantCompare("\"" + value + "\" isn't a whole number");
            }

            if (target.HasValue && target.AsInteger() == wanted)
                return ValueCheck.Same;
            return ValueCheck.Differs(target.HasValue ? Shown(target) : "");
        }

        private ValueCheck CompareNumber(Parameter target, LinkParameter parameter, string value)
        {
            double? wanted = InRevitUnits(target.Definition.GetDataType(), parameter.Unit, value);
            if (!wanted.HasValue)
                return ValueCheck.CantCompare("\"" + value + "\" isn't a number");
            if (!target.HasValue)
                return ValueCheck.Differs("");

            double actual = target.AsDouble();
            double scale = Math.Max(1, Math.Max(Math.Abs(actual), Math.Abs(wanted.Value)));
            return Math.Abs(actual - wanted.Value) <= Tolerance * scale ? ValueCheck.Same : ValueCheck.Differs(Shown(target));
        }

        // The database's number in Revit's own units for that kind of parameter; null when it isn't a number.
        private double? InRevitUnits(ForgeTypeId spec, string unit, string value)
        {
            string key = spec.TypeId + "\u0001" + (unit ?? "") + "\u0001" + value;
            double? number;
            if (!_numbers.TryGetValue(key, out number))
            {
                var converted = ParamMetaData.ConvertValue(_document, spec, value, unit) as DoubleParameterValue;
                number = converted == null ? (double?)null : converted.Value;
                _numbers.Add(key, number);
            }
            return number;
        }

        // As the Properties palette shows it, e.g. "300 mm" or "Yes".
        private static string Shown(Parameter target)
        {
            string shown = target.AsValueString();
            if (!string.IsNullOrEmpty(shown))
                return shown;
            return target.StorageType == StorageType.Double
                ? target.AsDouble().ToString("0.###", CultureInfo.InvariantCulture)
                : target.AsInteger().ToString(CultureInfo.InvariantCulture);
        }
    }
}
