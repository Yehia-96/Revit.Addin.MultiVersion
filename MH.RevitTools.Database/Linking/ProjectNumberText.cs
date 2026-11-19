using System.Globalization;
using System.Text.RegularExpressions;

namespace MH.RevitTools.Database.Linking
{
    /// <summary>
    /// A model's Project Number (Project Information), read the way models write it: <c>1761</c> without
    /// a sub-number, <c>1761-01</c> with one. The first four digits are the project number, and "-01" is
    /// sub-number 1. Words after that, as in "1761 Ulm" or "1761-01 Ulm", don't matter. Digits that
    /// might be a sub-number written another way, as in "1761.01" or "1761-012", make it unreadable:
    /// better no suggestion than the wrong project.
    /// </summary>
    public sealed class ProjectNumberText
    {
        // Four digits that don't run on into a fifth, then whatever follows.
        private static readonly Regex Start =
            new Regex(@"^([0-9]{4})(?![0-9])(.*)$", RegexOptions.CultureInvariant | RegexOptions.Singleline);

        // A dash (or a slash) after the number. Word processors turn "-" into look-alikes, so those count
        // too: hyphen, non-breaking hyphen, figure dash, en dash and minus sign.
        private const string Dash = @"[-/‐‑‒–−]";

        // The sub-number: one or two digits after the dash that don't run on into a third.
        private static readonly Regex SubNumberPart =
            new Regex(@"^\s*" + Dash + @"\s*([0-9]{1,2})(?![0-9])", RegexOptions.CultureInvariant);

        // Digits after the dash that can't be a sub-number, as in "1761-012".
        private static readonly Regex OtherDigitsAfterDash =
            new Regex(@"^\s*" + Dash + @"\s*[0-9]", RegexOptions.CultureInvariant);

        // Digits after another separator, as in "1761.01", "1761_01" or "1761 01": a sub-number or not?
        private static readonly Regex DigitsAfterOtherSeparator =
            new Regex(@"^[\s._]*[0-9]", RegexOptions.CultureInvariant);

        private ProjectNumberText(string text, bool isReadable, int number, int? subNumber)
        {
            Text = text;
            IsReadable = isReadable;
            Number = number;
            SubNumber = subNumber;
        }

        /// <summary>The Project Number as written, trimmed; empty when there is none.</summary>
        public string Text { get; }

        public bool IsEmpty => Text.Length == 0;

        /// <summary>Whether it starts with a project number. When false, Number and SubNumber mean nothing.</summary>
        public bool IsReadable { get; }

        /// <summary>E.g. 1761.</summary>
        public int Number { get; }

        /// <summary>E.g. 1 for 1761-01; null when the model writes none, as in 1761.</summary>
        public int? SubNumber { get; }

        public static ProjectNumberText Parse(string text)
        {
            string trimmed = (text ?? "").Trim();
            Match start = Start.Match(trimmed);
            if (!start.Success)
                return Unreadable(trimmed);

            int number = int.Parse(start.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture);
            string rest = start.Groups[2].Value;

            Match subNumber = SubNumberPart.Match(rest);
            if (subNumber.Success)
            {
                int sub = int.Parse(subNumber.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture);
                return new ProjectNumberText(trimmed, true, number, sub);
            }

            if (OtherDigitsAfterDash.IsMatch(rest) || DigitsAfterOtherSeparator.IsMatch(rest))
                return Unreadable(trimmed);

            // No digits follow, as in "1761", "1761 Ulm" or "1761 - Ulm": a number without a sub-number.
            return new ProjectNumberText(trimmed, true, number, null);
        }

        public override string ToString() => Text;

        private static ProjectNumberText Unreadable(string text) => new ProjectNumberText(text, false, 0, null);
    }
}
