// Shared source: keep identical in Revit.Addin.2024 / .2026 / .2027 apart from the namespace year.
// C# 7.3 / net48 floor - see PlanungsdatenbankLayout.cs for the full restriction list.
// Without Revit, so it's under tests: MH.RevitTools.Database.Tests\ExcelImport.
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit.Addin._2026.Services
{
    /// <summary>
    /// The names a shared parameter file has while a workbook's parameters are put into it.
    /// <para>
    /// A name is taken once, however many sheets have it. Projektnummer, for one, is in both the object sheet and the
    /// material sheet. The file's groups don't show what the same run has just created, so the run remembers it here;
    /// otherwise it would create the name a second time, which Revit refuses in the same group and allows in another.
    /// </para>
    /// </summary>
    internal sealed class SharedParameterNames
    {
        private readonly HashSet<string> _inFile;
        private readonly HashSet<string> _taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _refused = new List<string>();

        internal SharedParameterNames(IEnumerable<string> inFile)
        {
            _inFile = new HashSet<string>(inFile, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>True when the parameter is to be created: it isn't in the file, and this run hasn't taken it yet.</summary>
        internal bool Take(string name)
        {
            return _taken.Add(name) && !_inFile.Contains(name);
        }

        /// <summary>Revit didn't create it.</summary>
        internal void Refused(string name)
        {
            _refused.Add(name);
        }

        /// <summary>
        /// What was refused and isn't in the file now. Revit can refuse a parameter and have it in the file all the same, and
        /// that one isn't missing.
        /// </summary>
        internal List<string> Missing(IEnumerable<string> inFileNow)
        {
            var now = new HashSet<string>(inFileNow, StringComparer.OrdinalIgnoreCase);
            return _refused.Where(name => !now.Contains(name)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
    }
}
