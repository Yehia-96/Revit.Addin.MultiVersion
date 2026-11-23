using System;
using System.Collections.Generic;
using System.Linq;

namespace MH.RevitTools.Database.Changes
{
    /// <summary>
    /// Values that mean "no value here", not data, so the link never puts them into the model:
    /// <c>[Modeller]</c> (the modeller fills it in Revit), <c>[ET]</c> (doesn't apply), <c>-</c> (left empty on
    /// purpose), <c>TBT</c> (to be determined), and an empty value. The same rule as the Excel route's
    /// <c>PlanungsdatenbankLayout.IsSkipped</c> in the add-in: change both together.
    /// </summary>
    public static class Placeholders
    {
        public const string Modeller = "[Modeller]";
        public const string NotApplicable = "[ET]";
        public const string LeftEmpty = "-";
        public const string ToBeDetermined = "TBT";

        /// <summary>All of them, in the order "Not applied" lists them.</summary>
        public static IReadOnlyList<string> All { get; } = new[] { Modeller, NotApplicable, LeftEmpty, ToBeDetermined };

        public static bool IsPlaceholder(string value) =>
            string.IsNullOrWhiteSpace(value) || Named(value) != null;

        /// <summary>The placeholder as it's usually written, e.g. [Modeller] for "[modeller] "; null for an empty or a real value.</summary>
        public static string Named(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;
            string trimmed = value.Trim();
            return All.FirstOrDefault(placeholder => string.Equals(placeholder, trimmed, StringComparison.OrdinalIgnoreCase));
        }
    }
}
