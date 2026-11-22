namespace MH.RevitTools.Database.Parameters
{
    /// <summary>
    /// An element Revit deletes together with a model's parameter, as the add-in found by trying it in a
    /// change it then undid. Immutable.
    /// </summary>
    public sealed class DeletedElement
    {
        public DeletedElement(long parameterElementId, long id, string name, string kind)
        {
            ParameterElementId = parameterElementId;
            Id = id;
            Name = string.IsNullOrWhiteSpace(name) ? "(no name)" : name.Trim();
            Kind = string.IsNullOrWhiteSpace(kind) ? "Element" : kind.Trim();
        }

        /// <summary>The Revit element id of the model's parameter it goes with (<see cref="ModelBinding.ElementId"/>).</summary>
        public long ParameterElementId { get; }

        /// <summary>Its Revit element id, as Manage › Select by ID takes it.</summary>
        public long Id { get; }

        public string Name { get; }

        /// <summary>What it is, e.g. "View filter", "Schedule", or a category.</summary>
        public string Kind { get; }
    }
}
