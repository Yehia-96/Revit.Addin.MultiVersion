// File: ParamMetaData.cs (split from ExcelParser.cs)
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Globalization;
using Document = Autodesk.Revit.DB.Document;

namespace Revit.Addin._2026.Services
{
    using Autodesk.Revit.DB;
    using Autodesk.Revit.UI;
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;


    // The ParamMetaData class encapsulates metadata about a parameter,
    // including its name, group, and data type.
    public class ParamMetaData
        {
        public enum ParamTarget
        {
            FamilyElement,
            Material
        }
        public string Name { get; private set; }
        public string GroupName { get; private set; }
        public string Datatype { get; private set; }
        public ParamTarget Target { get; set; }
        public string MaterialType { get; set; }
        // Per-parameter prefix read from the Excel prefix row (family sheet, row 4).
        // Wired into the Revit shared-parameter Description, which propagates to both
        // the family parameter and the project parameter created from it.
        public string Description { get; set; }

        // Unit as the workbook writes it, e.g. "[m]" or "[-]". Only the Planungsdatenbank
        // format supplies this; the Material-Bauteilliste reader leaves it null, so that
        // format keeps using the single-argument SpecMap and its behaviour is unchanged.
        //
        // It matters because the new format types every measurement as [double] and states
        // the dimension in a separate row: Dicke, Breite and Durchmesser are all [double]
        // with [m]. Mapping on datatype alone would make them unitless numbers, and Revit
        // could not treat them as lengths.
        public string Unit { get; set; }

        public ParamMetaData(string name, string group, string type)
            {
                Name = name;
                GroupName = group;
                Datatype = type;
            }

        // Properties to get the Revit ForgeTypeId for the parameter's group and data type
        // This is important because the AddParameter API method requires these ForgeTypeIds 
        // as one of the arguments 
        public ForgeTypeId GroupNameRevit
            {
                get { return GroupMap(GroupName); }
            }

            public ForgeTypeId DataTypeRevit
            {
                get { return SpecMap(Datatype, Unit); }
            }

        // Static methods to map parameter datatype to Revit ForgeTypeIds
        public static ForgeTypeId SpecMap(string name)
            {
                switch (Normalise(name))
                {
                    case "text": return SpecTypeId.String.Text;
                    case "integer":
                    case "ganzzahl":
                    return SpecTypeId.Int.Integer;
                    case "länge":
                    case "length":
                    return SpecTypeId.Length;
                    case "angle": return SpecTypeId.Angle;
                    case "area": return SpecTypeId.Area;
                    case "volume": return SpecTypeId.Volume;
                    case "boolean": return SpecTypeId.Boolean.YesNo;

                    // Planungsdatenbank vocabulary. The workbook brackets its types -
                    // "[string]", "[integer]", "[double]" - which Normalise strips, so these
                    // cases serve both spellings.
                    case "string": return SpecTypeId.String.Text;
                    case "double": return SpecTypeId.Number;
                    case "date": return SpecTypeId.String.Text;
                    case "url": return SpecTypeId.String.Url;

                    default: return SpecTypeId.String.Text;
                }
            }

        /// <summary>
        /// As <see cref="SpecMap(string)"/>, but uses the workbook's unit row to tell a
        /// measurement from a bare number.
        /// <para>
        /// Only the Planungsdatenbank format supplies a unit. Everything else passes null and
        /// lands on the single-argument behaviour unchanged.
        /// </para>
        /// </summary>
        public static ForgeTypeId SpecMap(string name, string unit)
            {
                string type = Normalise(name);
                string u = Normalise(unit);

                // A double means different things depending on the dimension stated beside it:
                // Dicke/Breite/Durchmesser are [double] with [m] and must be lengths, while an
                // unqualified [double] is just a number.
                if (type == "double")
                {
                    switch (u)
                    {
                        case "m":
                        case "mm":
                        case "cm":
                            return SpecTypeId.Length;
                        case "m²":
                        case "m2":
                            return SpecTypeId.Area;
                        case "m³":
                        case "m3":
                            return SpecTypeId.Volume;
                        case "°":
                        case "grad":
                            return SpecTypeId.Angle;
                    }
                }
                return SpecMap(name);
            }

