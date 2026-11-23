using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MH.RevitTools.Database.Link;

namespace MH.RevitTools.Database.Changes
{
    /// <summary>
    /// Compares the database's values for a project with the model: what the pane lists as "changes from
    /// the database", which is what Apply writes, and what Apply leaves out. Elements meet their object by
    /// Bauteil_ID, materials theirs by name (Revit materials carry the Materialname), parameters are found by
    /// GUID. It never touches the Bauteil_ID, the key the modellers enter by hand, and placeholders ([Modeller],
    /// [ET], -, TBT and empty values) are never changes. Only reads; the Revit side answers for each value through <see cref="CompareValue"/>.
    /// </summary>
    public static class ChangeFinder
    {
        // The dictionary's names for the parts of an object's name: Bauteil "Schlitzrinne", Teilelement "links".
        private const string NamePart = "Bauteil";
        private const string SubNamePart = "Teilelement";
        private const string NameSet = "BAU";

        // What identifies an element: the key the elements are matched by, in both spellings. The numbers it's
        // made of (Bauteilnummer, Bauteilunternummer) are values like any other (Yehia, 2026-11-23), although the
        // Excel route leaves them out.
        private static readonly HashSet<string> IdentityNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
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

            var finder = new Finder(data.Parameters, compare);
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
            return new ChangeReport(groups, counts, finder.NotApplied, newest, newestBy);
        }

        /// <summary>True for the Bauteil_ID, which identifies an element and which the link never writes.</summary>
        public static bool IsIdentity(LinkParameter parameter) =>
            parameter != null && (IdentityNames.Contains(parameter.RevitName) || IdentityNames.Contains(parameter.Name));

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

        // One value of one parameter, of an object or a material.
        private sealed class Candidate
        {
            public Candidate(int parameterId, string value, DateTime? changedAtUtc, string changedBy)
            {
                ParameterId = parameterId;
                Value = value ?? "";
                ChangedAtUtc = changedAtUtc;
                ChangedBy = changedBy;
            }

            public int ParameterId { get; }

            public string Value { get; }

            public DateTime? ChangedAtUtc { get; }

            public string ChangedBy { get; }
        }

        // Collects the groups and what's left out while it compares.
        private sealed class Finder
        {
            private readonly Dictionary<int, LinkParameter> _dictionary = new Dictionary<int, LinkParameter>();
            private readonly Dictionary<int, int> _order = new Dictionary<int, int>();
            private readonly CompareValue _compare;
            private readonly NotAppliedCollector _notApplied;

            public Finder(IEnumerable<LinkParameter> dictionary, CompareValue compare)
            {
                foreach (LinkParameter parameter in dictionary)
                {
                    if (_dictionary.ContainsKey(parameter.ParameterId))
                        continue;
                    _dictionary.Add(parameter.ParameterId, parameter);
                    _order.Add(parameter.ParameterId, _order.Count);
                }
                _compare = compare;
                _notApplied = new NotAppliedCollector(PlaceOf);
            }

            public List<ChangeGroup> Groups { get; } = new List<ChangeGroup>();

            public IReadOnlyList<NotAppliedItem> NotApplied => _notApplied.Items();

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

                    List<long> ids = objectElements.Select(element => element.Id).ToList();
                    List<ChangeRow> rows = Rows(obj.Key, LinkTarget.Object, ids,
                        obj.Value.Select(value => new Candidate(value.ParameterId, value.Value, value.ChangedAtUtc, value.ChangedBy)));
                    if (rows.Count > 0)
                        Groups.Add(new ChangeGroup(obj.Key, ObjectName(obj.Value, objectElements), false, ids, rows));
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

