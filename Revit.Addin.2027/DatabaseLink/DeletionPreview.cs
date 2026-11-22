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
                return element == null
                    ? new DeletedElement(parameterId, id, null, null)
                    : new DeletedElement(parameterId, id, element.Name, KindOf(element));
            }
            catch (Exception ex) when (RevitErrors.IsRefusal(ex))
            {
                // A name Revit won't give mustn't hide the element: it's listed by its id.
                Trace.WriteLine("[Parameter Tools] Couldn't read element " + id + ": " + ex.Message);
                return new DeletedElement(parameterId, id, null, null);
            }
        }

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
