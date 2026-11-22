using Autodesk.Revit.DB;
using MH.RevitTools.Database.Parameters;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit.Addin._2027.DatabaseLink
{
    /// <summary>
    /// Finds the schedules and view filters that use the model's parameters Replace would delete. Deleting a
    /// parameter takes it out of them, so the dialog asks once more before such a parameter goes
    /// (<see cref="ReplaceUses"/>). It only reads the model.
    /// </summary>
    internal static class ParameterUseScan
    {
        public static ReplaceUses Find(Document document, IReadOnlyList<ParameterToReplace> toReplace)
        {
            var ids = new HashSet<long>(toReplace.Select(item => item.Old.ElementId).Where(id => id != 0));
            var uses = new List<ParameterUse>();
            if (ids.Count == 0)
                return ReplaceUses.Found(toReplace, uses);

            try
            {
                var schedules = new FilteredElementCollector(document).OfClass(typeof(ViewSchedule)).Cast<ViewSchedule>();
                foreach (ViewSchedule schedule in schedules)
                {
                    foreach (long id in FieldParameters(schedule.Definition).Where(ids.Contains).Distinct())
                        uses.Add(new ParameterUse(id, ParameterUseKind.Schedule, schedule.Name));
                }

                var filters = new FilteredElementCollector(document).OfClass(typeof(ParameterFilterElement)).Cast<ParameterFilterElement>();
                foreach (ParameterFilterElement filter in filters)
                {
                    foreach (long id in RuleParameters(filter.GetElementFilter()).Where(ids.Contains).Distinct())
                        uses.Add(new ParameterUse(id, ParameterUseKind.ViewFilter, filter.Name));
                }
            }
            catch (Exception ex)
            {
                // Any failure, e.g. on one odd schedule: unsure means every one counts as used, and the second
                // question says so. The scan only reads, so nothing needs undoing.
                return ReplaceUses.Unknown(toReplace, ex.Message.TrimEnd('.'));
            }
            return ReplaceUses.Found(toReplace, uses);
        }

        // Every parameter a schedule shows, combined fields and an embedded schedule's fields included.
        private static IEnumerable<long> FieldParameters(ScheduleDefinition definition)
        {
            if (definition == null)
                yield break;

            foreach (ScheduleFieldId fieldId in definition.GetFieldOrder())
            {
                ScheduleField field = definition.GetField(fieldId);
                yield return field.ParameterId.Value;
                if (field.IsCombinedParameterField)
                {
                    foreach (TableCellCombinedParameterData part in field.GetCombinedParameters())
                        yield return part.ParamId.Value;
                }
            }

            if (definition.HasEmbeddedSchedule)
            {
                foreach (long id in FieldParameters(definition.EmbeddedDefinition))
                    yield return id;
            }
        }

        // Every parameter a view filter's rules test, through its and/or groups.
        private static IEnumerable<long> RuleParameters(ElementFilter filter)
        {
            var byParameter = filter as ElementParameterFilter;
            if (byParameter != null)
                return byParameter.GetRules().Select(rule => rule.GetRuleParameter().Value);
            var logical = filter as ElementLogicalFilter;
            return logical == null ? Enumerable.Empty<long>() : logical.GetFilters().SelectMany(RuleParameters);
        }
    }
}
