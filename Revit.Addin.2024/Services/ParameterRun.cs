// Shared source: keep identical in Revit.Addin.2024 / .2026 / .2027 apart from the namespace year.
// C# 7.3 / net48 floor - see PlanungsdatenbankLayout.cs for the full restriction list.
// Without Revit, so it's under tests: MH.RevitTools.Database.Tests\ExcelImport.
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit.Addin._2024.Services
{
    /// <summary>
    /// What one run of Add Parameters did, and what the user is told. A run that finds elements and has nothing to change is
    /// another thing than one that finds no element. Immutable.
    /// </summary>
    public sealed class ParameterRun
    {
        // A message box has no scroll bar.
        private const int MaxNames = 15;

        /// <param name="notAdded">Parameters that couldn't be added, each with the reason.</param>
        /// <param name="notWritten">Parameters that an element lacks or has read-only, though the workbook has a value for it.</param>
        public ParameterRun(
            int elementsMatched, int parametersAdded, int valuesWritten,
            IEnumerable<string> notAdded = null, IEnumerable<string> notWritten = null)
        {
            ElementsMatched = elementsMatched;
            ParametersAdded = parametersAdded;
            ValuesWritten = valuesWritten;
            NotAdded = Distinct(notAdded);
            NotWritten = Distinct(notWritten);
        }

        public int ElementsMatched { get; private set; }

        public int ParametersAdded { get; private set; }

        public int ValuesWritten { get; private set; }

        public IReadOnlyList<string> NotAdded { get; private set; }

        public IReadOnlyList<string> NotWritten { get; private set; }

        public bool ChangedAnything
        {
            get { return ParametersAdded > 0 || ValuesWritten > 0; }
        }

        /// <param name="noMatch">What to say when no element matches.</param>
        /// <param name="workbook">The workbook's name and when it was saved; null to leave it out.</param>
        public string Message(string noMatch, string workbook)
        {
            if (ElementsMatched == 0) return noMatch;

            string text = ChangedAnything
                ? ParametersAdded + " parameters applied.\n" + ValuesWritten + " values applied"
                : NothingToChange(workbook);
            if (NotAdded.Count > 0)
                text += "\n\nNot added: " + Some(NotAdded) + ".";
            if (NotWritten.Count > 0)
                text += "\n\nNot written, because an element has no such parameter or has it read-only: " + Some(NotWritten) + ".";
            return text;
        }

        private string NothingToChange(string workbook)
        {
            bool isOne = ElementsMatched == 1;
            string text = (isOne ? "1 element matches" : ElementsMatched + " elements match")
                + ", and nothing was changed: " + (isOne ? "its" : "their") + (NotWritten.Count == 0 ? "" : " other")
                + " parameters already have the values of the Excel file.";
            return string.IsNullOrWhiteSpace(workbook)
                ? text
                : text + "\n\nExcel file: " + workbook + ". If you have changed it since then, save it in Excel and run this again.";
        }

        private static string Some(IReadOnlyList<string> names)
        {
            string text = string.Join(", ", names.Take(MaxNames));
            return names.Count <= MaxNames ? text : text + " and " + (names.Count - MaxNames) + " more";
        }

        private static IReadOnlyList<string> Distinct(IEnumerable<string> names)
        {
            return (names ?? Enumerable.Empty<string>()).Distinct(StringComparer.Ordinal).ToList().AsReadOnly();
        }
    }
}
