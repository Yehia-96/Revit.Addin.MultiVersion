using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using MH.RevitTools.Database.Link;

namespace MH.RevitTools.Database.Changes
{
    /// <summary>
    /// What one click on Apply writes: the values the pane listed, into the elements it listed them for, and
    /// nothing else. A value that isn't the database's any more by the time Apply runs is left out; the pane
    /// lists the new one. Placeholders and what identifies an element are never in it, whatever was listed.
    /// Immutable.
    /// </summary>
    public sealed class ApplyPlan
    {
        private readonly HashSet<string> _listed;

        private ApplyPlan(IEnumerable<PlannedValues> objects, IEnumerable<PlannedValues> materials, HashSet<string> listed, int outdated)
        {
            Objects = new ReadOnlyCollection<PlannedValues>(objects.ToList());
            Materials = new ReadOnlyCollection<PlannedValues>(materials.ToList());
            _listed = listed;
            Outdated = outdated;
        }

        /// <summary>The values per object, by Bauteil_ID.</summary>
        public IReadOnlyList<PlannedValues> Objects { get; }

        /// <summary>The values per material, by Material-ID.</summary>
        public IReadOnlyList<PlannedValues> Materials { get; }

        /// <summary>How many listed values are left out, as the database has another value by now.</summary>
        public int Outdated { get; }

        public bool IsEmpty => Objects.Count == 0 && Materials.Count == 0;

        /// <summary>
        /// True when the pane listed the value for that element (or material). An element that got its
        /// Bauteil_ID only after the list was made isn't; the pane lists it next.
        /// </summary>
        public bool IsListed(bool isMaterial, string key, Guid parameter, long elementId) =>
            _listed.Contains(ListedKey(isMaterial, key, parameter, elementId));

        /// <param name="data">The database's values as last read.</param>
        /// <param name="groups">The groups to apply, as the pane listed them.</param>
        public static ApplyPlan For(LinkProjectData data, IEnumerable<ChangeGroup> groups)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            if (groups == null)
                throw new ArgumentNullException(nameof(groups));

            var parameters = new Dictionary<int, LinkParameter>();
            foreach (LinkParameter parameter in data.Parameters)
                parameters[parameter.ParameterId] = parameter;
            Dictionary<string, string> objectValues = Values(data.ObjectValues.Select(v => Tuple.Create(v.BauteilId, v.ParameterId, v.Value)));
            Dictionary<string, string> materialValues = Values(data.MaterialValues.Select(v => Tuple.Create(v.MaterialId, v.ParameterId, v.Value)));

            var objects = new List<PlannedValues>();
            var materials = new List<PlannedValues>();
            var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int outdated = 0;
            foreach (ChangeGroup group in groups.Where(group => group != null))
            {
                Dictionary<string, string> current = group.IsMaterial ? materialValues : objectValues;
                var values = new List<PlannedValue>();
                foreach (ChangeRow row in group.Rows)
                {
                    LinkParameter parameter;
                    string now;
                    if (!parameters.TryGetValue(row.ParameterId, out parameter)
                        || !current.TryGetValue(KeyOf(group.Key, row.ParameterId), out now)
                        || !string.Equals(now, row.Value, StringComparison.Ordinal))
                    {
                        outdated++;
                        continue;
                    }
                    if (!MayWrite(parameter, row.Value, group.IsMaterial))
                        continue;
                    values.Add(new PlannedValue(parameter, row.Value));
                    foreach (long id in row.ElementIds)
                        listed.Add(ListedKey(group.IsMaterial, group.Key, parameter.Guid, id));
                }
                if (values.Count > 0)
                    (group.IsMaterial ? materials : objects).Add(new PlannedValues(group.Key, group.Name, values));
            }
            return new ApplyPlan(objects, materials, listed, outdated);
        }

        // ChangeFinder never lists these. Checked here too, as this is the last step before the model.
        private static bool MayWrite(LinkParameter parameter, string value, bool isMaterial) =>
            parameter.Guid != Guid.Empty
            && parameter.Target == (isMaterial ? LinkTarget.Material : LinkTarget.Object)
            && !ChangeFinder.IsIdentity(parameter)
            && !Placeholders.IsPlaceholder(value);

        private static string ListedKey(bool isMaterial, string key, Guid parameter, long elementId) =>
            (isMaterial ? "M|" : "O|") + (key ?? "").Trim() + "|" + parameter.ToString("N") + "|" + elementId;

        // The first value counts where a key repeats, as everywhere in the link.
        private static Dictionary<string, string> Values(IEnumerable<Tuple<string, int, string>> values)
        {
            var byKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Tuple<string, int, string> value in values)
            {
                string key = KeyOf(value.Item1, value.Item2);
                if (!byKey.ContainsKey(key))
                    byKey.Add(key, value.Item3 ?? "");
            }
            return byKey;
        }

        private static string KeyOf(string id, int parameterId) => (id ?? "").Trim() + "|" + parameterId;
    }

    /// <summary>The values Apply writes to one object or material. Immutable.</summary>
    public sealed class PlannedValues
    {
        internal PlannedValues(string key, string name, IEnumerable<PlannedValue> values)
        {
            Key = key;
            Name = name;
            Values = new ReadOnlyCollection<PlannedValue>(values.ToList());
        }

        /// <summary>The Bauteil_ID or the Material-ID.</summary>
        public string Key { get; }

        /// <summary>The object's name, or the material's, by which Revit knows it.</summary>
        public string Name { get; }

        public IReadOnlyList<PlannedValue> Values { get; }
    }

    /// <summary>One value to write. Immutable.</summary>
    public sealed class PlannedValue
    {
        internal PlannedValue(LinkParameter parameter, string value)
        {
            Parameter = parameter;
            Value = value;
        }

        public LinkParameter Parameter { get; }

        /// <summary>As the database stores it.</summary>
        public string Value { get; }
    }
}
