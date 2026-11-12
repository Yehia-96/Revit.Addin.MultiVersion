// Shared source: keep identical in Revit.Addin.2024 / .2026 / .2027 apart from the namespace year.
// C# 7.3 / net48 floor - see PlanungsdatenbankLayout.cs for the full restriction list.
using System;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.Revit.DB;

namespace Revit.Addin._2027.Services.Planungsdatenbank
{
    /// <summary>
    /// The connection between a row in the loaded workbook and an element in the Revit model.
    /// <para>
    /// Each format connects through its own identifier:
    /// <list type="bullet">
    /// <item>Material-Bauteilliste - the integer <c>Bauteilnummer</c> parameter, as it always has.</item>
    /// <item>Planungsdatenbank - the text <c>Bauteil_ID</c> (e.g. <c>BT-1101-01</c>), which the
    /// modellers enter by hand on every element. One value per element, no pairing.</item>
    /// </list>
    /// The reader keys its data with <see cref="Normalise"/> and every matcher reads the element
    /// side with <see cref="Read"/>, so the two sides are built by the same rules and cannot drift.
    /// </para>
    /// </summary>
    internal static class ElementKey
    {
        /// <summary>The old format's identifier, an integer parameter on the element.</summary>
        internal const string BauteilnummerParameter = "Bauteilnummer";

        /// <summary>
        /// The new format's identifier as the workbook spells it. The modellers type this by hand,
        /// so <see cref="BauteilIdSpellings"/> also accepts the hyphenated spelling.
        /// </summary>
        internal const string BauteilIdParameter = PlanungsdatenbankLayout.Headers.BauteilId;

        private static readonly string[] BauteilIdSpellings = { BauteilIdParameter, "Bauteil-ID" };

        /// <summary>Comparer for keys: an ID typed in lower case still matches.</summary>
        internal static readonly StringComparer KeyComparer = StringComparer.OrdinalIgnoreCase;

        /// <summary>What the identifier is called, for labels and messages.</summary>
        internal static string Label(ExcelFormat format)
        {
            return format == ExcelFormat.Planungsdatenbank ? "Bauteil-ID" : "Bauteilnummer";
        }

        /// <summary>
        /// Brings a key from the workbook into the form <see cref="Read"/> produces, or null if it
        /// is not a usable key.
        /// <para>
        /// For the old format that is the integer written plainly, so <c>" 1101"</c> still meets an
        /// element whose Bauteilnummer is 1101 - exactly what the old <c>int.Parse</c> matching did.
        /// For the new format it is the trimmed text.
        /// </para>
        /// </summary>
        internal static string Normalise(string key, ExcelFormat format)
        {
            if (key == null) return null;
            string trimmed = key.Trim();
            if (trimmed.Length == 0) return null;

            if (format == ExcelFormat.Planungsdatenbank) return trimmed;

            int number;
            return int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out number)
                ? number.ToString(CultureInfo.InvariantCulture)
                : null;
        }

        /// <summary>
        /// Reads an element's key, or null when the element carries none - no parameter, or one
        /// left empty. Such an element is simply not connected to the workbook.
        /// </summary>
        internal static string Read(Element element, ExcelFormat format)
        {
            if (element == null) return null;

            if (format != ExcelFormat.Planungsdatenbank)
            {
                Parameter bn = element.LookupParameter(BauteilnummerParameter);
                if (bn == null || !bn.HasValue) return null;
                return bn.AsInteger().ToString(CultureInfo.InvariantCulture);
            }

            foreach (string name in BauteilIdSpellings)
            {
                Parameter p = element.LookupParameter(name);
                if (p == null || !p.HasValue) continue;

                // Normally a text parameter. If a family made it a number, take what Revit shows.
                string raw = p.StorageType == StorageType.String ? p.AsString() : p.AsValueString();
                string key = Normalise(raw, format);
                if (key != null) return key;
            }
            return null;
        }

        /// <summary>
        /// Sorts keys the way a person reads them: digit runs compare as numbers, so 201 comes
        /// before 1101 and BT-201-01 before BT-1101-01.
        /// </summary>
        internal static readonly IComparer<string> NaturalOrder = new NaturalComparer();

        private sealed class NaturalComparer : IComparer<string>
        {
            public int Compare(string x, string y)
            {
                if (ReferenceEquals(x, y)) return 0;
                if (x == null) return -1;
                if (y == null) return 1;

                int i = 0, j = 0;
                while (i < x.Length && j < y.Length)
                {
                    if (char.IsDigit(x[i]) && char.IsDigit(y[j]))
                    {
                        int si = i, sj = j;
                        while (i < x.Length && char.IsDigit(x[i])) i++;
                        while (j < y.Length && char.IsDigit(y[j])) j++;

                        // Compare the runs as numbers without parsing, so length is no limit.
                        string a = x.Substring(si, i - si).TrimStart('0');
                        string b = y.Substring(sj, j - sj).TrimStart('0');
                        if (a.Length != b.Length) return a.Length.CompareTo(b.Length);
                        int c = string.CompareOrdinal(a, b);
                        if (c != 0) return c;
                    }
                    else
                    {
                        int c = char.ToUpperInvariant(x[i]).CompareTo(char.ToUpperInvariant(y[j]));
                        if (c != 0) return c;
                        i++; j++;
                    }
                }
                return (x.Length - i).CompareTo(y.Length - j);
            }
        }
    }
}
