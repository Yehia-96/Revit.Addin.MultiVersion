using Autodesk.Revit.DB;
using MH.RevitTools.Database.Changes;
using MH.RevitTools.Database.Checking;
using MH.RevitTools.Database.Link;
using Revit.Addin._2027.Dtos;
using Revit.Addin._2027.Helpers;
using Revit.Addin._2027.Services;
using Revit.Addin._2027.Services.Planungsdatenbank;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit.Addin._2027.DatabaseLink.Apply
{
    /// <summary>
    /// Creates the project's materials the model doesn't have, in one undo step: each with the Excel import's own code
    /// (<see cref="MaterialCreation.Create"/>: class, look, structural properties), then the database's fill patterns
    /// where the model has them, and its values. It never touches a material that's there. API context, outside a
    /// transaction.
    /// </summary>
    internal static class MaterialCreator
    {
        /// <param name="data">The database's project as last read.</param>
        /// <param name="listed">The materials to create, as the pane listed them.</param>
        public static ApplyResult Create(Document document, LinkProjectData data, IReadOnlyList<MissingMaterial> listed)
        {
            if (document.IsReadOnly)
                return ApplyResult.StopCreating("The model is open read-only, so nothing can be created.");

            var tally = new ApplyTally();
            CreationPlan plan = CreationPlan.For(data, listed);
            List<Material> existing = Materials(document);
            var names = new HashSet<string>(existing.Select(material => material.Name.Trim()), StringComparer.OrdinalIgnoreCase);
            List<PlannedMaterial> planned = plan.Materials.Where(material => !names.Contains(material.Name)).ToList();
            if (planned.Count == 0)
                return tally.CreationResult(plan.Outdated);

            using (var transaction = new Transaction(document, ApplyTexts.CreateUndoName(planned.Count)))
            {
                if (transaction.Start() != TransactionStatus.Started)
                    return ApplyResult.StopCreating("Revit couldn't start the change.");
                try
                {
                    Dictionary<long, PlannedMaterial> made = Make(document, planned, existing, tally);
                    if (made.Count == 0)
                    {
                        transaction.RollBack();
                        return tally.CreationResult(plan.Outdated);
                    }

                    // Only now do the new materials have the model's material parameters.
                    document.Regenerate();
                    FillValues(document, made, tally);
                    SetPatterns(document, made, tally);
                    transaction.SetName(ApplyTexts.CreateUndoName(made.Count));
                    if (transaction.Commit() != TransactionStatus.Committed)
                        return ApplyResult.StopCreating("Revit undid the change; its own message said why.");

                    foreach (long id in made.Keys)
                        tally.Created(id);
                    return tally.CreationResult(plan.Outdated);
                }
                catch (Exception)
                {
                    if (transaction.GetStatus() == TransactionStatus.Started)
                        transaction.RollBack();
                    throw;
                }
            }
        }

        // Each on its own, so that one Revit refuses, for its name say, doesn't stop the others.
        private static Dictionary<long, PlannedMaterial> Make(
            Document document, List<PlannedMaterial> planned, List<Material> existing, ApplyTally tally)
        {
            var made = new Dictionary<long, PlannedMaterial>();
            foreach (PlannedMaterial material in planned)
            {
                using (var attempt = new SubTransaction(document))
                {
                    attempt.Start();
                    try
                    {
                        var dto = new MaterialsDto(material.Name, material.Group, material.CutPattern, material.SurfacePattern);
                        Material created = MaterialCreation.Create(document, dto, material.Name, existing, new ImportReport(), true);
                        attempt.Commit();
                        existing.Add(created);
                        made.Add(created.Id.Value, material);
                    }
                    catch (Exception ex) when (RevitErrors.IsRefusal(ex))
                    {
                        if (attempt.GetStatus() == TransactionStatus.Started)
                            attempt.RollBack();
                        tally.Refused(material.Name, ex.Message);
                    }
                }
            }
            return made;
        }

        // By the same loop and rules as Apply. What can't go in, the pane lists at its next comparison.
        private static void FillValues(Document document, Dictionary<long, PlannedMaterial> made, ApplyTally tally)
        {
            PlanungsdatenbankParseResult values = DatabaseParseResult.From(made.Values);
            Func<Element, string> materialIdOf = element => made[element.Id.Value].MaterialId;
            List<Element> materials = made.Keys.Select(id => document.GetElement(new ElementId(id))).ToList();
            List<PendingValue> pending = ElementValueWriter.Collect(
                    document, materials, materialIdOf, values.MaterialData, values.MaterialParameterDefinitions,
                    new DatabaseValueRules(new RevitValues(document)))
                .Select(pair => new PendingValue(pair, materialIdOf(pair.Item1.Element), true))
                .ToList();
            ValueSetter.SetAll(document, pending, tally);
        }

        // Over the look's own. One the model doesn't have is listed under "Not applied" at the next comparison.
        private static void SetPatterns(Document document, Dictionary<long, PlannedMaterial> made, ApplyTally tally)
        {
            var patterns = new RevitPatterns(document);
            foreach (KeyValuePair<long, PlannedMaterial> material in made)
            {
                SetPattern(patterns, (Material)document.GetElement(new ElementId(material.Key)), MaterialPattern.Surface, material.Value.SurfacePattern, tally);
                SetPattern(patterns, (Material)document.GetElement(new ElementId(material.Key)), MaterialPattern.Cut, material.Value.CutPattern, tally);
            }
        }

        private static void SetPattern(RevitPatterns patterns, Material material, MaterialPattern which, string name, ApplyTally tally)
        {
            FillPatternElement pattern = name.Length == 0 ? null : patterns.Find(which, name);
            if (pattern == null)
                return;
            try
            {
                RevitPatterns.Set(material, which, pattern);
            }
            catch (Exception ex) when (RevitErrors.IsRefusal(ex))
            {
                tally.Refused(MaterialPatterns.Name(which), ex.Message);
            }
        }

        private static List<Material> Materials(Document document)
        {
            using (var collector = new FilteredElementCollector(document))
                return collector.OfClass(typeof(Material)).Cast<Material>().ToList();
        }
    }
}
