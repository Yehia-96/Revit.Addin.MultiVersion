using Autodesk.Revit.DB;
using MH.RevitTools.Database.Comparing;
using Revit.Addin._2027.Services;
using System;

namespace Revit.Addin._2027.DatabaseLink.Compare
{
    /// <summary>
    /// What Revit makes of a value: the conversion the Excel import uses, data type and unit included
    /// (<see cref="ParamMetaData"/>), applied to the Excel's values and the database's alike. API context.
    /// </summary>
    internal sealed class LoadedValues
    {
        private readonly Document _document;

        /// <param name="document">Its units stand in for a measurement without one, as in the Excel import.</param>
        public LoadedValues(Document document)
        {
            _document = document ?? throw new ArgumentNullException(nameof(document));
        }

        public LoadedValue Convert(string dataType, string unit, string raw)
        {
            string text = (raw ?? "").Trim();
            ParameterValue value = ParamMetaData.ConvertValue(_document, ParamMetaData.SpecMap(dataType, unit), text, unit);
            double? number = null;
            if (value is DoubleParameterValue measure)
                number = measure.Value;
            else if (value is IntegerParameterValue whole)
                number = whole.Value;
            return new LoadedValue(Shown(text, unit), text, number);
        }

        /// <summary>E.g. "Length" for a double in metres, "Text" for a string.</summary>
        public static string KindOf(string dataType, string unit)
        {
            ForgeTypeId spec = ParamMetaData.SpecMap(dataType, unit);
            if (spec == SpecTypeId.Length)
                return "Length";
            if (spec == SpecTypeId.Area)
                return "Area";
            if (spec == SpecTypeId.Volume)
                return "Volume";
            if (spec == SpecTypeId.Angle)
                return "Angle";
            if (spec == SpecTypeId.Number)
                return "Number";
            if (spec == SpecTypeId.Int.Integer)
                return "Integer";
            if (spec == SpecTypeId.Boolean.YesNo)
                return "Yes/No";
            return spec == SpecTypeId.String.Url ? "URL" : "Text";
        }

        // The workbook writes units in brackets, and [-] for none.
        private static string Shown(string text, string unit)
        {
            string plain = (unit ?? "").Trim().Trim('[', ']').Trim();
            return plain.Length == 0 || plain == "-" ? text : text + " " + plain;
        }
    }
}
