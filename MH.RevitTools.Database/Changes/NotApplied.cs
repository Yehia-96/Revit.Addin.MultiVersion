using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using MH.RevitTools.Database.Link;

namespace MH.RevitTools.Database.Changes
{
    /// <summary>Why Apply leaves values out, in the order "Not applied" lists them: what someone can act on first.</summary>
    public enum NotAppliedReason
    {
        /// <summary>Someone else has borrowed the elements.</summary>
        Borrowed,

        /// <summary>The elements have newer changes in the central model.</summary>
        OutOfDate,

        /// <summary>Revit doesn't let anyone change the parameter there.</summary>
        ReadOnly,

        /// <summary>The parameter sits on the type, which other elements share.</summary>
        OnType,

        /// <summary>The value doesn't fit the parameter, e.g. text in a number.</summary>
        DoesntFit,

        /// <summary>Two objects (or materials) share an ID in the database and have different values.</summary>
        ConflictingValues,

        /// <summary>The model doesn't have the parameter: Check parameters adds it.</summary>
        MissingParameter,

        /// <summary>Two of the project's materials share a name; only the first is used.</summary>
        SameMaterialName,

        /// <summary>[Modeller], [ET], - or TBT.</summary>
        Placeholder,

        /// <summary>An object's reference to its material: a parameter of materials, not of elements.</summary>
        MaterialReference,
    }

    /// <summary>One entry under "Not applied". Immutable; its words are in <c>ApplyTexts</c>.</summary>
    public sealed class NotAppliedItem
    {
        /// <summary><see cref="Text"/> of conflicting values that materials share, rather than objects.</summary>
        internal const string OfMaterials = "materials";

        internal NotAppliedItem(
            NotAppliedReason reason, string name, string text, IEnumerable<string> parameters, int valueCount, IEnumerable<long> elementIds)
        {
            Reason = reason;
            Name = name;
            Text = text;
            Parameters = new ReadOnlyCollection<string>((parameters ?? Enumerable.Empty<string>()).ToList());
            ValueCount = valueCount;
            ElementIds = new ReadOnlyCollection<long>((elementIds ?? Enumerable.Empty<long>()).ToList());
        }

        public NotAppliedReason Reason { get; }

        /// <summary>Who borrowed the elements (null when Revit doesn't say), the placeholder, the material's name, or the shared ID.</summary>
        public string Name { get; }

        /// <summary>The problem with a value that doesn't fit, the Material-ID that's left out, or <see cref="OfMaterials"/>; else null.</summary>
        public string Text { get; }

        /// <summary>The Revit names of the parameters concerned, in the dictionary's order.</summary>
        public IReadOnlyList<string> Parameters { get; }

        /// <summary>How many values it's about: for placeholders and references, one per parameter of an object or material.</summary>
        public int ValueCount { get; }

        /// <summary>The elements or materials concerned, where it's about particular ones.</summary>
        public IReadOnlyList<long> ElementIds { get; }
    }

    /// <summary>Collects what <see cref="ChangeFinder"/> leaves out, grouped by its reason.</summary>
    internal sealed class NotAppliedCollector
    {
        // A value that doesn't fit is named each time; this many are enough to see what's wrong.
        private const int MaxDoesntFit = 20;

        private readonly Func<int, int> _placeOf;
        private readonly Dictionary<Tuple<NotAppliedReason, string, string>, Entry> _entries =
            new Dictionary<Tuple<NotAppliedReason, string, string>, Entry>();

        /// <param name="placeOf">The parameter's place in the dictionary, by its id.</param>
        public NotAppliedCollector(Func<int, int> placeOf)
        {
            _placeOf = placeOf;
        }

        public void Borrowed(string owner, LinkParameter parameter, long elementId) =>
            At(NotAppliedReason.Borrowed, owner, null).Add(parameter, elementId);

        public void On(NotAppliedReason reason, LinkParameter parameter, long elementId) =>
            At(reason, null, null).Add(parameter, elementId);

        public void Placeholder(string placeholder, LinkParameter parameter) =>
            At(NotAppliedReason.Placeholder, placeholder, null).AddValue(parameter);

        public void MaterialReference(LinkParameter parameter) =>
            At(NotAppliedReason.MaterialReference, null, null).AddValue(parameter);

        public void DoesntFit(LinkParameter parameter, string problem)
        {
            string text = parameter.RevitName + ": " + problem;
            bool known = _entries.ContainsKey(Tuple.Create(NotAppliedReason.DoesntFit, (string)null, text));
            if (known || _entries.Keys.Count(key => key.Item1 == NotAppliedReason.DoesntFit) < MaxDoesntFit)
                At(NotAppliedReason.DoesntFit, null, text).AddValue(parameter);
        }

        /// <summary>The database has different values for the parameter under one ID.</summary>
        public void Conflict(string key, bool isMaterial, LinkParameter parameter) =>
            At(NotAppliedReason.ConflictingValues, key, isMaterial ? NotAppliedItem.OfMaterials : null).AddValue(parameter);

        /// <summary>The material left out, because an earlier one already has its name.</summary>
        public void SameName(string materialName, string leftOutMaterialId) =>
            At(NotAppliedReason.SameMaterialName, materialName, leftOutMaterialId);

        public IReadOnlyList<NotAppliedItem> Items() => _entries
            .OrderBy(entry => entry.Key.Item1)
            .ThenBy(entry => PlaceholderPlace(entry.Key.Item2))
            .ThenBy(entry => entry.Key.Item2 ?? "", StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Key.Item3 ?? "", StringComparer.OrdinalIgnoreCase)
            .Select(entry => new NotAppliedItem(entry.Key.Item1, entry.Key.Item2, entry.Key.Item3,
                entry.Value.Parameters.OrderBy(parameter => _placeOf(parameter.Key)).Select(parameter => parameter.Value)
                    .Distinct(StringComparer.OrdinalIgnoreCase),
                entry.Value.ValueCount, entry.Value.ElementIds))
            .ToList();

        private static int PlaceholderPlace(string name)
        {
            for (int i = 0; i < Placeholders.All.Count; i++)
            {
                if (string.Equals(Placeholders.All[i], name, StringComparison.Ordinal))
                    return i;
            }
            return Placeholders.All.Count;
        }

        private Entry At(NotAppliedReason reason, string name, string text)
        {
            Tuple<NotAppliedReason, string, string> key = Tuple.Create(reason, name, text);
            Entry entry;
            if (!_entries.TryGetValue(key, out entry))
            {
                entry = new Entry();
                _entries.Add(key, entry);
            }
            return entry;
        }

        private sealed class Entry
        {
            private readonly HashSet<long> _seen = new HashSet<long>();

            public Dictionary<int, string> Parameters { get; } = new Dictionary<int, string>();

            public List<long> ElementIds { get; } = new List<long>();

            public int ValueCount { get; private set; }

            public void Add(LinkParameter parameter, long elementId)
            {
                AddValue(parameter);
                if (_seen.Add(elementId))
                    ElementIds.Add(elementId);
            }

            public void AddValue(LinkParameter parameter)
            {
                Parameters[parameter.ParameterId] = parameter.RevitName;
                ValueCount++;
            }
        }
    }
}