        /// <summary>
        /// Trims, lowercases, and drops the square brackets the Planungsdatenbank workbook
        /// wraps its types and units in. "[double]" and "double" must map alike, and "[-]"
        /// must read as "no unit".
        /// </summary>
        private static string Normalise(string value)
            {
                if (string.IsNullOrWhiteSpace(value)) return string.Empty;
                string v = value.Trim().ToLowerInvariant();
                if (v.Length > 1 && v[0] == '[' && v[v.Length - 1] == ']')
                    v = v.Substring(1, v.Length - 2).Trim();
                if (v == "-") return string.Empty;
                return v;
            }

        /// <summary>
        /// True when <see cref="SpecMap(string)"/> recognises this datatype, rather than
        /// falling through to text. Lets a reader report an unmapped type instead of silently
        /// creating every parameter as a string - the quietest way this could go wrong.
        /// </summary>
        public static bool IsKnownDatatype(string name)
            {
                switch (Normalise(name))
                {
                    case "text":
                    case "integer":
                    case "ganzzahl":
                    case "länge":
                    case "length":
                    case "angle":
                    case "area":
                    case "volume":
                    case "boolean":
                    case "string":
                    case "double":
                    case "date":
                    case "url":
                        return true;
                    default:
                        return false;
                }
            }

        /// <summary>
        /// True when <see cref="GroupMap"/> recognises this group name. Unrecognised groups are
        /// cosmetic - the parameter still works, it just lands under "General" in the
        /// properties palette - so a reader should note these rather than refuse the workbook.
        /// </summary>
        public static bool IsKnownGroup(string name)
            {
                switch (Normalise(name))
                {
                    case "general":
                    case "allgemein":
                    case "text":
                    case "identity":
                    case "constraints":
                    case "graphics":
                    case "materials":
                    case "material":
                    case "dimensions":
                    case "geometrie":
                    case "spezifisch":
                    case "kategorisierung":
                    case "beschreibung":
                    case "ort":
                    case "darstellung":
                    case "sonstiges":
                    case "beton":
                    case "metall":
                    case "kunststoff":
                    case "boden":
                    case "erdbau":
                        return true;
                    default:
                        return false;
                }
            }

        // Static method to map parameter group names to Revit ForgeTypeIds
        public static ForgeTypeId GroupMap(string name)
            {
                switch (Normalise(name))
                {
                    case "general":
                    case "allgemein":
                    return GroupTypeId.General;
                    case "text": return GroupTypeId.Text;
                    case "identity": return GroupTypeId.IdentityData;
                    case "constraints": return GroupTypeId.Constraints;
                    case "graphics": return GroupTypeId.Graphics;
                    case "materials":
                    case "material":
                    return GroupTypeId.Materials;
                    case "dimensions":
                    case "geometrie":
                    return GroupTypeId.Geometry;
                    case "spezifisch": return GroupTypeId.Data;

                    // Planungsdatenbank groups. These only decide where a parameter appears in
                    // the properties palette, so a wrong choice is cosmetic and easy to change.
                    case "kategorisierung":
                    case "beschreibung":
                    return GroupTypeId.IdentityData;
                    case "ort": return GroupTypeId.Constraints;
                    case "darstellung": return GroupTypeId.Graphics;
                    case "sonstiges": return GroupTypeId.General;
                    // Material families: Beton, Metall, Kunststoff, Boden, Erdbau all describe
                    // what a material is, so they belong together under Materials.
                    case "beton":
                    case "metall":
                    case "kunststoff":
                    case "boden":
                    case "erdbau":
                    return GroupTypeId.Materials;

                default:
                       return GroupTypeId.General;
                }
            }
        // Static method to convert a raw string value to a ParameterValue
        // based on the specified ForgeTypeId. 
        // This is important because we add the parameter values using the SetMultiple method
        // Which requires an IList of Tuple of Parameter and ParameterValue
        // (IList<Tuple<Parameter, ParameterValue>>)
        /// <summary>
        /// As <see cref="ConvertValue(Document, ForgeTypeId, string)"/>, but for a measurement
        /// whose unit the source states explicitly.
        /// <para>
        /// The three-argument form reads a length in the <em>project's display unit</em> - right
        /// for the Material-Bauteilliste, which states no unit, so values were always typed in
        /// whatever the project shows. The Planungsdatenbank writes <c>[m]</c> beside every
        /// length, so a project displaying millimetres would otherwise read a 0.5 m thickness as
        /// 0.5 mm. When the unit is known, this converts from it instead.
        /// </para>
        /// <para>
        /// A null or unrecognised unit falls through to the three-argument behaviour unchanged,
        /// which is what every Material-Bauteilliste value gets.
        /// </para>
        /// </summary>
        public static ParameterValue ConvertValue(Document doc, ForgeTypeId spec, string raw, string unit)
            {
                ForgeTypeId sourceUnit = SourceUnit(unit);
                if (sourceUnit == null || string.IsNullOrWhiteSpace(raw))
                    return ConvertValue(doc, spec, raw);

                bool measured = spec == SpecTypeId.Length || spec == SpecTypeId.Area ||
                                spec == SpecTypeId.Volume || spec == SpecTypeId.Angle;
                if (!measured)
                    return ConvertValue(doc, spec, raw);

                double value;
                if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                    return new StringParameterValue(raw);

                return new DoubleParameterValue(UnitUtils.ConvertToInternalUnits(value, sourceUnit));
            }

