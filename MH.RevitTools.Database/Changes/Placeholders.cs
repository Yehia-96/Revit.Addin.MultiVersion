using System;
using System.Collections.Generic;

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
        private static readonly HashSet<string> Values = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "[Modeller]", "[ET]", "-", "TBT",
        };

        public static bool IsPlaceholder(string value) =>
            string.IsNullOrWhiteSpace(value) || Values.Contains(value.Trim());
    }
}
