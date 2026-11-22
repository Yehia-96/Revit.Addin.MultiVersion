using Autodesk.Revit.DB;
using System;
using System.Linq;

namespace Revit.Addin._2027.DatabaseLink
{
    /// <summary>
    /// Where a project parameter is used besides its values: schedules and view filters. Deleting the
    /// parameter would quietly drop it from them, so Replace leaves such a parameter alone.
    /// </summary>
    internal static class ParameterUses
    {
        /// <summary>Null when nothing uses it; otherwise where it's used, in a few words.</summary>
        public static string Describe(Document document, long parameterElementId)
        {
            var id = new ElementId(parameterElementId);
            int schedules;
            int filters;
            try
            {
                schedules = new FilteredElementCollector(document).OfClass(typeof(ViewSchedule))
                    .Cast<ViewSchedule>().Count(schedule => Uses(schedule, id));
                filters = new FilteredElementCollector(document).OfClass(typeof(ParameterFilterElement))
                    .Cast<ParameterFilterElement>().Count(filter => Uses(filter.GetElementFilter(), id));
            }
            catch (Exception ex) when (RevitErrors.IsRefusal(ex))
            {
                // Unsure means no: better a parameter left alone than a schedule losing a column.
                return "Revit couldn't tell where it's used (" + ex.Message.TrimEnd('.') + "); replace it by hand";
            }

            if (schedules == 0 && filters == 0)
                return null;
            string where = Count(schedules, "schedule")
                + (schedules > 0 && filters > 0 ? " and " : "")
                + Count(filters, "view filter");
            return "used in " + where + "; replace it by hand so they keep it";
        }

        private static bool Uses(ViewSchedule schedule, ElementId id)
        {
            ScheduleDefinition definition = schedule.Definition;
            if (definition == null)
                return false;
            return definition.GetFieldOrder().Any(field => definition.GetField(field).ParameterId == id);
        }

        private static bool Uses(ElementFilter filter, ElementId id)
        {
            var byParameter = filter as ElementParameterFilter;
            if (byParameter != null)
                return byParameter.GetRules().Any(rule => rule.GetRuleParameter() == id);
            var logical = filter as ElementLogicalFilter;
            return logical != null && logical.GetFilters().Any(inner => Uses(inner, id));
        }

        private static string Count(int count, string what) =>
            count == 0 ? "" : count + " " + what + (count == 1 ? "" : "s");
    }
}
