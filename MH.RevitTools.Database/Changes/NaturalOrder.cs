using System;
using System.Collections.Generic;

namespace MH.RevitTools.Database.Changes
{
    /// <summary>
    /// Sorts IDs the way a person reads them: digit runs compare as numbers, so BT-201-01 comes before
    /// BT-1101-01. The same order as the add-in's <c>ElementKey.NaturalOrder</c>.
    /// </summary>
    internal sealed class NaturalOrder : IComparer<string>
    {
        public static NaturalOrder Instance { get; } = new NaturalOrder();

        public int Compare(string x, string y)
        {
            if (ReferenceEquals(x, y))
                return 0;
            if (x == null)
                return -1;
            if (y == null)
                return 1;

            int i = 0, j = 0;
            while (i < x.Length && j < y.Length)
            {
                if (char.IsDigit(x[i]) && char.IsDigit(y[j]))
                {
                    int startX = i, startY = j;
                    while (i < x.Length && char.IsDigit(x[i]))
                        i++;
                    while (j < y.Length && char.IsDigit(y[j]))
                        j++;

                    // As numbers, without parsing, so length is no limit.
                    string a = x.Substring(startX, i - startX).TrimStart('0');
                    string b = y.Substring(startY, j - startY).TrimStart('0');
                    if (a.Length != b.Length)
                        return a.Length.CompareTo(b.Length);
                    int digits = string.CompareOrdinal(a, b);
                    if (digits != 0)
                        return digits;
                }
                else
                {
                    int letters = char.ToUpperInvariant(x[i]).CompareTo(char.ToUpperInvariant(y[j]));
                    if (letters != 0)
                        return letters;
                    i++;
                    j++;
                }
            }
            return (x.Length - i).CompareTo(y.Length - j);
        }
    }
}
