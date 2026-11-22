namespace MH.RevitTools.Database.Parameters
{
    /// <summary>Where a model's parameter is used besides the elements' values.</summary>
    public enum ParameterUseKind
    {
        Schedule,
        ViewFilter,
    }

    /// <summary>
    /// One schedule or view filter that uses a model's parameter, as the add-in finds it in the model.
    /// Immutable.
    /// </summary>
    public sealed class ParameterUse
    {
        public ParameterUse(long parameterElementId, ParameterUseKind kind, string name)
        {
            ParameterElementId = parameterElementId;
            Kind = kind;
            Name = string.IsNullOrWhiteSpace(name) ? "(no name)" : name.Trim();
        }

        /// <summary>The Revit element id of the model's parameter (<see cref="ModelBinding.ElementId"/>).</summary>
        public long ParameterElementId { get; }

        public ParameterUseKind Kind { get; }

        /// <summary>The schedule's or the view filter's name.</summary>
        public string Name { get; }
    }
}
