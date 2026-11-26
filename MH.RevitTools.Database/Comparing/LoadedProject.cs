using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using MH.RevitTools.Database.Changes;

namespace MH.RevitTools.Database.Comparing
{
    /// <summary>
    /// A project as one source gave it, the Planungsdatenbank workbook or the MH database, in one shape, so the
    /// two can be compared (step 7, the acceptance). Parameters go by their dictionary key, e.g. BAU-GEO-Dicke, as
    /// the two sources number and sometimes name them differently. Immutable; made by
    /// <see cref="LoadedProjectBuilder"/>.
    /// </summary>
    public sealed class LoadedProject
    {
        internal LoadedProject(string source, IDictionary<string, LoadedParameter> parameters,
            IDictionary<string, LoadedRow> objects, IDictionary<string, LoadedRow> materials,
            IEnumerable<string> conflicts, IEnumerable<string> remarks)
        {
            Source = source;
            Conflicts = new ReadOnlyCollection<string>(conflicts.ToList());
            Remarks = new ReadOnlyCollection<string>(remarks.ToList());
            Parameters = new ReadOnlyDictionary<string, LoadedParameter>(parameters);
            Objects = new ReadOnlyDictionary<string, LoadedRow>(objects);
            Materials = new ReadOnlyDictionary<string, LoadedRow>(materials);
        }

        /// <summary>For the texts: "Excel" or "database".</summary>
        public string Source { get; }

        /// <summary>
        /// What the source gives more than once, differently, as when two objects share a Bauteil_ID; only the
        /// first is compared, so these are said.
        /// </summary>
        public IReadOnlyList<string> Conflicts { get; }

        /// <summary>What the source's reader left out or questions, such as a row without a Bauteil_ID.</summary>
        public IReadOnlyList<string> Remarks { get; }

        /// <summary>By dictionary key, whatever its case.</summary>
        public IReadOnlyDictionary<string, LoadedParameter> Parameters { get; }

        /// <summary>By Bauteil_ID, whatever its case.</summary>
        public IReadOnlyDictionary<string, LoadedRow> Objects { get; }

        /// <summary>By Material-ID, whatever its case.</summary>
        public IReadOnlyDictionary<string, LoadedRow> Materials { get; }

        /// <summary>The same project with some parameters under another key.</summary>
        internal LoadedProject WithKeys(IReadOnlyDictionary<string, string> newKeys)
        {
            var parameters = new Dictionary<string, LoadedParameter>(StringComparer.OrdinalIgnoreCase);
            foreach (LoadedParameter parameter in Parameters.Values)
            {
                string key = Renamed(parameter.Key, newKeys);
                parameters[key] = new LoadedParameter(key, parameter.Name, parameter.Kind, parameter.IsOfMaterials);
            }
            return new LoadedProject(Source, parameters, Rows(Objects, newKeys), Rows(Materials, newKeys), Conflicts, Remarks);
        }

        private static Dictionary<string, LoadedRow> Rows(IReadOnlyDictionary<string, LoadedRow> rows, IReadOnlyDictionary<string, string> newKeys) =>
            rows.ToDictionary(
                row => row.Key,
                row => new LoadedRow(row.Key,
                    row.Value.Facts.ToDictionary(fact => fact.Key, fact => fact.Value, StringComparer.OrdinalIgnoreCase),
                    row.Value.Values.ToDictionary(value => Renamed(value.Key, newKeys), value => value.Value, StringComparer.OrdinalIgnoreCase)),
                StringComparer.OrdinalIgnoreCase);

        private static string Renamed(string key, IReadOnlyDictionary<string, string> newKeys)
        {
            string newKey;
            return newKeys.TryGetValue(key, out newKey) ? newKey : key;
        }
    }

