// Shared source: keep identical in Revit.Addin.2024 / .2026 / .2027.
//
// Language floor is C# 7.3 and net48, because Revit.Addin.2024 is a non-SDK project with no
// <LangVersion> set. Anything newer compiles in 2026/2027 and only breaks in 2024, at the end.
// So: no record, using var, switch expressions, ??=, nullable annotations, target-typed new or
// pattern combinators; and no Dictionary.TryAdd, string.Contains(char), HashCode.Combine or
// Enumerable.ToHashSet.
using System;
using System.Collections.Generic;

namespace Revit.Addin._2024.Services.Planungsdatenbank
{
    /// <summary>
    /// Where things are in the Planungsdatenbank workbook, expressed as the <em>labels</em> the
    /// workbook actually contains rather than as row and column numbers.
    /// <para>
    /// This is the whole point of the new reader. The old one addresses sheets positionally
    /// (<c>Worksheet(2)</c>, <c>Worksheet(3)</c>) with fixed row numbers in
    /// <see cref="ExcelLayout"/>, which is correct for the Material-Bauteilliste workbook but
    /// silently wrong here: in this 21-sheet workbook sheet 3 is <c>Bauteilkategorieliste</c> and
    /// the material sheet is sheet 5. Addressing by name and by label means a column inserted in
    /// the workbook costs nothing.
    /// </para>
    /// <para>
    /// Verified against C:\Yehia\DatabaseFiles\Planungsdatenbank.xlsx on 2026-10-31.
    /// </para>
    /// </summary>
    internal static class PlanungsdatenbankLayout
    {
        /// <summary>Sheets the reader needs, by name.</summary>
        internal static class Sheets
        {
            internal const string Objects = "Objektdatenbank";
            internal const string Materials = "Materialdatenbank";
        }

        /// <summary>
        /// The row labels in the label column. Objektdatenbank and Materialdatenbank share this
        /// layout exactly, so one set serves both.
        /// </summary>
        internal static class Labels
        {
            internal const string ParameterId = "Parameter-ID";

            /// <summary>
            /// The workbook spells it "Prameter-ID". Accepted as an alternate rather than
            /// corrected, because the file is the office's and will keep arriving this way.
            /// </summary>
            internal const string ParameterIdTypo = "Prameter-ID";

            internal const string PsetId = "Pset-ID";
            internal const string ParameterSet = "Parameterset";
            internal const string ParameterGroupId = "Parametergruppen-ID";
            internal const string ParameterGroup = "Parametergruppe";
            internal const string ParameterName = "Parametername";
            internal const string InputRequired = "Input obligat";
            internal const string DataType = "Datentyp";
            internal const string DataFormat = "Datenformat";
            internal const string Unit = "Einheit";
        }

        /// <summary>Column headers read from the <c>Parametername</c> row.</summary>
        internal static class Headers
        {
            // Family identity. Bauteil_ID (e.g. BT-1101-01) is the unique key and the connection
            // to Revit, where the modellers enter it on every element.
            internal const string BauteilId = "Bauteil_ID";

            /// <summary>Foreign key into <see cref="Sheets.Materials"/>, e.g. MB-1761-01-BET-0011.</summary>
            internal const string MaterialId = "Material-ID";

            // Material identity.
            internal const string MaterialName = "Materialname";
            internal const string MaterialGroup = "Materialgruppe";

            // Fill patterns. Present in the workbook but only sparsely filled so far, so a blank
            // means "no pattern", never an error. Reading them by label from the start is what
            // makes the rest of the rollout a no-op: when the office fills these columns in, the
            // values simply start arriving. No code change, nothing to remember.
            internal const string SurfacePattern = "Oberfläche-Ansicht_ID";
            internal const string CutPattern = "Oberfläche-Schnitt_ID";
        }

        /// <summary>
        /// Cell values that mean "no value here", not data.
        /// <para>
        /// This replaces the old format's per-column ignore row, which marked whole parameters as
        /// <c>Modeller</c> or <c>SBIM</c>. The new workbook marks individual cells instead, so the
        /// decision is per value: a parameter can be author-supplied for one Bauteil and
        /// modeller-supplied for the next.
        /// </para>
        /// <list type="bullet">
        /// <item><c>[Modeller]</c> - the modeller fills this in Revit; the workbook must not overwrite it.</item>
        /// <item><c>[ET]</c> - not applicable to this row (an Einbauteil has no Material-ID).</item>
        /// <item><c>-</c> - deliberately empty.</item>
        /// <item><c>TBT</c> - to be determined.</item>
        /// </list>
        /// </summary>
        private static readonly HashSet<string> SkipValues =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "[Modeller]", "[ET]", "-", "TBT"
            };

        /// <summary>True when a cell carries no usable value and must be left alone.</summary>
        internal static bool IsSkipped(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return true;
            return SkipValues.Contains(value.Trim());
        }

        /// <summary>
        /// How far down to look for the label rows. The labels sit in rows 2-11 in every sheet
        /// seen so far; this is slack, not an expectation.
        /// </summary>
        internal const int MaxLabelRow = 15;

        /// <summary>The label column. Column B in every sheet of this workbook.</summary>
        internal const int LabelColumn = 2;

        /// <summary>First column that can hold a parameter. Column C; A and B are the label gutter.</summary>
        internal const int FirstDataColumn = 3;
    }
}
