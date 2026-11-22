using System;

namespace MH.RevitTools.Database.Parameters
{
    /// <summary>One answer to Replace's second question, as Revit's dialog offers it: a label and a note under it.</summary>
    public sealed class ReplaceUsedAnswer
    {
        public ReplaceUsedAnswer(ReplaceChoice choice, string label, string note)
        {
            Choice = choice;
            Label = label ?? throw new ArgumentNullException(nameof(label));
            Note = note ?? "";
        }

        public ReplaceChoice Choice { get; }

        public string Label { get; }

        public string Note { get; }
    }
}
