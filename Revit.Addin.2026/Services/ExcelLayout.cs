namespace Revit.Addin._2026.Services
{
    /// <summary>
    /// Single source of truth for where data lives in the "Material-Bauteilliste" workbook
    /// that <see cref="ExcelParser.NewReadExcelStructured(string)"/> reads.
    ///
    /// Update these values when the Excel layout changes (e.g. when a row is inserted).
    /// All values are 1-based, matching ClosedXML cell addressing.
    /// </summary>
    public static class ExcelLayout
    {
        /// <summary>Column positions. These are identical on the family and material sheets.</summary>
        public static class Columns
        {
            public const int Bauteilnummer = 11;
            public const int MaterialName = 6;
            public const int MaterialType = 5;
            public const int FamilyStart = 7;    // first parameter column on the family sheet (skip first 6)
            public const int MaterialStart = 4;  // first parameter column on the material sheet (skip first 3)
            public const int SurfacePattern = 24;
            public const int CutPattern = 25;
        }

        /// <summary>
        /// Row positions on the FAMILY sheet ("Bauteilliste").
        /// A prefix row was inserted at row 4, shifting every row below it down by one.
        /// </summary>
        public static class FamilyRows
        {
            public const int Prefix = 3;     // per-parameter prefix -> Revit description
            public const int Header = 4;     // Parametername
            public const int Group = 6;      // Parameter Gruppe
            public const int DataType = 9;  // Datentyp
            public const int Ignore = 12;    // Dateneingabe (Modeller/SBIM)
            public const int DataStart = 14;
        }

        /// <summary>
        /// Row positions on the MATERIAL sheet ("Materialliste").
        /// This sheet did NOT get the prefix row, so the values are unchanged.
        /// </summary>
        public static class MaterialRows
        {
            public const int Header = 4;        // Parametername
            public const int Group = 6;         // Parameter Gruppe
            public const int MaterialType = 7;  // Parameter Untergruppe (material type)
            public const int DataType = 9;      // Datentyp
            public const int DataStart = 14;
        }
    }
}
