// Shared source: keep identical in Revit.Addin.2024 / .2026 / .2027.
// C# 7.3 / net48 floor - see PlanungsdatenbankLayout.cs for the full restriction list.
using System;
using System.IO;
using ClosedXML.Excel;

namespace Revit.Addin._2027.Services.Planungsdatenbank
{
    /// <summary>
    /// Which workbook layout a file uses. Public because the public element applier takes it to
    /// decide how elements are matched.
    /// </summary>
    public enum ExcelFormat
    {
        /// <summary>Could not be determined - the caller should say so rather than guess.</summary>
        Unknown = 0,

        /// <summary>
        /// The Material-Bauteilliste workbook the add-in has always read, handled by
        /// <c>ExcelParser.NewReadExcelStructured</c>. Untouched by this feature.
        /// </summary>
        Bauteilliste = 1,

        /// <summary>The 21-sheet Planungsdatenbank workbook.</summary>
        Planungsdatenbank = 2
    }

    /// <summary>
    /// Works out which layout a workbook uses, so the user does not have to tell us.
    /// <para>
    /// Detection is by sheet <em>name</em> and nothing else - no row counts, no cell probing.
    /// A Planungsdatenbank workbook has an <c>Objektdatenbank</c> sheet; the Bauteilliste has
    /// not. That single fact is enough, and it stays true as the workbook grows columns.
    /// </para>
    /// </summary>
    internal static class ExcelFormatDetector
    {
        /// <summary>
        /// Inspects the workbook and returns its format. Opening failures are the caller's to
        /// report, so they propagate rather than collapsing into <see cref="ExcelFormat.Unknown"/>
        /// - "could not detect" and "could not open" need different messages to the user.
        /// </summary>
        internal static ExcelFormat Detect(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path is empty.", "path");
            if (!File.Exists(path)) throw new FileNotFoundException("Workbook not found.", path);

            using (var workbook = new XLWorkbook(path))
            {
                return Detect(workbook);
            }
        }

        /// <summary>
        /// As <see cref="Detect(string)"/>, for a workbook that is already open - so a caller
        /// that goes on to read it does not pay to open the file twice.
        /// </summary>
        internal static ExcelFormat Detect(XLWorkbook workbook)
        {
            if (workbook == null) throw new ArgumentNullException("workbook");

            if (SheetGrid.HasSheet(workbook, PlanungsdatenbankLayout.Sheets.Objects))
                return ExcelFormat.Planungsdatenbank;

            // Anything else is assumed to be the old layout. Deliberately a fallback and not a
            // positive test: the Bauteilliste has no distinguishing sheet name to match on, and
            // the old reader has always accepted whatever it was given. Treating "not the new
            // one" as "the old one" preserves that exactly, so no previously working file starts
            // being refused.
            return ExcelFormat.Bauteilliste;
        }

        /// <summary>Short label for a format, for status text and warnings.</summary>
        internal static string DisplayName(ExcelFormat format)
        {
            switch (format)
            {
                case ExcelFormat.Planungsdatenbank: return "Planungsdatenbank";
                case ExcelFormat.Bauteilliste: return "Material-Bauteilliste";
                default: return "unrecognised";
            }
        }
    }
}
