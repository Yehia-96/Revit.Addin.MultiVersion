using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit.Addin._2027.DatabaseLink
{
    /// <summary>
    /// A 3D view of its own for the elements Replace would delete, with nothing else shown, so they can be
    /// looked at before anything goes (Yehia, 2026-11-22). Made in one undo step; Check parameters opens it
    /// once its dialog has closed. The view stays in the model until someone deletes it.
    /// </summary>
    internal static class DeletedElementsView
    {
        private const string BaseName = "MH Replace - elements it would delete";

        /// <summary>The new view; null when Revit couldn't make it, with the reason.</summary>
        public static View3D Create(Document document, IReadOnlyCollection<long> elementIds, out string problem)
        {
            List<ElementId> ids = elementIds
                .Select(id => new ElementId(id))
                .Where(id => document.GetElement(id) != null)
                .ToList();
            if (ids.Count == 0)
            {
                problem = "the elements aren't in the model any more";
                return null;
            }

            ViewFamilyType type = new FilteredElementCollector(document).OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(candidate => candidate.ViewFamily == ViewFamily.ThreeDimensional);
            if (type == null)
            {
                problem = "the model has no 3D view type";
                return null;
            }

            using (var transaction = new Transaction(document, "Show what Replace would delete"))
            {
                if (transaction.Start() != TransactionStatus.Started)
                {
                    problem = "Revit couldn't start the change";
                    return null;
                }
                try
                {
                    View3D view = View3D.CreateIsometric(document, type.Id);
                    view.Name = FreeName(document);
                    LetThemShow(document, view, ids);
                    // Isolated for good, not just for now: the view keeps showing only them.
                    view.IsolateElementsTemporary(ids);
                    view.ConvertTemporaryHideIsolateToPermanent();
                    if (transaction.Commit() != TransactionStatus.Committed)
                    {
                        problem = "Revit undid the change; its own message said why";
                        return null;
                    }
                    problem = null;
                    return view;
                }
                catch (Exception ex)
                {
                    // Any failure: the view isn't made, and the dialog says why.
                    if (transaction.GetStatus() == TransactionStatus.Started)
                        transaction.RollBack();
                    problem = ex.Message.TrimEnd('.');
                    return null;
                }
            }
        }

        // Isolating only hides the rest: a view template, a hidden category or a hidden workset would still hide
        // the elements themselves. So the view gets no template, and their categories and worksets are shown.
        private static void LetThemShow(Document document, View3D view, IReadOnlyCollection<ElementId> ids)
        {
            view.ViewTemplateId = ElementId.InvalidElementId;
            List<Element> elements = ids.Select(document.GetElement).Where(element => element != null).ToList();

            foreach (ElementId category in elements.Where(element => element.Category != null)
                         .Select(element => element.Category.Id).Distinct())
            {
                if (view.CanCategoryBeHidden(category))
                    view.SetCategoryHidden(category, false);
            }

            if (!document.IsWorkshared)
                return;
            foreach (WorksetId workset in elements.Select(element => element.WorksetId).Distinct())
            {
                if (workset != WorksetId.InvalidWorksetId && !view.IsWorksetVisible(workset))
                    view.SetWorksetVisibility(workset, WorksetVisibility.Visible);
            }
        }

        // View names are unique in a model: the next free "… (2)", "… (3)".
        private static string FreeName(Document document)
        {
            var taken = new HashSet<string>(
                new FilteredElementCollector(document).OfClass(typeof(View)).Cast<View>().Select(view => view.Name),
                StringComparer.OrdinalIgnoreCase);
            string name = BaseName;
            for (int i = 2; taken.Contains(name); i++)
                name = BaseName + " (" + i + ")";
            return name;
        }
    }
}
