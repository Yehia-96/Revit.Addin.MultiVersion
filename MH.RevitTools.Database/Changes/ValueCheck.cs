using MH.RevitTools.Database.Link;

namespace MH.RevitTools.Database.Changes
{
    /// <summary>How one database value compares with the model.</summary>
    public enum ValueState
    {
        /// <summary>The model has the same value.</summary>
        Same,

        /// <summary>The model has another value, or none.</summary>
        Differs,

        /// <summary>The element or material doesn't have the parameter (found by GUID).</summary>
        NoParameter,

        /// <summary>The value can't go into the parameter, e.g. text into a number; the reason says why.</summary>
        CantCompare,
    }

    /// <summary>
    /// The Revit side's answer for one value on one element or material. Immutable.
    /// </summary>
    public sealed class ValueCheck
    {
        private ValueCheck(ValueState state, string modelText, string problem)
        {
            State = state;
            ModelText = modelText;
            Problem = problem;
        }

        public static ValueCheck Same { get; } = new ValueCheck(ValueState.Same, null, null);

        public static ValueCheck NoParameter { get; } = new ValueCheck(ValueState.NoParameter, null, null);

        public ValueState State { get; }

        /// <summary>With <see cref="ValueState.Differs"/>: the model's value as Revit shows it; null or empty when it has none.</summary>
        public string ModelText { get; }

        /// <summary>With <see cref="ValueState.CantCompare"/>: why, in plain words.</summary>
        public string Problem { get; }

        public static ValueCheck Differs(string modelText) => new ValueCheck(ValueState.Differs, modelText, null);

        public static ValueCheck CantCompare(string problem) => new ValueCheck(ValueState.CantCompare, null, problem);
    }

    /// <summary>
    /// Compares one database value with the model: the element's or material's parameter with the
    /// <paramref name="parameter"/>'s GUID. Called on Revit's main thread.
    /// </summary>
    public delegate ValueCheck CompareValue(long elementId, LinkParameter parameter, string databaseValue);
}
