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
    /// import uses (<see cref="ElementValueWriter"/>), and sets the elements' materials and the materials' fill patterns it
    /// lists. It borrows the elements first and leaves out those someone else has. It never adds parameters, materials or
    /// patterns and never touches what isn't listed. API context, outside a transaction.
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
            // The elements with an object of the plan, by their Bauteil_ID.
            List<Element> elements = plan.Objects.Count == 0 ? new List<Element>() : ElementsOf(document, data, plan);
            List<PendingValue> pending = Collect(document, elements, plan);
            List<PendingMaterial> materials = CollectMaterials(document, elements, plan, tally);
            List<PendingPattern> patterns = CollectPatterns(document, plan);
            if (pending.Count + materials.Count + patterns.Count == 0)
                return tally.Result(plan.Outdated);

            try
            {
                HashSet<long> free = Borrow(document, pending.Select(value => value.ElementId)
                    .Concat(materials.Select(material => material.ElementId)).Concat(patterns.Select(pattern => pattern.Material.Id)), tally);
                pending = pending.Where(value => free.Contains(value.ElementId.Value)).ToList();
                materials = materials.Where(material => free.Contains(material.ElementId.Value)).ToList();
                patterns = patterns.Where(pattern => free.Contains(pattern.Material.Id.Value)).ToList();
            }
            catch (Exception ex) when (RevitErrors.IsRefusal(ex))
            {
                return ApplyResult.Stop("Revit couldn't borrow the elements from the central model: " + ex.Message);
            }
            if (pending.Count + materials.Count + patterns.Count == 0)
                return tally.Result(plan.Outdated);

            string stopped = Write(document, ApplyTexts.UndoName(groups), pending, materials, patterns, tally);
            return stopped == null ? tally.Result(plan.Outdated) : ApplyResult.Stop(stopped);
        }

        private static List<Element> ElementsOf(Document document, LinkProjectData data, ApplyPlan plan)
        {
            var keys = new HashSet<string>(plan.Objects.Select(planned => planned.Key), StringComparer.OrdinalIgnoreCase);
            return ModelReader.LinkableElements(document, data).Where(element => keys.Contains(KeyOf(element) ?? "")).ToList();
        }

        // The values the model still needs, each with the object or material it belongs to.
        private static List<PendingValue> Collect(Document document, List<Element> elements, ApplyPlan plan)
        {
            PlanungsdatenbankParseResult values = DatabaseParseResult.From(plan);
            var rules = new DatabaseValueRules(new RevitValues(document));
            var pending = new List<PendingValue>();

            if (elements.Count > 0)
            {
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
                pending.AddRange(ElementValueWriter.Collect(document, Materials(document), materialIdOf, values.MaterialData, values.MaterialParameterDefinitions, rules)
                    .Select(pair => new PendingValue(pair, materialIdOf(pair.Item1.Element), true)));
            }
            // Only the elements the pane listed: one that got its Bauteil_ID since then is listed next.
            return pending.Where(value => plan.IsListed(value.IsMaterial, value.Key, value.ParameterGuid, value.ElementId.Value)).ToList();
        }

        // The listed materials the elements aren't made of yet, and the model has.
        private static List<PendingMaterial> CollectMaterials(Document document, List<Element> elements, ApplyPlan plan, ApplyTally tally)
        {
            var pending = new List<PendingMaterial>();
            var plannedByKey = new Dictionary<string, PlannedValues>(StringComparer.OrdinalIgnoreCase);
            foreach (PlannedValues planned in plan.Objects.Where(planned => planned.Material != null))
                plannedByKey[planned.Key] = planned;
            if (plannedByKey.Count == 0)
                return pending;

            var materials = new RevitMaterials(document);
            foreach (Element element in elements)
            {
                PlannedValues planned;
                if (!plannedByKey.TryGetValue(KeyOf(element) ?? "", out planned) || !plan.IsMaterialListed(planned.Key, element.Id.Value))
                    continue;
                try
                {
                    Parameter target = RevitMaterials.ParameterOf(element);
                    if (target == null || target.IsReadOnly || materials.Has(target, planned.Material))
                        continue;
                    Material found = materials.Find(planned.Material);
                    if (found != null)
                        pending.Add(new PendingMaterial(target, planned.Key, found));
                }
                catch (Exception ex) when (RevitErrors.IsRefusal(ex))
                {
                    tally.Refused(ObjectMaterial.ParameterName, ex.Message);
                }
            }
            return pending;
        }

        // The listed fill patterns the materials don't have yet, and the model has.
        private static List<PendingPattern> CollectPatterns(Document document, ApplyPlan plan)
        {
            var pending = new List<PendingPattern>();
            var plannedByName = new Dictionary<string, PlannedValues>(StringComparer.OrdinalIgnoreCase);
            foreach (PlannedValues planned in plan.Materials.Where(planned => planned.Patterns.Count > 0))
                plannedByName[planned.Name.Trim()] = planned;
            if (plannedByName.Count == 0)
                return pending;

            var patterns = new RevitPatterns(document);
            foreach (Material material in Materials(document).Cast<Material>())
            {
                PlannedValues planned;
                if (!plannedByName.TryGetValue(material.Name.Trim(), out planned))
                    continue;
                foreach (PlannedPattern pattern in planned.Patterns)
                {
                    if (!plan.IsListed(planned.Key, pattern.Which, material.Id.Value) || patterns.Has(material, pattern.Which, pattern.Name))
                        continue;
                    FillPatternElement found = patterns.Find(pattern.Which, pattern.Name);
                    if (found != null)
                        pending.Add(new PendingPattern(material, planned.Key, pattern.Which, found));
                }
            }
            return pending;
        }

        private static List<Element> Materials(Document document)
        {
            using (var collector = new FilteredElementCollector(document))
                return collector.OfClass(typeof(Material)).ToList();
        }

        // Without the elements someone else has, or that changed in the central model.
        private static HashSet<long> Borrow(Document document, IEnumerable<ElementId> elementIds, ApplyTally tally)
        {
            List<ElementId> ids = elementIds.Distinct().ToList();
            return new Worksharing(document).Borrow(ids, (id, access, owner) =>
            {
                if (access == ElementAccess.OutOfDate)
                    tally.OutOfDate(id.Value);
                else
                    tally.Borrowed(owner, id.Value);
            });
        }

        /// <returns>Why nothing was written; null when it was.</returns>
        private static string Write(
            Document document, string undoName, List<PendingValue> pending, List<PendingMaterial> materials, List<PendingPattern> patterns, ApplyTally tally)
        {
            using (var transaction = new Transaction(document, undoName))
            {
                if (transaction.Start() != TransactionStatus.Started)
                    return "Revit couldn't start the change.";
                try
                {
                    List<PendingValue> set = ValueSetter.SetAll(document, pending, tally);
                    List<PendingMaterial> setMaterials = SetMaterials(materials, tally);
                    List<PendingPattern> setPatterns = SetPatterns(patterns, tally);
                    if (set.Count + setMaterials.Count + setPatterns.Count == 0)
                    {
                        transaction.RollBack();
                        return null;
                    }
                    if (transaction.Commit() != TransactionStatus.Committed)
                        return "Revit undid the change; its own message said why.";

                    foreach (PendingValue value in set)
                        tally.Written(value.Key, value.IsMaterial, value.ParameterGuid, value.ElementId.Value);
                    foreach (PendingMaterial material in setMaterials)
                        tally.WrittenMaterial(material.BauteilId, material.ElementId.Value);
                    foreach (PendingPattern pattern in setPatterns)
                        tally.WrittenPattern(pattern.MaterialId, pattern.Which, pattern.Material.Id.Value);
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

        // One by one: an element whose material Revit refuses doesn't stop the others.
        private static List<PendingMaterial> SetMaterials(List<PendingMaterial> materials, ApplyTally tally)
        {
            var set = new List<PendingMaterial>();
            foreach (PendingMaterial material in materials)
            {
                try
                {
                    if (material.Target.Set(material.Material.Id))
                        set.Add(material);
                    else
                        tally.Refused(ObjectMaterial.ParameterName, "Revit didn't take the material");
                }
                catch (Exception ex) when (RevitErrors.IsRefusal(ex))
                {
                    tally.Refused(ObjectMaterial.ParameterName, ex.Message);
                }
            }
            return set;
        }

        // One by one: a pattern Revit refuses doesn't stop the others.
        private static List<PendingPattern> SetPatterns(List<PendingPattern> patterns, ApplyTally tally)
        {
            var set = new List<PendingPattern>();
            foreach (PendingPattern pattern in patterns)
            {
                try
                {
                    if (RevitPatterns.Set(pattern.Material, pattern.Which, pattern.Pattern))
                        set.Add(pattern);
                }
                catch (Exception ex) when (RevitErrors.IsRefusal(ex))
                {
                    tally.Refused(MaterialPatterns.Name(pattern.Which), ex.Message);
                }
            }
            return set;
        }

        private static string KeyOf(Element element) => ElementKey.Read(element, ExcelFormat.Planungsdatenbank);

        /// <summary>One material on its way into one element.</summary>
        private sealed class PendingMaterial
        {
            public PendingMaterial(Parameter target, string bauteilId, Material material)
            {
                Target = target;
                ElementId = target.Element.Id;
                BauteilId = bauteilId;
                Material = material;
            }

            public Parameter Target { get; }

            public ElementId ElementId { get; }

            public string BauteilId { get; }

            public Material Material { get; }
        }

        /// <summary>One fill pattern on its way into one material.</summary>
        private sealed class PendingPattern
        {
            public PendingPattern(Material material, string materialId, MaterialPattern which, FillPatternElement pattern)
            {
                Material = material;
                MaterialId = materialId;
                Which = which;
                Pattern = pattern;
            }

            public Material Material { get; }

            /// <summary>The database's Material-ID.</summary>
            public string MaterialId { get; }

            public MaterialPattern Which { get; }

            public FillPatternElement Pattern { get; }
        }
    }
}
