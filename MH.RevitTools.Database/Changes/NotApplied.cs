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

        /// <summary>The model doesn't have the material the objects are made of.</summary>
        MissingMaterial,

        /// <summary>The model doesn't have the fill pattern.</summary>
        MissingPattern,

        /// <summary>The elements have no parameter that takes their material.</summary>
        NoMaterialParameter,

        /// <summary>Two of the project's materials share a name; only the first is used.</summary>
        SameMaterialName,

        /// <summary>A material the model doesn't have, and that can't be created: it has no name or no group.</summary>
        NotCreatable,

        /// <summary>Values of materials for parameters that go on elements.</summary>
        OfElements,

        /// <summary>[Modeller], [ET], - or TBT.</summary>
        Placeholder,
    }

    /// <summary>One entry under "Not applied". Immutable; its words are in <c>ApplyTexts</c>.</summary>
    public sealed class NotAppliedItem
    {
        /// <summary><see cref="Text"/> of conflicting values that materials share, rather than objects.</summary>
        internal const string OfMaterials = "materials";

        /// <summary><see cref="Text"/> of a missing material that Create doesn't make, as it's a global one.</summary>
        internal const string Global = "global";

        /// <summary><see cref="Text"/> of a missing material that Create can't make for another reason.</summary>
        internal const string NotCreated = "not created";

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
        /// The problem with a value that doesn't fit, the Material-ID that's left out, what a material lacks,
        /// <see cref="OfMaterials"/>, <see cref="Global"/> or <see cref="NotCreated"/>; else null.
        /// </summary>
        public string Text { get; }

        /// <summary>The Revit names of the parameters concerned, in the dictionary's order.</summary>
        public IReadOnlyList<string> Parameters { get; }

        /// <summary>How many values it's about: for placeholders, one per parameter of an object or material.</summary>
        public int ValueCount { get; }

        /// <summary>The elements or materials concerned, where it's about particular ones.</summary>
        public IReadOnlyList<long> ElementIds { get; }
    }

    /// <summary>Collects what <see cref="ChangeFinder"/> leaves out, grouped by its reason.</summary>
    internal sealed class NotAppliedCollector
    {
        // A value that doesn't fit is named each time; this many are enough to see what's wrong.
        private const int MaxDoesntFit = 20;

        // Beside the dictionary's IDs, which are positive, for what's no parameter of it; surface before cut.
        private const int MaterialPlace = -3;
        private const int SurfacePlace = -2;
        private const int CutPlace = -1;

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
        public void MissingPattern(string patternName, MaterialPattern which, long materialId) =>
            At(NotAppliedReason.MissingPattern, KnownAs(NotAppliedReason.MissingPattern, patternName), null)
                .Add(IdOf(which), MaterialPatterns.Name(which), materialId);

        // What an object is made of, on one of its elements.
        public void MaterialBorrowed(string owner, long elementId) => AddMaterial(At(NotAppliedReason.Borrowed, owner, null), elementId);

        public void MaterialOn(NotAppliedReason reason, long elementId) => AddMaterial(At(reason, null, null), elementId);

        public void MaterialDoesntFit(string problem) => DoesntFit(MaterialPlace, ObjectMaterial.ParameterName, problem);

        public void MaterialConflict(string bauteilId) =>
            At(NotAppliedReason.ConflictingValues, bauteilId, null).AddValue(MaterialPlace, ObjectMaterial.ParameterName);

        /// <summary>The model doesn't have the material; named once, for all elements that should have it.</summary>
        /// <param name="why">Why Create doesn't make it; null when it does.</param>
        public void MissingMaterial(string materialName, string why, long elementId) =>
            AddMaterial(At(NotAppliedReason.MissingMaterial, KnownAs(NotAppliedReason.MissingMaterial, materialName), why), elementId);

        /// <summary>A material the model doesn't have and that can't be created.</summary>
        /// <param name="lacks">What it lacks, as the dictionary calls it.</param>
        public void NotCreatable(string material, string lacks) => At(NotAppliedReason.NotCreatable, material, lacks);

        public void Placeholder(string placeholder, LinkParameter parameter) =>
            At(NotAppliedReason.Placeholder, placeholder, null).AddValue(parameter.ParameterId, parameter.RevitName);

        public void OfElements(LinkParameter parameter) =>
            At(NotAppliedReason.OfElements, null, null).AddValue(parameter.ParameterId, parameter.RevitName);

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

        private static int IdOf(MaterialPattern which) => which == MaterialPattern.Surface ? SurfacePlace : CutPlace;

        private static void AddMaterial(Entry entry, long elementId) => entry.Add(MaterialPlace, ObjectMaterial.ParameterName, elementId);

        // The name as it was given first, whatever its case.
        private string KnownAs(NotAppliedReason reason, string name) => _entries.Keys.Where(key => key.Item1 == reason)
            .Select(key => key.Item2).FirstOrDefault(known => string.Equals(known, name, StringComparison.OrdinalIgnoreCase)) ?? name;

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
