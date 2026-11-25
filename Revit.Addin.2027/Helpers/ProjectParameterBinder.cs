// Shared source: keep identical in Revit.Addin.2024 / .2026 / .2027 apart from the namespace year.
// C# 7.3 / net48 floor - see PlanungsdatenbankLayout.cs for the full restriction list.
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using System.Collections.Generic;

namespace Revit.Addin._2027.Helpers
{
    /// <summary>What became of putting a parameter on categories.</summary>
    internal enum BindingOutcome
    {
        /// <summary>It's on all of them already.</summary>
        AlreadyThere,

        Added,

        /// <summary>This project has it as a type parameter. It stays one; Revit doesn't allow both.</summary>
        IsTypeParameter,

        /// <summary>Revit refused.</summary>
        Refused,
    }

    /// <summary>
    /// Puts a shared parameter on categories, as an instance parameter of the project.
    /// <para>
    /// It only ever adds. Revit's ReInsert replaces a parameter's categories by the ones it's given, and the parameter,
    /// with its values, is gone from those that aren't among them. So the categories it's on already are given along.
    /// </para>
    /// </summary>
    internal static class ProjectParameterBinder
    {
        /// <summary>Inside a transaction.</summary>
        /// <param name="definition">From the shared parameter file, or the project's own.</param>
        /// <param name="groupId">Where the palette shows a new one. One the project has stays where it is.</param>
        internal static BindingOutcome Add(
            Document document, Application application, Definition definition, CategorySet categories, ForgeTypeId groupId)
        {
            BindingMap map = document.ParameterBindings;
            Definition own = InProject(document, definition);
            Binding existing = own == null || !map.Contains(own) ? null : map.get_Item(own);
            if (existing == null)
            {
                return map.Insert(definition, application.Create.NewInstanceBinding(categories), groupId)
                    ? BindingOutcome.Added
                    : BindingOutcome.Refused;
            }

            var instances = existing as InstanceBinding;
            if (instances == null)
                return BindingOutcome.IsTypeParameter;

            CategorySet all = application.Create.NewCategorySet();
            var ids = new HashSet<ElementId>();
            foreach (Category category in instances.Categories)
            {
                if (ids.Add(category.Id)) all.Insert(category);
            }

            bool isNew = false;
            foreach (Category category in categories)
            {
                if (!ids.Add(category.Id)) continue;
                all.Insert(category);
                isNew = true;
            }
            if (!isNew)
                return BindingOutcome.AlreadyThere;

            return map.ReInsert(own, application.Create.NewInstanceBinding(all), own.GetGroupTypeId())
                ? BindingOutcome.Added
                : BindingOutcome.Refused;
        }

        /// <summary>The reason, for the user; null when there's nothing to say.</summary>
        internal static string Problem(string parameterName, BindingOutcome outcome)
        {
            switch (outcome)
            {
                case BindingOutcome.IsTypeParameter: return parameterName + " (a type parameter in this project)";
                case BindingOutcome.Refused: return parameterName + " (refused by Revit)";
                default: return null;
            }
        }

        // The project's own definition of it; null when the project doesn't have it.
        private static Definition InProject(Document document, Definition definition)
        {
            var external = definition as ExternalDefinition;
            if (external == null) return definition;

            SharedParameterElement shared = SharedParameterElement.Lookup(document, external.GUID);
            return shared == null ? null : shared.GetDefinition();
        }
    }
}