        /// <summary>Revit unit for a workbook unit such as "[m]", or null when it is not one we convert from.</summary>
        private static ForgeTypeId SourceUnit(string unit)
            {
                switch (Normalise(unit))
                {
                    case "m": return UnitTypeId.Meters;
                    case "cm": return UnitTypeId.Centimeters;
                    case "mm": return UnitTypeId.Millimeters;
                    case "m²":
                    case "m2": return UnitTypeId.SquareMeters;
                    case "m³":
                    case "m3": return UnitTypeId.CubicMeters;
                    case "°":
                    case "grad": return UnitTypeId.Degrees;
                    default: return null;
                }
            }

        public static ParameterValue ConvertValue(Document doc, ForgeTypeId spec, string raw)
            {
                if (string.IsNullOrWhiteSpace(raw))
                {
                    if (spec == SpecTypeId.Boolean.YesNo) return new IntegerParameterValue(0);
                    if (spec == SpecTypeId.Int.Integer) return new IntegerParameterValue(0);
                    return new StringParameterValue("");
                }

                try
                {
                    if (spec == SpecTypeId.String.Text)
                    {
                        return new StringParameterValue(raw);
                    }

                    if (spec == SpecTypeId.Int.Integer)
                    {
                        return new IntegerParameterValue(int.Parse(raw));
                    }

                    if (spec == SpecTypeId.Boolean.YesNo)
                    {
                        bool isTrue = raw == "1" ||
                                      raw.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                                      raw.Equals("true", StringComparison.OrdinalIgnoreCase);
                        return new IntegerParameterValue(isTrue ? 1 : 0);
                    }

                    if (spec == SpecTypeId.Length ||
                        spec == SpecTypeId.Area ||
                        spec == SpecTypeId.Volume ||
                        spec == SpecTypeId.Angle)
                    {
                        double displayVal = double.Parse(raw, CultureInfo.InvariantCulture);
                        Units units = doc.GetUnits();
                        FormatOptions fmt = units.GetFormatOptions(spec);
                        ForgeTypeId unitId = fmt.GetUnitTypeId();
                        double internalVal = UnitUtils.ConvertToInternalUnits(displayVal, unitId);
                        return new DoubleParameterValue(internalVal);
                    }

                    // A unitless number. Only the Planungsdatenbank mapping produces this spec;
                    // without this branch it fell through to a string value, which Revit refuses
                    // on a numeric parameter.
                    if (spec == SpecTypeId.Number)
                    {
                        return new DoubleParameterValue(double.Parse(raw, CultureInfo.InvariantCulture));
                    }

                    return new StringParameterValue(raw);
                }
                catch
                {
                    return new StringParameterValue(raw); // Fallback on any error
                }
            }

