using Autodesk.Revit.DB;
using MH.RevitTools.Database.Changes;
using MH.RevitTools.Database.Link;
using Revit.Addin._2027.Services;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Revit.Addin._2027.DatabaseLink.Apply
{
    /// <summary>
    /// The database's values in Revit's form, and how the model's compare with them. The pane's list and Apply
    /// both ask here, so what's listed is what gets written. Parameters are found by GUID, never by name.
    /// Numbers are converted the way the Excel route converts them
    /// (<see cref="ParamMetaData.ConvertValue(Document, ForgeTypeId, string, string)"/>), from the database's unit
    /// into Revit's, so 0.3 m and 300 mm are the same value. A value Revit can't read is a problem the pane
    /// names, never a stop. One instance per comparison or apply: it remembers elements and converted values, as
    /// many elements share them. Only reads. API context.
    /// </summary>
    internal sealed class RevitValues
    {
        // Revit keeps lengths in feet; a millionth of the value is far below anything a model shows.
        private const double Tolerance = 1e-6;

        private readonly Document _document;
        private readonly Worksharing _worksharing;
        private readonly Dictionary<long, Element> _elements = new Dictionary<long, Element>();
        private readonly Dictionary<string, Tuple<ParameterValue, string>> _wanted =
            new Dictionary<string, Tuple<ParameterValue, string>>(StringComparer.Ordinal);

        public RevitValues(Document document)
        {
            _document = document ?? throw new ArgumentNullException(nameof(document));
            _worksharing = new Worksharing(document);
        }

        /// <summary>How the element's (or material's) value compares with the database's, and whether Apply could write it.</summary>
        public ValueCheck Compare(long elementId, LinkParameter parameter, string databaseValue)
        {
            try
            {
                Element element = ElementFor(elementId);
                if (element == null || parameter.Guid == Guid.Empty)
                    return ValueCheck.NoParameter;

                Parameter target = element.get_Parameter(parameter.Guid);
                bool onType = target == null;
                if (onType)
                    target = OnItsType(element, parameter.Guid);
                if (target == null)
                    return ValueCheck.NoParameter;

                string problem;
                ParameterValue wanted = Wanted(target, parameter.Unit, databaseValue, out problem);
                if (wanted == null)
                    return ValueCheck.CantCompare(problem);
                if (IsSame(target, wanted))
                    return ValueCheck.Same;
                if (onType)
                    return ValueCheck.OnType;

                string owner;
                switch (_worksharing.AccessTo(element.Id, out owner))
                {
                    case ElementAccess.Borrowed:
                        return ValueCheck.Borrowed(owner);
                    case ElementAccess.OutOfDate:
                        return ValueCheck.OutOfDate;
                    default:
                        return target.IsReadOnly ? ValueCheck.ReadOnly : ValueCheck.Differs(Shown(target));
                }
            }
            catch (Exception ex)
            {
                // One odd value mustn't stop the whole list: it's named under "Not applied" instead.
                return ValueCheck.CantCompare("Revit couldn't read it (" + ex.Message.TrimEnd('.') + ")");
            }
        }

        /// <summary>The database's value as the parameter takes it; null, with the reason, when it doesn't fit.</summary>
        /// <param name="unit">The database's unit for it, e.g. "m"; null for none.</param>
        public ParameterValue Wanted(Parameter target, string unit, string databaseValue, out string problem)
        {
            string value = (databaseValue ?? "").Trim();
            ForgeTypeId spec = target.Definition.GetDataType();
            string key = (int)target.StorageType + "\u0001" + spec.TypeId + "\u0001" + (unit ?? "") + "\u0001" + value;
            Tuple<ParameterValue, string> wanted;
            if (!_wanted.TryGetValue(key, out wanted))
            {
                wanted = Convert(target.StorageType, spec, unit, value);
                _wanted.Add(key, wanted);
            }
            problem = wanted.Item2;
            return wanted.Item1;
        }

        /// <summary>True when the parameter already holds the value.</summary>
        public static bool IsSame(Parameter target, ParameterValue wanted)
        {
            if (wanted is StringParameterValue text)
                return string.Equals((target.AsString() ?? "").Trim(), text.Value, StringComparison.Ordinal);
            if (!target.HasValue)
                return false;
            if (wanted is IntegerParameterValue number)
                return target.AsInteger() == number.Value;
            if (wanted is DoubleParameterValue measure)
            {
                double actual = target.AsDouble();
                double scale = Math.Max(1, Math.Max(Math.Abs(actual), Math.Abs(measure.Value)));
                return Math.Abs(actual - measure.Value) <= Tolerance * scale;
            }
            return false;
        }

        private Tuple<ParameterValue, string> Convert(StorageType storage, ForgeTypeId spec, string unit, string value)
        {
            switch (storage)
            {
                case StorageType.String:
                    return Fits(new StringParameterValue(value));
                case StorageType.Integer:
                    return ConvertWholeNumber(spec, value);
                case StorageType.Double:
                    return ConvertNumber(spec, unit, value);
                case StorageType.ElementId:
                    return Misfit("Revit keeps an element here, such as a material; the link doesn't fill those");
                default:
                    return Misfit("Revit can't hold a value in it");
            }
        }

        // The database writes numbers with a dot and nothing else; a comma would otherwise be read as a thousands
        // separator ("0,3" as 3). And a measurement needs a unit Revit can convert it from: without one, the
        // Excel route's conversion falls back on whatever unit the project displays.
        private Tuple<ParameterValue, string> ConvertNumber(ForgeTypeId spec, string unit, string value)
        {
            double number;
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number)
                || double.IsNaN(number) || double.IsInfinity(number))
                return Misfit("\"" + value + "\" isn't a number");

            if (spec != SpecTypeId.Number && UnitUtils.IsMeasurableSpec(spec))
            {
                ForgeTypeId sourceUnit = ParamMetaData.SourceUnit(unit);
                if (sourceUnit == null || !UnitUtils.IsValidUnit(spec, sourceUnit))
                    return Misfit(string.IsNullOrWhiteSpace(unit)
                        ? "the database gives no unit for it"
                        : "the database's unit (" + unit.Trim() + ") doesn't fit the parameter");
            }

            var measure = ParamMetaData.ConvertValue(_document, spec, value, unit) as DoubleParameterValue;
            return measure == null ? Misfit("\"" + value + "\" doesn't fit the parameter") : Fits(measure);
        }

        private static Tuple<ParameterValue, string> ConvertWholeNumber(ForgeTypeId spec, string value)
        {
            if (spec == SpecTypeId.Boolean.YesNo)
            {
                bool? yes = ChangeFinder.YesNo(value);
                return yes.HasValue
                    ? Fits(new IntegerParameterValue(yes.Value ? 1 : 0))
                    : Misfit("\"" + value + "\" isn't Yes or No");
            }
            int number;
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number)
                ? Fits(new IntegerParameterValue(number))
                : Misfit("\"" + value + "\" isn't a whole number");
        }

        private static Tuple<ParameterValue, string> Fits(ParameterValue value) => Tuple.Create(value, (string)null);

        private static Tuple<ParameterValue, string> Misfit(string problem) => Tuple.Create((ParameterValue)null, problem);

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

        // As the Properties palette shows it, e.g. "300 mm" or "Yes"; empty without a value.
        private static string Shown(Parameter target)
        {
            if (target.StorageType == StorageType.String)
                return target.AsString() ?? "";
            if (!target.HasValue)
                return "";
            string shown = target.AsValueString();
            if (!string.IsNullOrEmpty(shown))
                return shown;
            return target.StorageType == StorageType.Double
                ? target.AsDouble().ToString("0.###", CultureInfo.InvariantCulture)
                : target.AsInteger().ToString(CultureInfo.InvariantCulture);
        }
    }
}
