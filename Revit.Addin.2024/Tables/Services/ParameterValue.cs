using Autodesk.Revit.DB;
using System.Globalization;
namespace Revit.Addin._2024.Tables.Services
{
    public static class ParameterValueReader
    {
        public static string GetParameterValue(Element element, string parameterName)
        {
            if (element == null || string.IsNullOrWhiteSpace(parameterName))
                return string.Empty;

            Parameter parameter = element.LookupParameter(parameterName);

            if (parameter == null || !parameter.HasValue)
                return string.Empty;

            switch (parameter.StorageType)
            {
                case StorageType.String:
                    return parameter.AsString() ?? string.Empty;

                case StorageType.Integer:
                    return parameter.AsInteger().ToString(CultureInfo.InvariantCulture);

                case StorageType.Double:
                    string formattedDouble = parameter.AsValueString();

                    if (!string.IsNullOrWhiteSpace(formattedDouble))
                        return formattedDouble;

                    return parameter.AsDouble().ToString(CultureInfo.InvariantCulture);

                case StorageType.ElementId:
                    return GetElementIdValue(element.Document, parameter);

                case StorageType.None:
                default:
                    return string.Empty;
            }
        }

        private static string GetElementIdValue(Document doc, Parameter parameter)
        {
            if (doc == null || parameter == null)
                return string.Empty;

            ElementId id = parameter.AsElementId();

            if (id == ElementId.InvalidElementId)
                return string.Empty;

            Element referencedElement = doc.GetElement(id);

            if (referencedElement != null)
                return referencedElement.Name;

            // ElementId got a 64-bit Value in Revit 2024; earlier versions only expose
            // the 32-bit IntegerValue (which became obsolete in 2024). The DefineConstants
            // in the .csproj pick the right branch per build configuration.
#if REVIT2024
            return id.Value.ToString(CultureInfo.InvariantCulture);
#else
            return id.IntegerValue.ToString(CultureInfo.InvariantCulture);
#endif
        }
    }
}