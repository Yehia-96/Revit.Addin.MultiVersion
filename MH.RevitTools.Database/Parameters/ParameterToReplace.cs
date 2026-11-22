using System;

namespace MH.RevitTools.Database.Parameters
{
    /// <summary>
    /// An Other GUID parameter that Replace swaps: Revit can't change a parameter's GUID, so the model's
    /// parameter of the same name (<see cref="Old"/>) is deleted, with its values in every element and
    /// on every category, and the database's (<see cref="Add"/>) is bound in its place.
    /// </summary>
    public sealed class ParameterToReplace
    {
        public ParameterToReplace(ModelBinding old, ParameterToAdd add)
        {
            Old = old ?? throw new ArgumentNullException(nameof(old));
            Add = add ?? throw new ArgumentNullException(nameof(add));
        }

        /// <summary>The model's parameter of the same name, deleted first.</summary>
        public ModelBinding Old { get; }

        /// <summary>The database's parameter, bound in its place from the master file.</summary>
        public ParameterToAdd Add { get; }
    }
}
