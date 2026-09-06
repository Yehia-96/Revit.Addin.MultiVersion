// File: ParamMetaData.cs (split from ExcelParser.cs)
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Globalization;
using Document = Autodesk.Revit.DB.Document;

namespace Revit.Addin._2024.Services
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
                get { return SpecMap(Datatype); }
            }

        // Static methods to map parameter datatype to Revit ForgeTypeIds
        public static ForgeTypeId SpecMap(string name)
            {
                switch (name.ToLowerInvariant())
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
                    default: return SpecTypeId.String.Text;
                }
            }

        // Static method to map parameter group names to Revit ForgeTypeIds
        public static ForgeTypeId GroupMap(string name)
            {
                switch (name.ToLowerInvariant())
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


                default:
                       return GroupTypeId.General;
                }
            }
        // Static method to convert a raw string value to a ParameterValue
        // based on the specified ForgeTypeId. 
        // This is important because we add the parameter values using the SetMultiple method
        // Which requires an IList of Tuple of Parameter and ParameterValue
        // (IList<Tuple<Parameter, ParameterValue>>)
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

                    return new StringParameterValue(raw);
                }
                catch
                {
                    return new StringParameterValue(raw); // Fallback on any error
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
                        return actualId.IntegerValue == idVal;

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

    
