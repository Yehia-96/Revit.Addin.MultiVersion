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

        /// <summary>The model doesn't have the fill pattern.</summary>
        MissingPattern,

        /// <summary>Two of the project's materials share a name; only the first is used.</summary>
        SameMaterialName,

        /// <summary>A material the model doesn't have, and that can't be created: it has no name or no group.</summary>
        NotCreatable,

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

        /// <summary>
        /// Who borrowed the elements (null when Revit doesn't say), the placeholder, the fill pattern's or the material's
        /// name, or the shared ID.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// The problem with a value that doesn't fit, the Material-ID that's left out, what a material lacks, or
        /// <see cref="OfMaterials"/>; else null.
        /// </summary>
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
            At(NotAppliedReason.Borrowed, owner, null).Add(parameter.ParameterId, parameter.RevitName, elementId);

        public void Borrowed(string owner, MaterialPattern which, long materialId) =>
            At(NotAppliedReason.Borrowed, owner, null).Add(IdOf(which), MaterialPatterns.Name(which), materialId);

        public void On(NotAppliedReason reason, LinkParameter parameter, long elementId) =>
            At(reason, null, null).Add(parameter.ParameterId, parameter.RevitName, elementId);

        public void On(NotAppliedReason reason, MaterialPattern which, long materialId) =>
            At(reason, null, null).Add(IdOf(which), MaterialPatterns.Name(which), materialId);

        /// <summary>The model doesn't have the fill pattern; named once, for all materials that should have it.</summary>
        public void MissingPattern(string patternName, MaterialPattern which, long materialId)
        {
            string known = _entries.Keys.Where(key => key.Item1 == NotAppliedReason.MissingPattern)
                .Select(key => key.Item2).FirstOrDefault(name => string.Equals(name, patternName, StringComparison.OrdinalIgnoreCase));
            At(NotAppliedReason.MissingPattern, known ?? patternName, null).Add(IdOf(which), MaterialPatterns.Name(which), materialId);
        }

        /// <summary>A material the model doesn't have and that can't be created.</summary>
        /// <param name="lacks">What it lacks, as the dictionary calls it.</param>
        public void NotCreatable(string material, string lacks) => At(NotAppliedReason.NotCreatable, material, lacks);

        public void Placeholder(string placeholder, LinkParameter parameter) =>
            At(NotAppliedReason.Placeholder, placeholder, null).AddValue(parameter.ParameterId, parameter.RevitName);

        public void MaterialReference(LinkParameter parameter) =>
            At(NotAppliedReason.MaterialReference, null, null).AddValue(parameter.ParameterId, parameter.RevitName);

        public void DoesntFit(LinkParameter parameter, string problem) => DoesntFit(parameter.ParameterId, parameter.RevitName, problem);

        public void DoesntFit(MaterialPattern which, string problem) => DoesntFit(IdOf(which), MaterialPatterns.Name(which), problem);

        private void DoesntFit(int parameterId, string parameterName, string problem)
        {
            string text = parameterName + ": " + problem;
            bool known = _entries.ContainsKey(Tuple.Create(NotAppliedReason.DoesntFit, (string)null, text));
            if (known || _entries.Keys.Count(key => key.Item1 == NotAppliedReason.DoesntFit) < MaxDoesntFit)
                At(NotAppliedReason.DoesntFit, null, text).AddValue(parameterId, parameterName);
        }

        /// <summary>The database has different values for the parameter under one ID.</summary>
        public void Conflict(string key, bool isMaterial, LinkParameter parameter) =>
            At(NotAppliedReason.ConflictingValues, key, isMaterial ? NotAppliedItem.OfMaterials : null).AddValue(parameter.ParameterId, parameter.RevitName);

        /// <summary>The material left out, because an earlier one already has its name.</summary>
        public void SameName(string materialName, string leftOutMaterialId) =>
            At(NotAppliedReason.SameMaterialName, materialName, leftOutMaterialId);

        public IReadOnlyList<NotAppliedItem> Items() => _entries
            .OrderBy(entry => entry.Key.Item1)
            .ThenBy(entry => PlaceholderPlace(entry.Key.Item2))
            .ThenBy(entry => entry.Key.Item2 ?? "", StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Key.Item3 ?? "", StringComparer.OrdinalIgnoreCase)
            .Select(entry => new NotAppliedItem(entry.Key.Item1, entry.Key.Item2, entry.Key.Item3,
                entry.Value.Parameters.OrderBy(parameter => _placeOf(parameter.Key)).ThenBy(parameter => parameter.Key).Select(parameter => parameter.Value)
                    .Distinct(StringComparer.OrdinalIgnoreCase),
                entry.Value.ValueCount, entry.Value.ElementIds))
            .ToList();

        // Beside the dictionary's, which are positive; surface before cut.
        private static int IdOf(MaterialPattern which) => which == MaterialPattern.Surface ? -2 : -1;

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

            public void Add(int parameterId, string parameterName, long elementId)
            {
                AddValue(parameterId, parameterName);
                if (_seen.Add(elementId))
                    ElementIds.Add(elementId);
            }

            public void AddValue(int parameterId, string parameterName)
            {
                Parameters[parameterId] = parameterName;
                ValueCount++;
            }
        }
    }
}
