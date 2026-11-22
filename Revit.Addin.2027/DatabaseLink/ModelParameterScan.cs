using Autodesk.Revit.DB;
using MH.RevitTools.Database.Parameters;
using System;
using System.Collections.Generic;

namespace Revit.Addin._2027.DatabaseLink
{
    /// <summary>
    /// The model's project parameters as plain data for <see cref="ParameterCheck"/>: each one's name,
    /// its GUID when it's a shared parameter, instance or type, and its categories. Reads only.
    /// </summary>
    internal static class ModelParameterScan
    {
        public static List<ModelBinding> Read(Document document)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));

            var bindings = new List<ModelBinding>();
            DefinitionBindingMapIterator iterator = document.ParameterBindings.ForwardIterator();
            iterator.Reset();
            while (iterator.MoveNext())
            {
                var definition = iterator.Key as InternalDefinition;
                var binding = iterator.Current as ElementBinding;
                if (definition == null || binding == null)
                    continue;

                var shared = document.GetElement(definition.Id) as SharedParameterElement;
                bindings.Add(new ModelBinding(
                    definition.Name,
                    shared == null ? (Guid?)null : shared.GuidValue,
                    binding is InstanceBinding,
                    CategoryNames(binding.Categories),
                    definition.Id.Value));
            }
            return bindings;
        }

        private static List<string> CategoryNames(CategorySet categories)
        {
            var names = new List<string>();
            foreach (Category category in categories)
            {
                if (category.BuiltInCategory != BuiltInCategory.INVALID)
                    names.Add(category.BuiltInCategory.ToString());
            }
            return names;
        }
    }
}
