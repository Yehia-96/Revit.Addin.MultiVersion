using Autodesk.Revit.DB;
using MH.RevitTools.Database.Parameters;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Revit.Addin._2027.DatabaseLink
{
    /// <summary>
    /// Finds what else Revit deletes together with each model parameter Replace would delete: it deletes
    /// the parameter in a change it always undoes, then reads the elements Revit named, which are back by
    /// then. The model never changes. Replace lists them and deletes them only once the user agrees.
    /// </summary>
    internal static class DeletionPreview
    {
        public static ReplaceDeletions Find(Document document, IReadOnlyList<ParameterToReplace> toReplace)
        {
            var elements = new List<DeletedElement>();
            var tried = new HashSet<long>();
            using (var trial = new Transaction(document, "See what Replace deletes"))
            {
                // Without a change to try it in, nothing is known; Replace itself still refuses an unlisted deletion.
                if (trial.Start() != TransactionStatus.Started)
                    return ReplaceDeletions.Found(toReplace, elements);

                // Whatever Revit posts about the trial goes with it, never to the user.
                FailureHandlingOptions options = trial.GetFailureHandlingOptions();
                options.SetClearAfterRollback(true);
                trial.SetFailureHandlingOptions(options);
                try
                {
                    foreach (ParameterToReplace swap in toReplace)
                    {
                        long parameterId = swap.Old.ElementId;
                        if (parameterId == 0 || !tried.Add(parameterId))
                            continue;
                        foreach (long id in DeletedWith(document, parameterId))
                            elements.Add(Describe(document, parameterId, id));
                    }
                }
                finally
                {
                    if (trial.GetStatus() == TransactionStatus.Started)
                        RollBack(() => trial.RollBack());
                }
            }
            return ReplaceDeletions.Found(toReplace, elements);
        }

        // The ids Revit deletes besides the parameter, found in a sub-transaction rolled back before they're
        // read, so the elements exist again. A parameter Revit won't delete at all yields none: Replace says
        // so itself.
        private static IReadOnlyList<long> DeletedWith(Document document, long parameterId)
        {
            using (var attempt = new SubTransaction(document))
            {
                if (attempt.Start() != TransactionStatus.Started)
                    return Array.Empty<long>();
                try
                {
                    return document.Delete(new ElementId(parameterId))
                        .Select(id => id.Value)
                        .Where(id => id != parameterId)
                        .Distinct()
                        .ToList();
                }
                catch (Exception ex) when (RevitErrors.IsRefusal(ex))
                {
                    return Array.Empty<long>();
                }
                finally
                {
                    if (attempt.GetStatus() == TransactionStatus.Started)
                        RollBack(() => attempt.RollBack());
                }
            }
        }

        // A rollback that fails mustn't hide what went wrong before it; disposing the transaction still undoes
        // the trial.
        private static void RollBack(Action rollBack)
        {
            try
            {
                rollBack();
            }
            catch (Exception ex) when (RevitErrors.IsRefusal(ex))
            {
                Trace.WriteLine("[Parameter Tools] Rolling back the Replace trial failed: " + ex.Message);
            }
        }

        private static DeletedElement Describe(Document document, long parameterId, long id)
        {
            try
            {
                Element element = document.GetElement(new ElementId(id));
                if (element == null)
                    return new DeletedElement(parameterId, id, null, null);

                var owner = element.ViewSpecific ? document.GetElement(element.OwnerViewId) as View : null;
                return new DeletedElement(parameterId, id, NameOf(document, element), KindOf(element),
                    owner == null ? null : ViewKindOf(owner), owner == null ? null : owner.Name, CanBeShown(element));
            }
            catch (Exception ex) when (RevitErrors.IsRefusal(ex))
            {
                // A name Revit won't give mustn't hide the element: it's listed by its id.
                Trace.WriteLine("[Parameter Tools] Couldn't read element " + id + ": " + ex.Message);
                return new DeletedElement(parameterId, id, null, null);
            }
        }

        // Its own name, else its type's ("Family: Type"); empty when Revit gives neither.
        private static string NameOf(Document document, Element element)
        {
            if (!string.IsNullOrWhiteSpace(element.Name))
                return element.Name;
            ElementId typeId = element.GetTypeId();
            var type = typeId == ElementId.InvalidElementId ? null : document.GetElement(typeId) as ElementType;
            if (type == null || string.IsNullOrWhiteSpace(type.Name))
                return null;
            return string.IsNullOrWhiteSpace(type.FamilyName) ? type.Name : type.FamilyName + ": " + type.Name;
        }

        // A model element with geometry: what a 3D view of its own can show.
        private static bool CanBeShown(Element element) =>
            !element.ViewSpecific
            && element.Category != null
            && element.Category.CategoryType == CategoryType.Model
            && element.get_BoundingBox(null) != null;

        private static string ViewKindOf(View view) =>
            view is ViewSchedule ? "schedule" : view is ViewSheet ? "sheet" : "view";

        // What the list calls it: the kinds a parameter is likely to take with it, else its category, else
        // Revit's own name for its class.
        private static string KindOf(Element element)
        {
            if (element is ParameterFilterElement)
                return "View filter";
            if (element is ViewSchedule)
                return "Schedule";
            var view = element as View;
            if (view != null)
                return view.IsTemplate ? "View template" : "View";
            if (element is ParameterElement)
                return "Parameter";
            if (element is GlobalParameter)
                return "Global parameter";
            Category category = element.Category;
            return category != null && !string.IsNullOrEmpty(category.Name) ? category.Name : element.GetType().Name;
        }
    }
}
