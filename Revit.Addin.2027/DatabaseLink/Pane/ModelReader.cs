using Autodesk.Revit.DB;
using MH.RevitTools.Database.Changes;
using MH.RevitTools.Database.Link;
using Revit.Addin._2027.Services.Planungsdatenbank;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit.Addin._2027.DatabaseLink.Pane
{
    /// <summary>
    /// Reads what the change list compares: the model's elements of the categories the database's object
    /// parameters go on, with their Bauteil_ID read exactly as the Excel route reads it (<see cref="ElementKey"/>),
    /// and the model's materials. Only reads. API context.
    /// </summary>
    internal static class ModelReader
    {
        public static IReadOnlyList<ModelElement> Elements(Document document, LinkProjectData data)
        {
            List<BuiltInCategory> categories = ObjectCategories(data);
            if (categories.Count == 0)
                return new ModelElement[0];

            using (var filter = new ElementMulticategoryFilter(categories))
            using (var collector = new FilteredElementCollector(document))
            {
                return collector.WhereElementIsNotElementType().WherePasses(filter)
                    .Select(element => new ModelElement(
                        element.Id.Value, ElementKey.Read(element, ExcelFormat.Planungsdatenbank), TypeName(document, element)))
                    .ToList();
            }
        }

        public static IReadOnlyList<ModelMaterial> Materials(Document document)
        {
            using (var collector = new FilteredElementCollector(document))
            {
                return collector.OfClass(typeof(Material)).Cast<Material>()
                    .Select(material => new ModelMaterial(material.Id.Value, material.Name))
                    .ToList();
            }
        }

        // The categories of the parameters that go on elements, e.g. Generic Models; the database names them
        // by BuiltInCategory, so Revit's language doesn't matter.
        private static List<BuiltInCategory> ObjectCategories(LinkProjectData data)
        {
            var categories = new List<BuiltInCategory>();
            foreach (string name in data.Parameters.Where(p => p.Target == LinkTarget.Object).SelectMany(p => p.Categories)
                         .Distinct(StringComparer.Ordinal))
            {
                BuiltInCategory category;
                if (Enum.TryParse(name, out category) && Enum.IsDefined(typeof(BuiltInCategory), category)
                    && category != BuiltInCategory.OST_Materials && !categories.Contains(category))
                    categories.Add(category);
            }
            return categories;
        }

        // "Bodenplatte: 30 cm", as the Properties palette's type list shows it; null without a type.
        private static string TypeName(Document document, Element element)
        {
            var type = document.GetElement(element.GetTypeId()) as ElementType;
            if (type == null)
                return null;
            return string.IsNullOrWhiteSpace(type.FamilyName) ? type.Name : type.FamilyName + ": " + type.Name;
        }
    }
}
