namespace MH.RevitTools.Database.Parameters
{
    /// <summary>
    /// An element Revit deletes together with a model's parameter, as the add-in found by trying it in a
    /// change it then undid. Immutable.
    /// </summary>
    public sealed class DeletedElement
    {
        /// <param name="parameterElementId">The model's parameter it goes with.</param>
        /// <param name="id">Its Revit element id.</param>
        /// <param name="name">Its name, or its type's; empty when Revit gives none.</param>
        /// <param name="kind">E.g. "View filter", a category, or Revit's class name.</param>
        /// <param name="ownerViewKind">For an element that belongs to one view: "schedule", "sheet" or "view".</param>
        /// <param name="ownerViewName">That view's name.</param>
        /// <param name="canBeShown">True for a model element with geometry, which a 3D view can show.</param>
        public DeletedElement(long parameterElementId, long id, string name, string kind,
            string ownerViewKind = null, string ownerViewName = null, bool canBeShown = false)
        {
            ParameterElementId = parameterElementId;
            Id = id;
            Name = string.IsNullOrWhiteSpace(name) ? "(no name)" : name.Trim();
            Kind = string.IsNullOrWhiteSpace(kind) ? "Element" : kind.Trim();
            CanBeShown = canBeShown;
            Note = string.IsNullOrWhiteSpace(ownerViewName)
                ? (canBeShown ? "" : "Not shown in any view")
                : "In " + (string.IsNullOrWhiteSpace(ownerViewKind) ? "view" : ownerViewKind.Trim()) + " “" + ownerViewName.Trim() + "”";
        }

        /// <summary>The Revit element id of the model's parameter it goes with (<see cref="ModelBinding.ElementId"/>).</summary>
        public long ParameterElementId { get; }

        /// <summary>Its Revit element id, as Manage › Select by ID takes it.</summary>
        public long Id { get; }

        public string Name { get; }

        /// <summary>What it is, e.g. "View filter", "Schedule", or a category.</summary>
        public string Kind { get; }

        /// <summary>True for a model element with geometry, which a 3D view can show.</summary>
        public bool CanBeShown { get; }

        /// <summary>The line under its name: the view it belongs to, or that no view shows it; empty for a model element.</summary>
        public string Note { get; }
    }
}
