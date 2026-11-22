using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MH.RevitTools.Database.Link;

namespace MH.RevitTools.Database.Changes
{
    /// <summary>
    /// Compares the database's values for a project with the model: what the pane lists as "changes from
    /// the database". Elements meet their object by Bauteil_ID, materials theirs by name (Revit materials
    /// carry the Materialname), parameters are found by GUID. Placeholders ([Modeller], [ET], -, TBT and
    /// empty values) are never changes, and neither is the Bauteil_ID itself, the key. Only reads; the Revit
    /// side answers for each value through <see cref="CompareValue"/>.
    /// </summary>
    public static class ChangeFinder
    {
        // The dictionary's names for the parts of an object's name: Bauteil "Schlitzrinne", Teilelement "links".
        private const string NamePart = "Bauteil";
        private const string SubNamePart = "Teilelement";
        private const string NameSet = "BAU";

        // The key the elements are matched by; the Excel route accepts both spellings too.
        private static readonly HashSet<string> KeyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Bauteil_ID", "Bauteil-ID",
        };

        public static ChangeReport Find(
            LinkProjectData data,
            IEnumerable<ModelElement> elements,
            IEnumerable<ModelMaterial> materials,
            CompareValue compare)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            if (compare == null)
                throw new ArgumentNullException(nameof(compare));

            var dictionary = new Dictionary<int, LinkParameter>();
            var order = new Dictionary<int, int>();
            foreach (LinkParameter parameter in data.Parameters)
            {
                if (dictionary.ContainsKey(parameter.ParameterId))
                    continue;
                dictionary.Add(parameter.ParameterId, parameter);
                order.Add(parameter.ParameterId, order.Count);
            }

            var finder = new Finder(dictionary, order, compare);
            List<ModelElement> modelElements = (elements ?? Enumerable.Empty<ModelElement>()).Where(e => e != null).ToList();
            ModelCounts counts = finder.Objects(data.ObjectValues, modelElements);
            finder.Materials(data.Materials, data.MaterialValues, (materials ?? Enumerable.Empty<ModelMaterial>()).Where(m => m != null).ToList());

            DateTime? newest = null;
            string newestBy = null;
            foreach (Tuple<DateTime?, string> change in data.ObjectValues.Select(v => Tuple.Create(v.ChangedAtUtc, v.ChangedBy))
                         .Concat(data.MaterialValues.Select(v => Tuple.Create(v.ChangedAtUtc, v.ChangedBy))))
            {
                if (change.Item1.HasValue && (!newest.HasValue || change.Item1.Value > newest.Value))
                {
                    newest = change.Item1;
                    newestBy = change.Item2;
                }
            }

