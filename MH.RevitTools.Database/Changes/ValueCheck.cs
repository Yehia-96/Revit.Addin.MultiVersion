using MH.RevitTools.Database.Link;

namespace MH.RevitTools.Database.Changes
{
    /// <summary>How one database value compares with the model.</summary>
    public enum ValueState
    {
        /// <summary>The model has the same value.</summary>
        Same,

        /// <summary>The model has another value, or none, and Apply can write it.</summary>
        Differs,

        /// <summary>The element or material doesn't have the parameter (found by GUID).</summary>
        NoParameter,

        /// <summary>The value can't go into the parameter, e.g. text into a number; the reason says why.</summary>
        CantCompare,

        /// <summary>It differs, but Revit doesn't let anyone change the parameter there.</summary>
        ReadOnly,

        /// <summary>It differs, but the parameter sits on the element's type, which other elements share.</summary>
        OnType,

        /// <summary>It differs, but someone else has borrowed the element.</summary>
        Borrowed,

        /// <summary>It differs, but the element has newer changes in the central model.</summary>
        OutOfDate,

        /// <summary>The model doesn't have the fill pattern.</summary>
        NoPattern,
    }

    /// <summary>
    /// The Revit side's answer for one value on one element or material. Immutable.
    /// </summary>
    public sealed class ValueCheck
    {
        private ValueCheck(ValueState state, string modelText, string problem, string owner)
        {
            State = state;
            ModelText = modelText;
            Problem = problem;
            Owner = owner;
        }

        public static ValueCheck Same { get; } = new ValueCheck(ValueState.Same, null, null, null);

        public static ValueCheck NoParameter { get; } = new ValueCheck(ValueState.NoParameter, null, null, null);

        public static ValueCheck ReadOnly { get; } = new ValueCheck(ValueState.ReadOnly, null, null, null);

        public static ValueCheck OnType { get; } = new ValueCheck(ValueState.OnType, null, null, null);

        public static ValueCheck OutOfDate { get; } = new ValueCheck(ValueState.OutOfDate, null, null, null);

        public static ValueCheck NoPattern { get; } = new ValueCheck(ValueState.NoPattern, null, null, null);

        public ValueState State { get; }

        /// <summary>With <see cref="ValueState.Differs"/>: the model's value as Revit shows it; null or empty when it has none.</summary>
        public string ModelText { get; }

        /// <summary>With <see cref="ValueState.CantCompare"/>: why, in plain words.</summary>
        public string Problem { get; }

        /// <summary>With <see cref="ValueState.Borrowed"/>: who has the element; null when Revit doesn't say.</summary>
        public string Owner { get; }

        public static ValueCheck Differs(string modelText) => new ValueCheck(ValueState.Differs, modelText, null, null);

        public static ValueCheck CantCompare(string problem) => new ValueCheck(ValueState.CantCompare, null, problem, null);

        public static ValueCheck Borrowed(string owner) =>
            new ValueCheck(ValueState.Borrowed, null, null, string.IsNullOrWhiteSpace(owner) ? null : owner.Trim());
    }

    /// <summary>
    /// Compares one database value with the model: the element's or material's parameter with the
    /// <paramref name="parameter"/>'s GUID. Called on Revit's main thread.
    /// </summary>
    public delegate ValueCheck CompareValue(long elementId, LinkParameter parameter, string databaseValue);
}
