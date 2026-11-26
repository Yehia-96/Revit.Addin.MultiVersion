using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using MH.RevitTools.Database.Link;

namespace MH.RevitTools.Database.Changes
{
    /// <summary>A material of the project that the model doesn't have. Immutable.</summary>
    public sealed class MissingMaterial
    {
        internal MissingMaterial(string materialId, string name)
        {
            MaterialId = materialId;
            Name = name;
        }

        /// <summary>E.g. MB-1761-01-BET-0031.</summary>
        public string MaterialId { get; }

        /// <summary>The Materialname, by which Revit would know it.</summary>
        public string Name { get; }
    }

    /// <summary>
    /// What one click on Create makes: the materials the pane listed as missing, as the database has them now, and no
    /// others. One that's gone from the database or has another name by then is left out; the pane lists it anew.
    /// Immutable.
    /// </summary>
    public sealed class CreationPlan
    {
        private CreationPlan(IEnumerable<PlannedMaterial> materials, int outdated)
        {
            Materials = new ReadOnlyCollection<PlannedMaterial>(materials.ToList());
            Outdated = outdated;
        }

        /// <summary>In the database's order.</summary>
        public IReadOnlyList<PlannedMaterial> Materials { get; }

        /// <summary>How many listed materials are left out, as the database has changed by now.</summary>
        public int Outdated { get; }

        /// <param name="data">The database's project as last read.</param>
        /// <param name="listed">The materials to create, as the pane listed them.</param>
        public static CreationPlan For(LinkProjectData data, IEnumerable<MissingMaterial> listed)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            if (listed == null)
                throw new ArgumentNullException(nameof(listed));

            var wanted = new Dictionary<string, MissingMaterial>(StringComparer.OrdinalIgnoreCase);
            foreach (MissingMaterial material in listed.Where(material => material != null))
                wanted[material.MaterialId.Trim()] = material;
            var parameters = new Dictionary<int, LinkParameter>();
            foreach (LinkParameter parameter in data.Parameters)
                parameters[parameter.ParameterId] = parameter;
            ILookup<string, LinkMaterialValue> values = data.MaterialValues.ToLookup(value => (value.MaterialId ?? "").Trim(), StringComparer.OrdinalIgnoreCase);

            var planned = new List<PlannedMaterial>();
            foreach (LinkMaterial material in data.Materials)
            {
                string id = material.MaterialId.Trim();
                MissingMaterial missing;
                if (!wanted.TryGetValue(id, out missing)
                    || !string.Equals(missing.Name, material.Name.Trim(), StringComparison.OrdinalIgnoreCase)
                    || string.IsNullOrWhiteSpace(material.Group))
                    continue;
                wanted.Remove(id);
                planned.Add(new PlannedMaterial(material, Values(values[id], parameters)));
            }
            return new CreationPlan(planned, wanted.Count);
        }

        // As Apply would write them: one value per parameter, none where the database has several.
        private static IEnumerable<PlannedValue> Values(IEnumerable<LinkMaterialValue> values, Dictionary<int, LinkParameter> parameters)
        {
            foreach (IGrouping<int, LinkMaterialValue> candidates in values.GroupBy(value => value.ParameterId))
            {
                LinkParameter parameter;
                string value = candidates.First().Value ?? "";
                if (parameters.TryGetValue(candidates.Key, out parameter)
                    && candidates.Select(candidate => candidate.Value).Distinct(SameText.Comparer).Count() == 1
                    && ApplyPlan.MayWrite(parameter, value, true))
                    yield return new PlannedValue(parameter, value);
            }
        }
    }

    /// <summary>A material to create. Immutable.</summary>
    public sealed class PlannedMaterial
    {
        internal PlannedMaterial(LinkMaterial material, IEnumerable<PlannedValue> values)
        {
            MaterialId = material.MaterialId;
            Name = material.Name.Trim();
            Group = material.Group.Trim();
            SurfacePattern = MaterialPatterns.Of(material, MaterialPattern.Surface);
            CutPattern = MaterialPatterns.Of(material, MaterialPattern.Cut);
            Values = new ReadOnlyCollection<PlannedValue>(values.ToList());
        }

        public string MaterialId { get; }

        public string Name { get; }

        /// <summary>The Materialgruppe, e.g. Beton, which its class and look go by.</summary>
        public string Group { get; }

        /// <summary>The surface fill pattern's name; empty for none.</summary>
        public string SurfacePattern { get; }

        /// <summary>The cut fill pattern's name; empty for none.</summary>
        public string CutPattern { get; }

        public IReadOnlyList<PlannedValue> Values { get; }
    }
}