    /// <summary>One parameter as a source defines it. Immutable.</summary>
    public sealed class LoadedParameter
    {
        public LoadedParameter(string key, string name, string kind, bool isOfMaterials = false)
        {
            Key = key ?? "";
            Name = string.IsNullOrWhiteSpace(name) ? Key : name.Trim();
            Kind = kind ?? "";
            IsOfMaterials = isOfMaterials;
        }

        /// <summary>E.g. BAU-GEO-Dicke.</summary>
        public string Key { get; }

        /// <summary>As the source names it, e.g. "Beschreibung (Bauteilinformation)".</summary>
        public string Name { get; }

        /// <summary>What Revit makes of it, e.g. Length, Text, Integer.</summary>
        public string Kind { get; }

        /// <summary>In Revit it's a parameter of materials; of an object, it only says what the object is made of.</summary>
        public bool IsOfMaterials { get; }
    }

    /// <summary>One value, as the source shows it and as Revit gets it. Immutable.</summary>
    public sealed class LoadedValue
    {
        // Units are converted, so the last digits can be rounding. Relative, and nothing coarser: a soil's
        // permeability can be 1e-9.
        private const double Tolerance = 1e-9;

        /// <param name="shown">As the source has it, with its unit, e.g. "0.3 m".</param>
        /// <param name="text">The plain text.</param>
        /// <param name="number">What Revit gets, in its own units; null when it's not a number.</param>
        public LoadedValue(string shown, string text, double? number)
        {
            Shown = shown ?? "";
            Text = (text ?? "").Trim();
            Number = number;
        }

        public string Shown { get; }

        public string Text { get; }

        public double? Number { get; }

        /// <summary>True when Revit gets the same from both, after unit conversion.</summary>
        public bool IsSameAs(LoadedValue other)
        {
            if (other == null)
                return false;
            if (!Number.HasValue || !other.Number.HasValue)
                return SameText.Is(Text, other.Text);

            double mine = Number.Value, theirs = other.Number.Value;
            if (IsWhole(mine) && IsWhole(theirs))
                return mine == theirs;
            return Math.Abs(mine - theirs) <= Tolerance * Math.Max(Math.Abs(mine), Math.Abs(theirs));
        }

        private static bool IsWhole(double number) => number == Math.Floor(number);
    }

    /// <summary>One object or material: what it's called and its values. Immutable.</summary>
    public sealed class LoadedRow
    {
        internal LoadedRow(string key, IDictionary<string, string> facts, IDictionary<string, LoadedValue> values)
        {
            Key = key;
            Facts = new ReadOnlyDictionary<string, string>(facts);
            Values = new ReadOnlyDictionary<string, LoadedValue>(values);
        }

        /// <summary>The Bauteil_ID or the Material-ID.</summary>
        public string Key { get; }

        /// <summary>
        /// What it is besides its values: a material's name, group and fill patterns, or the material an object is made of.
        /// </summary>
        public IReadOnlyDictionary<string, string> Facts { get; }

        /// <summary>By the parameter's dictionary key. Placeholders and empty values aren't in it.</summary>
        public IReadOnlyDictionary<string, LoadedValue> Values { get; }
    }

    /// <summary>The value in Revit's form: the parameter's data type and unit, and the value as stored.</summary>
    public delegate LoadedValue ConvertLoaded(string dataType, string unit, string raw);

    /// <summary>Puts a source's project together.</summary>
    public sealed class LoadedProjectBuilder
    {
        private readonly string _source;
        private readonly Dictionary<string, LoadedParameter> _parameters = New<LoadedParameter>();
        private readonly Dictionary<string, Row> _objects = New<Row>();
        private readonly Dictionary<string, Row> _materials = New<Row>();
        private readonly List<string> _conflicts = new List<string>();
        private readonly List<string> _remarks = new List<string>();

        public LoadedProjectBuilder(string source)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
        }

        /// <summary>The lookup key of a dictionary parameter, e.g. BAU-GEO-Dicke.</summary>
        public static string KeyOf(string setCode, string groupCode, string name) =>
            (setCode ?? "").Trim() + "-" + (groupCode ?? "").Trim() + "-" + (name ?? "").Trim();