                var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (LinkMaterial material in materials)
                {
                    List<long> modelIds;
                    string name = material.Name.Trim();
                    if (!seenIds.Add(material.MaterialId) || !modelByName.TryGetValue(name, out modelIds))
                        continue;

                    // Revit tells materials apart by name, so a name can mean one database material only; like
                    // the Excel route, the first one.
                    if (!seenNames.Add(name))
                    {
                        _notApplied.SameName(name, material.MaterialId);
                        continue;
                    }

                    List<LinkMaterialValue> materialValues;
                    if (!valuesByMaterial.TryGetValue(material.MaterialId.Trim(), out materialValues))
                        continue;
                    List<ChangeRow> rows = Rows(material.MaterialId, LinkTarget.Material, modelIds,
                        materialValues.Select(value => new Candidate(value.ParameterId, value.Value, value.ChangedAtUtc, value.ChangedBy)));
                    if (rows.Count > 0)
                        Groups.Add(new ChangeGroup(material.MaterialId, material.Name, true, modelIds, rows));
                }
            }

            // The changes on one object or material, the most recent first.
            private List<ChangeRow> Rows(string key, LinkTarget target, List<long> ids, IEnumerable<Candidate> values)
            {
                var rows = new List<ChangeRow>();
                foreach (IGrouping<int, Candidate> candidates in values.GroupBy(value => value.ParameterId))
                {
                    LinkParameter parameter;
                    if (!_dictionary.TryGetValue(candidates.Key, out parameter) || IsIdentity(parameter))
                        continue;

                    // Several values under one ID, as when two objects share a Bauteil_ID: nothing says which one
                    // is meant, so none goes in, and Apply never goes back and forth between them.
                    if (candidates.Select(value => value.Value.Trim()).Distinct(StringComparer.Ordinal).Count() > 1)
                    {
                        _notApplied.Conflict(key, target == LinkTarget.Material, parameter);
                        continue;
                    }

                    Candidate candidate = candidates.First();
                    if (LeftOut(parameter, candidate.Value, target))
                        continue;
                    ChangeRow row = Compare(parameter, candidate, ids);
                    if (row != null)
                        rows.Add(row);
                }
                return Ordered(rows).ToList();
            }

            // Placeholders, and an object's values for parameters of materials (its Material-ID): those say which
            // material it has, and Revit has them on the material, not on the element.
            private bool LeftOut(LinkParameter parameter, string value, LinkTarget target)
            {
                if (Placeholders.IsPlaceholder(value))
                {
                    string placeholder = Placeholders.Named(value);
                    if (placeholder != null)
                        _notApplied.Placeholder(placeholder, parameter);
                    return true;
                }
                if (parameter.Target == target)
                    return false;
                _notApplied.MaterialReference(parameter);
                return true;
            }

            // One value against every element (or material) of its object: a row when Apply can write it to any.
            private ChangeRow Compare(LinkParameter parameter, Candidate candidate, List<long> ids)
            {
                var differing = new List<long>();
                var modelTexts = new List<string>();
                foreach (long id in ids)
                {
                    ValueCheck check = _compare(id, parameter, candidate.Value) ?? ValueCheck.CantCompare("Revit gave no answer for it");
                    switch (check.State)
                    {
                        case ValueState.Same:
                            break;
                        case ValueState.Differs:
                            differing.Add(id);
                            modelTexts.Add(check.ModelText);
                            break;
                        case ValueState.CantCompare:
                            _notApplied.DoesntFit(parameter, check.Problem);
                            break;
                        case ValueState.Borrowed:
                            _notApplied.Borrowed(check.Owner, parameter, id);
                            break;
                        default:
                            _notApplied.On(ReasonOf(check.State), parameter, id);
                            break;
                    }
                }
                return differing.Count == 0
                    ? null
                    : new ChangeRow(parameter.ParameterId, parameter.RevitName, candidate.Value, FromText(modelTexts),
                        Display(parameter, candidate.Value), candidate.ChangedAtUtc, candidate.ChangedBy, differing);
            }

            private static NotAppliedReason ReasonOf(ValueState state)
            {
                switch (state)
                {
                    case ValueState.NoParameter:
                        return NotAppliedReason.MissingParameter;
                    case ValueState.ReadOnly:
                        return NotAppliedReason.ReadOnly;
                    case ValueState.OnType:
                        return NotAppliedReason.OnType;
                    case ValueState.OutOfDate:
                        return NotAppliedReason.OutOfDate;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(state), state, "Not a state that leaves a value out.");
                }
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
