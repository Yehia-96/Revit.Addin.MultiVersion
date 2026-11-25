using MH.RevitTools.Database.Changes;
using MH.RevitTools.Database.Link;
using Revit.Addin._2027.Services;
using Revit.Addin._2027.Services.Planungsdatenbank;
using System;
using System.Collections.Generic;

namespace Revit.Addin._2027.DatabaseLink.Apply
{
    /// <summary>
    /// Puts the database's values into the shape the Excel reader produces
    /// (<see cref="PlanungsdatenbankParseResult"/>), so one writer serves both. The parameters also carry their
    /// GUID, by which the link finds them.
    /// </summary>
    internal static class DatabaseParseResult
    {
        public static PlanungsdatenbankParseResult From(ApplyPlan plan)
        {
            if (plan == null)
                throw new ArgumentNullException(nameof(plan));

            var result = new PlanungsdatenbankParseResult();
            foreach (PlannedValues obj in plan.Objects)
                result.FamilyData[obj.Key] = Values(obj.Values, result.ParameterDefinitions, ParamMetaData.ParamTarget.FamilyElement);
            foreach (PlannedValues material in plan.Materials)
            {
                result.MaterialData[material.Key] = Values(material.Values, result.MaterialParameterDefinitions, ParamMetaData.ParamTarget.Material);
                result.MaterialNamesById[material.Key] = material.Name;
            }
            return result;
        }

        /// <summary>The same for materials that are being created.</summary>
        public static PlanungsdatenbankParseResult From(IEnumerable<PlannedMaterial> materials)
        {
            if (materials == null)
                throw new ArgumentNullException(nameof(materials));

            var result = new PlanungsdatenbankParseResult();
            foreach (PlannedMaterial material in materials)
            {
                result.MaterialData[material.MaterialId] = Values(material.Values, result.MaterialParameterDefinitions, ParamMetaData.ParamTarget.Material);
                result.MaterialNamesById[material.MaterialId] = material.Name;
            }
            return result;
        }

        private static Dictionary<string, string> Values(
            IEnumerable<PlannedValue> planned, Dictionary<string, ParamMetaData> definitions, ParamMetaData.ParamTarget target)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (PlannedValue value in planned)
                values[NameIn(definitions, value.Parameter, target)] = value.Value;
            return values;
        }

        // The parameter's Revit name. Names only repeat across categories, where the GUID tells them apart.
        private static string NameIn(Dictionary<string, ParamMetaData> definitions, LinkParameter parameter, ParamMetaData.ParamTarget target)
        {
            string name = parameter.RevitName;
            ParamMetaData known;
            if (definitions.TryGetValue(name, out known) && known.SharedGuid != parameter.Guid)
                name += " " + parameter.Guid.ToString("D");
            if (!definitions.ContainsKey(name))
            {
                definitions.Add(name, new ParamMetaData(name, parameter.GroupCode, parameter.DataType)
                {
                    Unit = parameter.Unit,
                    Target = target,
                    SharedGuid = parameter.Guid,
                });
            }
            return name;
        }
    }
}
