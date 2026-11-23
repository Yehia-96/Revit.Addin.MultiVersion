using Autodesk.Revit.DB;
using MH.RevitTools.Database.Changes;
using MH.RevitTools.Database.Checking;
using MH.RevitTools.Database.Link;
using Revit.Addin._2027.DatabaseLink.Pane;
using Revit.Addin._2027.Helpers;
using Revit.Addin._2027.Services.Planungsdatenbank;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit.Addin._2027.DatabaseLink.Apply
{
    /// <summary>
    /// Writes the database's values the pane lists into the model, in one undo step, with the loop the Excel
    /// import uses (<see cref="ElementValueWriter"/>). It borrows the elements first and leaves out those someone
    /// else has. It never adds parameters and never touches what isn't listed. API context, outside a
    /// transaction.
    /// </summary>
    internal static class DatabaseApplier
    {
        /// <param name="data">The database's values as last read.</param>
        /// <param name="groups">The groups to apply, as the pane listed them.</param>
        public static ApplyResult Apply(Document document, LinkProjectData data, IReadOnlyList<ChangeGroup> groups)
        {
            if (document.IsReadOnly)
                return ApplyResult.Stop("The model is open read-only, so nothing can be applied.");

            var tally = new ApplyTally();
            ApplyPlan plan = ApplyPlan.For(data, groups);
            List<PendingValue> pending = Collect(document, data, plan);
            if (pending.Count == 0)
                return tally.Result(plan.Outdated);

            try
            {
                pending = Borrowed(document, pending, tally);
            }
            catch (Exception ex) when (RevitErrors.IsRefusal(ex))
            {
                return ApplyResult.Stop("Revit couldn't borrow the elements from the central model: " + ex.Message);
            }
            if (pending.Count == 0)
                return tally.Result(plan.Outdated);

            string stopped = Write(document, ApplyTexts.UndoName(groups), pending, tally);
            return stopped == null ? tally.Result(plan.Outdated) : ApplyResult.Stop(stopped);
        }

        // The values the model still needs, each with the object or material it belongs to.
        private static List<PendingValue> Collect(Document document, LinkProjectData data, ApplyPlan plan)
        {
            PlanungsdatenbankParseResult values = DatabaseParseResult.From(plan);
            var rules = new DatabaseValueRules(new RevitValues(document));
            var pending = new List<PendingValue>();

            if (values.FamilyData.Count > 0)
            {
                List<Element> elements = ModelReader.LinkableElements(document, data)
                    .Where(element => values.FamilyData.ContainsKey(KeyOf(element) ?? ""))
                    .ToList();
                pending.AddRange(ElementValueWriter.Collect(document, elements, KeyOf, values.FamilyData, values.ParameterDefinitions, rules)
                    .Select(pair => new PendingValue(pair, KeyOf(pair.Item1.Element), false)));
            }

            if (values.MaterialData.Count > 0)
            {
                // Revit materials carry the Materialname; the rows are kept by Material-ID.
                var idsByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (KeyValuePair<string, string> material in values.MaterialNamesById)
                    idsByName[material.Value.Trim()] = material.Key;
                Func<Element, string> materialIdOf = element =>
                {
                    string id;
                    return idsByName.TryGetValue(element.Name.Trim(), out id) ? id : null;
                };
                List<Element> materials;
                using (var collector = new FilteredElementCollector(document))
                    materials = collector.OfClass(typeof(Material)).ToList();
                pending.AddRange(ElementValueWriter.Collect(document, materials, materialIdOf, values.MaterialData, values.MaterialParameterDefinitions, rules)
                    .Select(pair => new PendingValue(pair, materialIdOf(pair.Item1.Element), true)));
            }
            // Only the elements the pane listed: one that got its Bauteil_ID since then is listed next.
            return pending.Where(value => plan.IsListed(value.IsMaterial, value.Key, value.ParameterGuid, value.ElementId.Value)).ToList();
        }

        // Without the elements someone else has, or that changed in the central model.
        private static List<PendingValue> Borrowed(Document document, List<PendingValue> pending, ApplyTally tally)
        {
            List<ElementId> ids = pending.Select(value => value.ElementId).Distinct().ToList();
            HashSet<long> free = new Worksharing(document).Borrow(ids, (id, access, owner) =>
            {
                if (access == ElementAccess.OutOfDate)
                    tally.OutOfDate(id.Value);
                else
                    tally.Borrowed(owner, id.Value);
            });
            return pending.Where(value => free.Contains(value.ElementId.Value)).ToList();
        }

        /// <returns>Why nothing was written; null when it was.</returns>
        private static string Write(Document document, string undoName, List<PendingValue> pending, ApplyTally tally)
        {
            using (var transaction = new Transaction(document, undoName))
            {
                if (transaction.Start() != TransactionStatus.Started)
                    return "Revit couldn't start the change.";
                try
                {
                    List<PendingValue> set = SetAll(document, pending, tally);
                    if (set.Count == 0)
                    {
                        transaction.RollBack();
                        return null;
                    }
                    if (transaction.Commit() != TransactionStatus.Committed)
                        return "Revit undid the change; its own message said why.";

                    foreach (PendingValue value in set)
                        tally.Written(value.Key, value.IsMaterial, value.ParameterGuid, value.ElementId.Value);
                    return null;
                }
                catch (Exception)
                {
                    if (transaction.GetStatus() == TransactionStatus.Started)
                        transaction.RollBack();
                    throw;
                }
            }
        }

        // All at once, which is quick; when Revit throws for one of them, one by one, so that one doesn't stop
        // the others.
        private static List<PendingValue> SetAll(Document document, List<PendingValue> pending, ApplyTally tally)
        {
            using (var attempt = new SubTransaction(document))
            {
                attempt.Start();
                try
                {
                    List<PendingValue> set = Set(pending, tally);
                    attempt.Commit();
                    return set;
                }
                catch (Exception ex) when (RevitErrors.IsRefusal(ex))
                {
                    attempt.RollBack();
                }
            }

            var one = new List<PendingValue>();
            foreach (PendingValue value in pending)
            {
                try
                {
                    one.AddRange(Set(new List<PendingValue> { value }, tally));
                }
                catch (Exception ex) when (RevitErrors.IsRefusal(ex))
                {
                    tally.Refused(value.ParameterName, ex.Message);
                }
            }
            return one;
        }

        private static List<PendingValue> Set(List<PendingValue> values, ApplyTally tally)
        {
            var refused = new HashSet<string>(StringComparer.Ordinal);
            IList<Parameter> notSet = Parameter.SetMultiple(values.Select(value => value.Pair).ToList());
            foreach (Parameter parameter in notSet ?? new Parameter[0])
                refused.Add(PendingValue.IdOf(parameter));

            foreach (PendingValue value in values.Where(value => refused.Contains(value.Id)))
                tally.Refused(value.ParameterName, "Revit didn't take the value");
            return values.Where(value => !refused.Contains(value.Id)).ToList();
        }

        private static string KeyOf(Element element) => ElementKey.Read(element, ExcelFormat.Planungsdatenbank);

        /// <summary>One value on its way into one element or material.</summary>
        private sealed class PendingValue
        {
            public PendingValue(Tuple<Parameter, ParameterValue> pair, string key, bool isMaterial)
            {
                Pair = pair;
                Key = key;
                IsMaterial = isMaterial;
                ElementId = pair.Item1.Element.Id;
                ParameterGuid = pair.Item1.GUID;
                ParameterName = pair.Item1.Definition.Name;
                Id = IdOf(pair.Item1);
            }

            public Tuple<Parameter, ParameterValue> Pair { get; }

            /// <summary>The Bauteil_ID or the Material-ID.</summary>
            public string Key { get; }

            public bool IsMaterial { get; }

            public ElementId ElementId { get; }

            public Guid ParameterGuid { get; }

            public string ParameterName { get; }

            /// <summary>Tells one element's parameter from all others.</summary>
            public string Id { get; }

            public static string IdOf(Parameter parameter) => parameter.Element.Id.Value + "|" + parameter.Id.Value;
        }
    }
}
