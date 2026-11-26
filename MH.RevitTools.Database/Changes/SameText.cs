using System;
using System.Collections.Generic;

namespace MH.RevitTools.Database.Changes
{
    /// <summary>
    /// When two texts say the same: letter by letter, but for what's around them and how their line breaks are written. A
    /// note typed in Studio has CR LF, one from a cell of the workbook LF, and Revit may keep either.
    /// </summary>
    public static class SameText
    {
        public static readonly IEqualityComparer<string> Comparer = new TextComparer();

        public static bool Is(string one, string other) => string.Equals(Plain(one), Plain(other), StringComparison.Ordinal);

        private static string Plain(string text) => (text ?? "").Trim().Replace("\r\n", "\n").Replace('\r', '\n');

        private sealed class TextComparer : IEqualityComparer<string>
        {
            public bool Equals(string one, string other) => Is(one, other);

            // 0 for an empty one: Distinct takes 0 for null without asking here.
            public int GetHashCode(string text)
            {
                string plain = Plain(text);
                return plain.Length == 0 ? 0 : StringComparer.Ordinal.GetHashCode(plain);
            }
        }
    }
}
