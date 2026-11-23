using Autodesk.Revit.DB;
using Revit.Addin._2027.Helpers;
using Revit.Addin._2027.Services;
using System;

namespace Revit.Addin._2027.DatabaseLink.Apply
{
    /// <summary>
    /// The database link's rules for the shared writing loop: parameters by GUID, on the element itself only,
    /// and the same comparison and conversion as the pane's list (<see cref="RevitValues"/>).
    /// </summary>
    internal sealed class DatabaseValueRules : IValueRules
    {
        private readonly RevitValues _values;

        public DatabaseValueRules(RevitValues values)
        {
            _values = values ?? throw new ArgumentNullException(nameof(values));
        }

        // The Excel route's ignore row belongs to the workbook it last read, never to the database.
        public bool IsIgnored(string parameterName) => false;

        public Parameter Find(Element element, string parameterName, ParamMetaData definition) =>
            definition.SharedGuid.HasValue ? element.get_Parameter(definition.SharedGuid.Value) : null;

        public bool NeedsValue(Parameter parameter, string value, ParamMetaData definition)
        {
            string problem;
            ParameterValue wanted = _values.Wanted(parameter, definition.Unit, value, out problem);
            return wanted != null && !RevitValues.IsSame(parameter, wanted);
        }

        public ParameterValue Convert(Document document, Parameter parameter, string value, ParamMetaData definition)
        {
            string problem;
            return _values.Wanted(parameter, definition.Unit, value, out problem);
        }
    }
}