        /// <summary>
        /// The converted value as this parameter can take it, or null when it can't.
        /// <para>
        /// The model's parameter can be of another kind than the workbook's: a Bauteilnummer that a family
        /// has as a text, or Revit's own "Typ". A value that doesn't fit isn't written, or SetMultiple throws
        /// and none of the run's values is.
        /// </para>
        /// </summary>
        public static ParameterValue Fitting(Parameter parameter, ParameterValue value, string raw)
            {
                switch (parameter.StorageType)
                {
                    case StorageType.String:
                        return value is StringParameterValue ? value : new StringParameterValue(raw ?? "");
                    case StorageType.Integer:
                        int number;
                        if (value is IntegerParameterValue) return value;
                        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out number)
                            ? new IntegerParameterValue(number)
                            : null;
                    case StorageType.Double:
                        return value as DoubleParameterValue;
                    case StorageType.ElementId:
                        // The workbook has no element ids. Materials are assigned on their own.
                        return value as ElementIdParameterValue;
                    default:
                        return null;
                }
            }

        // Static method to retrieve all shared parameters from a DefinitionFile
        public static List<string> GetAllSharedParameters(DefinitionFile defFile)
            {
                var result = new List<string>();

                if (defFile == null)
                {
                    TaskDialog.Show("Error", "Shared parameter file is not set or could not be opened.");
                    return null;
                }

                foreach (DefinitionGroup group in defFile.Groups)
                {
                    foreach (Definition def in group.Definitions)
                    {
                        result.Add(def.Name);
                    }
                }

                return result;
            }
        // Static method to check if a parameter's value matches a given string value
        public static bool AreValuesEqual(Parameter param, string dataValue)
        {
            if (param == null || dataValue == null)
                return false;

            switch (param.StorageType)
            {
                case StorageType.String:
                    return string.Equals(param.AsString(), dataValue, StringComparison.OrdinalIgnoreCase);

                case StorageType.Integer:
                    if (int.TryParse(dataValue, out int i))
                        return param.AsInteger() == i;
                    break;

                case StorageType.Double:
                    if (double.TryParse(dataValue, out double d))
                    {
                        double actual = param.AsDouble();
                        // compare with small tolerance
                        return Math.Abs(actual - d) < 0.001;
                    }
                    break;

                case StorageType.ElementId:
                    var actualId = param.AsElementId();
                    if (int.TryParse(dataValue, out int idVal))
                        return actualId.Value == idVal; // Revit 2026: ElementId.IntegerValue removed -> Value (long)

                    // Or if matching by name (e.g., Material)
                    Element matElem = param.Element?.Document?.GetElement(actualId);
                    return string.Equals(matElem?.Name, dataValue, StringComparison.OrdinalIgnoreCase);

                case StorageType.None:
                default:
                    return false;
            }

            return false;
        }
        // Static method to check if a parameter is a built-in parameter
        public static bool IsBuiltInParam(string name)
        {
            return Enum.GetValues(typeof(BuiltInParameter))
                .Cast<BuiltInParameter>()
                .Any(bip => LabelUtils.GetLabelFor(bip)
                    .Equals(name, StringComparison.OrdinalIgnoreCase));
        }
    }
    // The SimpleLoad class implements the IFamilyLoadOptions interface to handle family loading options.
    // This is used when loading families into a Revit document.
    class SimpleLoad : IFamilyLoadOptions
    {
        public bool OnFamilyFound(bool familyInUse, out bool overwrite)
        {
            overwrite = true;
            return true;
        }

        public bool OnSharedFamilyFound(Family shared, bool familyInUse, out FamilySource source, out bool overwrite)
        {
            source = FamilySource.Project;
            overwrite = true;
            return true;
        }
    }
    public class SimpleFamilyLoad : IFamilyLoadOptions
    {
        public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
        {
            overwriteParameterValues = true;
            return true;
        }

        public bool OnSharedFamilyFound(
            Family sharedFamily,
            bool familyInUse,
            out FamilySource source,
            out bool overwriteParameterValues)
        {
            source = FamilySource.Family;
            overwriteParameterValues = true;
            return true;
        }
    }
}

    
