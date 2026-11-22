using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using MH.RevitTools.Database.Link;

namespace MH.RevitTools.Database.Parameters
{
    /// <summary>
    /// A parameter that Add binds: from the master file by its GUID, as an instance parameter on
    /// <see cref="Categories"/>. When the model already has it on other categories, it extends that
    /// binding: the other categories and the palette group the model gave it stay as they are.
    /// </summary>
    public sealed class ParameterToAdd
    {
        public ParameterToAdd(LinkParameter parameter, IEnumerable<string> categories, bool extendsBinding)
        {
            Parameter = parameter;
            Categories = new ReadOnlyCollection<string>((categories ?? Enumerable.Empty<string>()).ToList());
            ExtendsBinding = extendsBinding;
            Group = PaletteGroups.ForGroupCode(parameter == null ? null : parameter.GroupCode);
        }

        public LinkParameter Parameter { get; }

        /// <summary>The categories to add it to, as BuiltInCategory names.</summary>
        public IReadOnlyList<string> Categories { get; }

        /// <summary>True when the model has the parameter already, on other categories.</summary>
        public bool ExtendsBinding { get; }

        /// <summary>Where a new binding shows in the palette; an extended one keeps its own.</summary>
        public PaletteGroup Group { get; }
    }
}
