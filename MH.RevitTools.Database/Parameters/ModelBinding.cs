using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace MH.RevitTools.Database.Parameters
{
    /// <summary>
    /// A project parameter bound in the model, as Revit reports it: its name, its GUID when it's a
    /// shared parameter, instance or type, and its categories as BuiltInCategory names.
    /// </summary>
    public sealed class ModelBinding
    {
        public ModelBinding(string name, Guid? guid, bool isInstance, IEnumerable<string> categories, long elementId = 0)
        {
            Name = (name ?? "").Trim();
            Guid = guid;
            IsInstance = isInstance;
            Categories = new ReadOnlyCollection<string>((categories ?? Enumerable.Empty<string>()).ToList());
            ElementId = elementId;
        }

        /// <summary>The Revit element id of the model's parameter, for Replace to delete it; 0 when unknown.</summary>
        public long ElementId { get; }

        public string Name { get; }

        /// <summary>The shared parameter's GUID; null for a project parameter that isn't shared.</summary>
        public Guid? Guid { get; }

        public bool IsInstance { get; }

        /// <summary>E.g. OST_GenericModel.</summary>
        public IReadOnlyList<string> Categories { get; }

        public bool IsOn(string category) => Categories.Contains(category, StringComparer.Ordinal);
    }
}