        public void Parameter(LoadedParameter parameter)
        {
            if (parameter == null)
                throw new ArgumentNullException(nameof(parameter));
            LoadedParameter first;
            if (!_parameters.TryGetValue(parameter.Key, out first))
                _parameters.Add(parameter.Key, parameter);
            else if (!string.Equals(first.Kind, parameter.Kind, StringComparison.Ordinal))
                _conflicts.Add(first.Key + ": " + first.Kind + " and " + parameter.Kind);
        }

        /// <summary>An object, also one without values.</summary>
        public void Object(string bauteilId) => At(_objects, bauteilId);

        public void ObjectValue(string bauteilId, string parameterKey, LoadedValue value) => Put(_objects, bauteilId, parameterKey, value);

        /// <summary>The material an object is made of, by the Materialname, which is what Revit gets.</summary>
        public void ObjectMaterial(string bauteilId, string materialName) => Fact(_objects, bauteilId, "Material", materialName);

        /// <summary>A material; its name, group and fill patterns may be empty.</summary>
        public void Material(string materialId, string name, string group, string surfacePattern, string cutPattern)
        {
            Fact(_materials, materialId, "Materialname", name);
            Fact(_materials, materialId, "Materialgruppe", group);
            Fact(_materials, materialId, "surface pattern", surfacePattern);
            Fact(_materials, materialId, "cut pattern", cutPattern);
        }

        public void MaterialValue(string materialId, string parameterKey, LoadedValue value) => Put(_materials, materialId, parameterKey, value);

        /// <summary>Something the source's reader left out or questions.</summary>
        public void Remark(string text)
        {
            if (!string.IsNullOrWhiteSpace(text))
                _remarks.Add(text.Trim());
        }

        public LoadedProject Build() => new LoadedProject(_source, _parameters, Rows(_objects), Rows(_materials), _conflicts, _remarks);

        // The first one counts where something repeats, as in the Excel reader; another one is said.
        private void Fact(Dictionary<string, Row> rows, string key, string name, string value)
        {
            Row row = At(rows, key);
            string trimmed = (value ?? "").Trim(), first;
            if (!row.Facts.TryGetValue(name, out first))
                row.Facts.Add(name, trimmed);
            else if (!string.Equals(first, trimmed, StringComparison.Ordinal))
                _conflicts.Add((key ?? "").Trim() + " · " + name + ": “" + first + "” and “" + trimmed + "”");
        }

        private void Put(Dictionary<string, Row> rows, string key, string parameterKey, LoadedValue value)
        {
            if (value == null)
                return;
            Row row = At(rows, key);
            LoadedValue first;
            if (!row.Values.TryGetValue(parameterKey, out first))
                row.Values.Add(parameterKey, value);
            else if (!first.IsSameAs(value))
                _conflicts.Add((key ?? "").Trim() + " · " + parameterKey + ": “" + first.Shown + "” and “" + value.Shown + "”");
        }

        private static Row At(Dictionary<string, Row> rows, string key)
        {
            string trimmed = (key ?? "").Trim();
            Row row;
            if (!rows.TryGetValue(trimmed, out row))
            {
                row = new Row();
                rows.Add(trimmed, row);
            }
            return row;
        }

        private static Dictionary<string, LoadedRow> Rows(Dictionary<string, Row> rows) =>
            rows.ToDictionary(row => row.Key, row => new LoadedRow(row.Key, row.Value.Facts, row.Value.Values), StringComparer.OrdinalIgnoreCase);

        private static Dictionary<string, T> New<T>() => new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);

        private sealed class Row
        {
            public Dictionary<string, string> Facts { get; } = New<string>();

            public Dictionary<string, LoadedValue> Values { get; } = New<LoadedValue>();
        }
    }
}