            List<ChangeGroup> groups = finder.Groups
                .OrderByDescending(group => group.NewestChangeUtc ?? DateTime.MinValue)
                .ThenBy(group => group.IsMaterial)
                .ThenBy(group => group.Key, NaturalOrder.Instance)
                .ToList();
            return new ChangeReport(groups, counts, finder.Missing.OrderBy(name => name, StringComparer.OrdinalIgnoreCase),
                finder.Problems, newest, newestBy);
        }

        /// <summary>
        /// The database's value as the pane shows it: with its unit ("0.3 m", "12 m²", "90°"), and Yes or No
        /// for a Boolean stored as true/false.
        /// </summary>
        public static string Display(LinkParameter parameter, string value)
        {
            string text = (value ?? "").Trim();
            if (parameter == null || text.Length == 0)
                return text;

            if (string.Equals(parameter.DataType, "Boolean", StringComparison.OrdinalIgnoreCase))
            {
                bool? yes = YesNo(text);
                return yes.HasValue ? (yes.Value ? "Yes" : "No") : text;
            }

            switch ((parameter.Unit ?? "").Trim().ToLowerInvariant())
            {
                case "":
                    return text;
                case "m2":
                case "m²":
                    return text + " m²";
                case "m3":
                case "m³":
                    return text + " m³";
                case "grad":
                case "°":
                    return text + "°";
                default:
                    return text + " " + parameter.Unit.Trim();
            }
        }

        /// <summary>True, false, or null when the text is neither (true/false, 1/0, yes/no, ja/nein).</summary>
        public static bool? YesNo(string text)
        {
            switch ((text ?? "").Trim().ToLowerInvariant())
            {
                case "true":
                case "1":
                case "yes":
                case "ja":
                    return true;
                case "false":
                case "0":
                case "no":
                case "nein":
                    return false;
                default:
                    return null;
            }
        }

        // The model's side of a change: its value, or "(empty)"; or how many values the elements hold.
        private static string FromText(IReadOnlyCollection<string> modelTexts)
        {
            List<string> distinct = modelTexts.Select(text => string.IsNullOrWhiteSpace(text) ? "" : text.Trim())
                .Distinct(StringComparer.Ordinal).ToList();
            if (distinct.Count > 1)
                return distinct.Count.ToString(CultureInfo.InvariantCulture) + " different values";
            return distinct.Count == 0 || distinct[0].Length == 0 ? "(empty)" : distinct[0];
        }

        private static bool IsKey(LinkParameter parameter) =>
            KeyNames.Contains(parameter.RevitName) || KeyNames.Contains(parameter.Name);

        // Collects the groups, the missing parameters and the problems while it compares.
        private sealed class Finder
        {
            private const int MaxProblems = 20;

            private readonly Dictionary<int, LinkParameter> _dictionary;
            private readonly Dictionary<int, int> _order;
            private readonly CompareValue _compare;
            private readonly HashSet<string> _missing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            private readonly List<string> _problems = new List<string>();
            private readonly HashSet<string> _problemSet = new HashSet<string>(StringComparer.Ordinal);

            public Finder(Dictionary<int, LinkParameter> dictionary, Dictionary<int, int> order, CompareValue compare)
            {
                _dictionary = dictionary;
                _order = order;
                _compare = compare;
            }

            public List<ChangeGroup> Groups { get; } = new List<ChangeGroup>();

            public IEnumerable<string> Missing => _missing;

            public IEnumerable<string> Problems => _problems;

            public ModelCounts Objects(IReadOnlyList<LinkObjectValue> values, List<ModelElement> elements)
            {
                Dictionary<string, List<LinkObjectValue>> valuesByObject = values
                    .Where(value => !string.IsNullOrWhiteSpace(value.BauteilId))
                    .GroupBy(value => value.BauteilId.Trim(), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

                var linked = new List<long>();
                var withoutId = new List<long>();
                var notInProject = new List<long>();
                var elementsByObject = new Dictionary<string, List<ModelElement>>(StringComparer.OrdinalIgnoreCase);
                foreach (ModelElement element in elements)
                {
                    if (element.BauteilId == null)
                    {
                        withoutId.Add(element.Id);
                        continue;
                    }
                    if (!valuesByObject.ContainsKey(element.BauteilId))
                    {
                        notInProject.Add(element.Id);
                        continue;
                    }
                    linked.Add(element.Id);
                    List<ModelElement> list;
                    if (!elementsByObject.TryGetValue(element.BauteilId, out list))
                    {
                        list = new List<ModelElement>();
                        elementsByObject.Add(element.BauteilId, list);
                    }
                    list.Add(element);
                }

                foreach (KeyValuePair<string, List<LinkObjectValue>> obj in valuesByObject)
                {
                    List<ModelElement> objectElements;
                    if (!elementsByObject.TryGetValue(obj.Key, out objectElements))
                        continue;

                    // Values whose parameter goes on elements; an object's material references (e.g. the
                    // Material-ID of a material parameter) live on the material in Revit, not on the element.
                    var rows = new List<ChangeRow>();
                    foreach (LinkObjectValue value in obj.Value)
                    {
                        LinkParameter parameter;
                        if (!_dictionary.TryGetValue(value.ParameterId, out parameter)
                            || parameter.Target != LinkTarget.Object || IsKey(parameter))
                            continue;
                        ChangeRow row = Compare(parameter, value.Value, value.ChangedAtUtc, value.ChangedBy,
                            objectElements.Select(element => element.Id));
                        if (row != null)
                            rows.Add(row);
                    }
                    if (rows.Count > 0)
                        Groups.Add(new ChangeGroup(obj.Key, ObjectName(obj.Value, objectElements), false,
                            objectElements.Select(element => element.Id), Ordered(rows)));
                }

                return new ModelCounts(linked, withoutId, notInProject);
            }

            public void Materials(IReadOnlyList<LinkMaterial> materials, IReadOnlyList<LinkMaterialValue> values, List<ModelMaterial> modelMaterials)
            {
                Dictionary<string, List<long>> modelByName = modelMaterials
                    .Where(material => material.Name.Length > 0)
                    .GroupBy(material => material.Name, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.Select(m => m.Id).ToList(), StringComparer.OrdinalIgnoreCase);
                Dictionary<string, List<LinkMaterialValue>> valuesByMaterial = values
                    .Where(value => !string.IsNullOrWhiteSpace(value.MaterialId))
                    .GroupBy(value => value.MaterialId.Trim(), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (LinkMaterial material in materials)
                {
                    List<long> modelIds;
                    List<LinkMaterialValue> materialValues;
                    if (!seen.Add(material.MaterialId)
                        || !modelByName.TryGetValue(material.Name.Trim(), out modelIds)
                        || !valuesByMaterial.TryGetValue(material.MaterialId.Trim(), out materialValues))
                        continue;

                    var rows = new List<ChangeRow>();
                    foreach (LinkMaterialValue value in materialValues)
                    {
                        LinkParameter parameter;
                        if (!_dictionary.TryGetValue(value.ParameterId, out parameter) || parameter.Target != LinkTarget.Material)
                            continue;
                        ChangeRow row = Compare(parameter, value.Value, value.ChangedAtUtc, value.ChangedBy, modelIds);
                        if (row != null)
                            rows.Add(row);
                    }
                    if (rows.Count > 0)
                        Groups.Add(new ChangeGroup(material.MaterialId, material.Name, true, modelIds, Ordered(rows)));
                }
            }

            // One value against every element (or material) of its object: a row when any of them differs.
            private ChangeRow Compare(LinkParameter parameter, string value, DateTime? changedAtUtc, string changedBy, IEnumerable<long> ids)
            {
                if (Placeholders.IsPlaceholder(value))
                    return null;

                var differing = new List<long>();
                var modelTexts = new List<string>();
                foreach (long id in ids)
                {
                    ValueCheck check = _compare(id, parameter, value) ?? ValueCheck.CantCompare("Revit gave no answer for it.");
                    switch (check.State)
                    {
                        case ValueState.Differs:
                            differing.Add(id);
                            modelTexts.Add(check.ModelText);
                            break;
                        case ValueState.NoParameter:
                            _missing.Add(parameter.RevitName);
                            break;
                        case ValueState.CantCompare:
                            Problem(parameter.RevitName + ": " + check.Problem);
                            break;
                    }
                }
                return differing.Count == 0
                    ? null
                    : new ChangeRow(parameter.ParameterId, parameter.RevitName, FromText(modelTexts), Display(parameter, value),
                        changedAtUtc, changedBy, differing);
            }

            private void Problem(string text)
            {
                if (_problems.Count < MaxProblems && _problemSet.Add(text))
                    _problems.Add(text);
            }

            private IEnumerable<ChangeRow> Ordered(List<ChangeRow> rows) => rows
                .OrderByDescending(row => row.ChangedAtUtc ?? DateTime.MinValue)
                .ThenBy(row => PlaceOf(row.ParameterId));

            private int PlaceOf(int parameterId)
            {
                int place;
                return _order.TryGetValue(parameterId, out place) ? place : int.MaxValue;
            }

            // "Schlitzrinne links" from the object's Bauteil and Teilelement; else the elements' type.
            private string ObjectName(List<LinkObjectValue> values, List<ModelElement> elements)
            {
                string name = PartValue(values, NamePart);
                if (name != null)
                {
                    string sub = PartValue(values, SubNamePart);
                    return sub == null ? name : name + " " + sub;
                }

                List<string> types = elements.Select(element => element.TypeName).Where(type => type != null)
                    .Distinct(StringComparer.Ordinal).ToList();
                if (types.Count == 1)
                    return types[0];
                return types.Count == 0 ? "" : types.Count.ToString(CultureInfo.InvariantCulture) + " types";
            }

            private string PartValue(List<LinkObjectValue> values, string partName)
            {
                LinkObjectValue best = null;
                foreach (LinkObjectValue value in values)
                {
                    LinkParameter parameter;
                    if (!_dictionary.TryGetValue(value.ParameterId, out parameter)
                        || !string.Equals(parameter.Name, partName, StringComparison.OrdinalIgnoreCase)
                        || Placeholders.IsPlaceholder(value.Value))
                        continue;
                    if (best == null || string.Equals(parameter.SetCode, NameSet, StringComparison.OrdinalIgnoreCase))
                        best = value;
                }
                return best == null ? null : best.Value.Trim();
            }
        }
    }
}
