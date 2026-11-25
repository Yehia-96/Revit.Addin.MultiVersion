using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using MH.RevitTools.Database.Changes;
using MH.RevitTools.Database.Checking;

namespace MH.RevitTools.Database.Comparing
{
    /// <summary>
    /// Compares a project as the Planungsdatenbank workbook has it with the same project as the MH database has
    /// it: objects and what they're made of, parameters, values after unit conversion, and materials (step 7, the
    /// acceptance). What differs by design is listed apart, so it doesn't hide what doesn't. Immutable.
    /// </summary>
    public sealed class LoadComparison
    {
        // The Excel reader leaves these columns out: they identify the row. The database holds them as values.
        private static readonly HashSet<string> ObjectIdentity = Names("Bauteil_ID");
        private static readonly HashSet<string> MaterialIdentity = Names("Material-ID");

        // A material's fill pattern columns in the workbook. The database holds the patterns with the material,
        // where they're compared as what the material is.
        private static readonly HashSet<string> MaterialPatterns = Names("Oberfläche-Ansicht_ID", "Oberfläche-Schnitt_ID");
        private static readonly HashSet<string> ObjectPatterns = Names();

        private LoadComparison(LoadedProject excel, LoadedProject database, IEnumerable<ComparisonSection> sections)
        {
            // What differs first, what differs by design last.
            Sections = new ReadOnlyCollection<ComparisonSection>(sections
                .Where(section => section.Count > 0)
                .OrderBy(section => section.Kind)
                .ToList());
            DifferenceCount = Count(ComparisonSectionKind.Difference);
            RemarkCount = Count(ComparisonSectionKind.Remark);
            Summary = Summarize(excel, database);
        }

        /// <summary>
        /// What differs, what the Excel reader remarks, then what differs by design; sections without anything
        /// aren't listed.
        /// </summary>
        public IReadOnlyList<ComparisonSection> Sections { get; }

        /// <summary>How many objects, parameters, values and materials differ; what differs by design doesn't count.</summary>
        public int DifferenceCount { get; }

        /// <summary>How many things the Excel reader left out or questions: they couldn't be compared.</summary>
        public int RemarkCount { get; }

        /// <summary>True when nothing differs and nothing was left out of the comparison.</summary>
        public bool IsIdentical => DifferenceCount == 0 && RemarkCount == 0;

        /// <summary>"Identical", or e.g. "12 differences between the Excel and the database".</summary>
        public string Title
        {
            get
            {
                if (IsIdentical)
                    return "Identical";
                return DifferenceCount == 0
                    ? "No differences, but " + PaneTexts.Count(RemarkCount, "remark", "remarks") + " about the workbook"
                    : PaneTexts.Count(DifferenceCount, "difference", "differences") + " between the Excel and the database";
            }
        }

        /// <summary>What was compared, e.g. "Excel: 4 objects, 61 materials. Database: 2 objects, 38 materials. In both: …".</summary>
        public string Summary { get; }

        public static LoadComparison Of(LoadedProject excel, LoadedProject database)
        {
            if (excel == null)
                throw new ArgumentNullException(nameof(excel));
            if (database == null)
                throw new ArgumentNullException(nameof(database));

            // The same parameter under another set in the two: its values are still the same values.
            Dictionary<string, string> refiled = Refiled(excel, database);
            excel = excel.WithKeys(refiled);

            var sections = new List<ComparisonSection>
            {
                OnlyIn("Objects only in the Excel", excel.Objects, database.Objects, ComparisonSectionKind.Difference, row => true),
                OnlyIn("Objects only in the database", database.Objects, excel.Objects, ComparisonSectionKind.Difference, row => true),
            };
            sections.Add(Facts("Objects made of another material", excel.Objects, database.Objects));
            sections.AddRange(Values("object", "objects", excel, database, excel.Objects, database.Objects, ObjectIdentity, ObjectPatterns,
                key => IsOfMaterials(key, database)));
            sections.Add(new ComparisonSection("Parameters only in the Excel", ComparisonSectionKind.Difference,
                Missing(excel.Parameters, database.Parameters).Select(parameter => parameter.Key + " (" + parameter.Kind + ")")));
            sections.Add(new ComparisonSection("Parameters filed under another set", ComparisonSectionKind.Difference, refiled
                .OrderBy(pair => pair.Key, NaturalOrder.Instance)
                .Select(pair => pair.Key + " in the Excel is " + pair.Value + " in the database")));
            sections.Add(Kinds(excel, database));
            sections.Add(OnlyIn("Materials only in the Excel", excel.Materials, database.Materials, ComparisonSectionKind.Difference,
                row => !ObjectMaterial.IsGlobal(row.Key)));
            sections.Add(OnlyIn("Materials only in the database", database.Materials, excel.Materials, ComparisonSectionKind.Difference, row => true));
            sections.Add(Facts("Materials that differ", excel.Materials, database.Materials));
            sections.AddRange(Values("material", "materials", excel, database, excel.Materials, database.Materials, MaterialIdentity, MaterialPatterns,
                key => false));

            sections.Add(new ComparisonSection("Given twice in the Excel, differently. Only the first is compared.",
                ComparisonSectionKind.Difference, excel.Conflicts));
            sections.Add(new ComparisonSection("Given twice in the database, differently. Only the first is compared.",
                ComparisonSectionKind.Difference, database.Conflicts));
            sections.Add(new ComparisonSection("The Excel reader's remarks about the workbook. What it left out isn't compared.",
                ComparisonSectionKind.Remark, excel.Remarks));

            sections.Add(OnlyIn("By design: global materials only in the Excel. In the database they belong to no project.",
                excel.Materials, database.Materials, ComparisonSectionKind.ByDesign, row => ObjectMaterial.IsGlobal(row.Key)));
            sections.Add(new ComparisonSection("By design: parameters only in the database. Its dictionary holds more than the workbook.",
                ComparisonSectionKind.ByDesign, Missing(database.Parameters, excel.Parameters).Select(parameter => parameter.Key)));
            return new LoadComparison(excel, database, sections);
        }

        /// <summary>The whole comparison as plain text, to copy.</summary>
        public string ToText()
        {
            var text = new StringBuilder();
            text.AppendLine(Title).AppendLine(Summary);
            foreach (ComparisonSection section in Sections)
            {
                text.AppendLine().AppendLine(section.Title + " (" + Number(section.Count) + ")");
                foreach (string line in section.Lines)
                    text.AppendLine("  " + line);
            }
            return text.ToString();
        }

        private int Count(ComparisonSectionKind kind) => Sections.Where(section => section.Kind == kind).Sum(section => section.Count);

        // The values of what's in both: those that differ one by one, those only one side has per parameter.
        private static IEnumerable<ComparisonSection> Values(
            string one, string many, LoadedProject excel, LoadedProject database,
            IReadOnlyDictionary<string, LoadedRow> excelRows, IReadOnlyDictionary<string, LoadedRow> databaseRows,
            HashSet<string> identityNames, HashSet<string> patternNames, Func<string, bool> saysMadeOf)
        {
            var differing = new List<string>();
            var onlyExcel = new OnlyOneSide(one, many, excel, database);
            var onlyDatabase = new OnlyOneSide(one, many, database, excel);
            var identity = new OnlyOneSide(one, many, database, excel);
            var patterns = new OnlyOneSide(one, many, database, excel);
            var madeOf = new OnlyOneSide(one, many, database, excel);
            foreach (string key in excelRows.Keys.Where(databaseRows.ContainsKey).OrderBy(key => key, NaturalOrder.Instance))
            {
                LoadedRow inExcel = excelRows[key], inDatabase = databaseRows[key];
                foreach (KeyValuePair<string, LoadedValue> value in inExcel.Values)
                {
                    LoadedValue other;
                    if (!inDatabase.Values.TryGetValue(value.Key, out other))
                        (HasName(value.Key, patternNames) ? patterns : saysMadeOf(value.Key) ? madeOf : onlyExcel).Add(value.Key, key, value.Value);
                    else if (!value.Value.IsSameAs(other))
                        differing.Add(key + " · " + NameOf(value.Key, database, excel) + ": Excel “" + value.Value.Shown + "”, database “" + other.Shown + "”");
                }
                foreach (KeyValuePair<string, LoadedValue> value in inDatabase.Values.Where(value => !inExcel.Values.ContainsKey(value.Key)))
                    (HasName(value.Key, identityNames) ? identity : onlyDatabase).Add(value.Key, key, value.Value);
            }

            string subject = char.ToUpperInvariant(one[0]) + one.Substring(1);
            yield return new ComparisonSection(subject + " values that differ", ComparisonSectionKind.Difference, differing);
            yield return onlyExcel.Section(subject + " values only in the Excel", ComparisonSectionKind.Difference);
            yield return onlyDatabase.Section(subject + " values only in the database", ComparisonSectionKind.Difference);
            yield return identity.Section("By design: what identifies " + (one == "object" ? "an object" : "a material")
                + ". The Excel reader leaves it out, the database holds it as a value.", ComparisonSectionKind.ByDesign);
            yield return patterns.Section("By design: fill patterns. The database holds them with the material, and they're compared there.",
                ComparisonSectionKind.ByDesign);
            yield return madeOf.Section("By design: what an object is made of, among its values. The database holds it once, and it's compared there.",
                ComparisonSectionKind.ByDesign);
        }

        private static bool IsOfMaterials(string key, LoadedProject database)
        {
            LoadedParameter parameter;
            return database.Parameters.TryGetValue(key, out parameter) && parameter.IsOfMaterials;
        }

        // The Excel's parameters that the database has once more under another set: the same group and name, and
        // only one of them on each side.
        private static Dictionary<string, string> Refiled(LoadedProject excel, LoadedProject database)
        {
            var refiled = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            ILookup<string, LoadedParameter> databaseOnly = Missing(database.Parameters, excel.Parameters)
                .ToLookup(parameter => GroupAndName(parameter.Key), StringComparer.OrdinalIgnoreCase);
            foreach (IGrouping<string, LoadedParameter> inExcel in Missing(excel.Parameters, database.Parameters)
                         .GroupBy(parameter => GroupAndName(parameter.Key), StringComparer.OrdinalIgnoreCase))
            {
                List<LoadedParameter> inDatabase = databaseOnly[inExcel.Key].ToList();
                if (inExcel.Count() == 1 && inDatabase.Count == 1)
                    refiled.Add(inExcel.Single().Key, inDatabase[0].Key);
            }
            return refiled;
        }

        // "SPE-Betonstahl Mat.-ID" of BAU-SPE-Betonstahl Mat.-ID.
        private static string GroupAndName(string key)
        {
            int dash = key.IndexOf('-');
            return dash < 0 ? key : key.Substring(dash + 1);
        }

        private static ComparisonSection Facts(
            string title, IReadOnlyDictionary<string, LoadedRow> excelRows, IReadOnlyDictionary<string, LoadedRow> databaseRows)
        {
            var lines = new List<string>();
            foreach (string key in excelRows.Keys.Where(databaseRows.ContainsKey).OrderBy(key => key, NaturalOrder.Instance))
            {
                IReadOnlyDictionary<string, string> inExcel = excelRows[key].Facts, inDatabase = databaseRows[key].Facts;
                foreach (string name in inExcel.Keys.Union(inDatabase.Keys, StringComparer.OrdinalIgnoreCase))
                {
                    string mine = Fact(inExcel, name), other = Fact(inDatabase, name);
                    if (!string.Equals(mine, other, StringComparison.Ordinal))
                        lines.Add(key + " · " + name + ": Excel “" + mine + "”, database “" + other + "”");
                }
            }
            return new ComparisonSection(title, ComparisonSectionKind.Difference, lines);
        }

        private static string Fact(IReadOnlyDictionary<string, string> facts, string name)
        {
            string value;
            return facts.TryGetValue(name, out value) ? value : "";
        }

        private static ComparisonSection Kinds(LoadedProject excel, LoadedProject database) =>
            new ComparisonSection("Parameters that Revit takes differently", ComparisonSectionKind.Difference, excel.Parameters.Values
                .Where(parameter => database.Parameters.ContainsKey(parameter.Key))
                .Select(parameter => Tuple.Create(parameter, database.Parameters[parameter.Key]))
                .Where(pair => !string.Equals(pair.Item1.Kind, pair.Item2.Kind, StringComparison.Ordinal))
                .OrderBy(pair => pair.Item1.Key, NaturalOrder.Instance)
                .Select(pair => pair.Item1.Key + ": Excel " + pair.Item1.Kind + ", database " + pair.Item2.Kind));

        private static ComparisonSection OnlyIn(
            string title, IReadOnlyDictionary<string, LoadedRow> rows, IReadOnlyDictionary<string, LoadedRow> others,
            ComparisonSectionKind kind, Func<LoadedRow, bool> which) =>
            new ComparisonSection(title, kind, rows.Values
                .Where(row => !others.ContainsKey(row.Key) && which(row))
                .OrderBy(row => row.Key, NaturalOrder.Instance)
                .Select(row => row.Key + (row.Values.Count == 0 ? " (no values)" : " (" + PaneTexts.Count(row.Values.Count, "value", "values") + ")")));

        private static IEnumerable<LoadedParameter> Missing(
            IReadOnlyDictionary<string, LoadedParameter> parameters, IReadOnlyDictionary<string, LoadedParameter> others) =>
            parameters.Values.Where(parameter => !others.ContainsKey(parameter.Key)).OrderBy(parameter => parameter.Key, NaturalOrder.Instance);

        private static string Summarize(LoadedProject excel, LoadedProject database) =>
            "Excel: " + Counted(excel.Objects.Count, excel.Materials.Count) + ". Database: " + Counted(database.Objects.Count, database.Materials.Count)
            + ". In both: " + Counted(excel.Objects.Keys.Count(database.Objects.ContainsKey), excel.Materials.Keys.Count(database.Materials.ContainsKey)) + ".";

        private static string Counted(int objects, int materials) =>
            PaneTexts.Count(objects, "object", "objects") + ", " + PaneTexts.Count(materials, "material", "materials");

        // The dictionary name at the key's end: "Dicke" in BAU-GEO-Dicke, "Material-ID" in IDE-KAT-Material-ID.
        private static bool HasName(string parameterKey, HashSet<string> names)
        {
            string[] parts = parameterKey.Split(new[] { '-' }, 3);
            return parts.Length == 3 && names.Contains(parts[2]);
        }

        private static string NameOf(string key, LoadedProject first, LoadedProject second)
        {
            LoadedParameter parameter;
            return first.Parameters.TryGetValue(key, out parameter) || second.Parameters.TryGetValue(key, out parameter) ? parameter.Name : key;
        }

        private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

        private static HashSet<string> Names(params string[] names) => new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);

        // Values only one side has, a line per parameter: "Materialname: 38 materials, e.g. MB-…-0011 “11-Beton”".
        private sealed class OnlyOneSide
        {
            private readonly string _one;
            private readonly string _many;
            private readonly LoadedProject _first;
            private readonly LoadedProject _second;
            private readonly Dictionary<string, List<string>> _byParameter = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            public OnlyOneSide(string one, string many, LoadedProject first, LoadedProject second)
            {
                _one = one;
                _many = many;
                _first = first;
                _second = second;
            }

            public void Add(string parameterKey, string rowKey, LoadedValue value)
            {
                List<string> rows;
                if (!_byParameter.TryGetValue(parameterKey, out rows))
                {
                    rows = new List<string>();
                    _byParameter.Add(parameterKey, rows);
                }
                rows.Add(rowKey + " “" + value.Shown + "”");
            }

            public ComparisonSection Section(string title, ComparisonSectionKind kind) => new ComparisonSection(title, kind,
                _byParameter.OrderBy(parameter => parameter.Key, NaturalOrder.Instance).Select(parameter =>
                    Named(parameter.Key) + ": " + PaneTexts.Count(parameter.Value.Count, _one, _many) + ", "
                    + (parameter.Value.Count == 1 ? "" : "e.g. ") + parameter.Value[0]),
                _byParameter.Sum(parameter => parameter.Value.Count));

            // The key, and the name where the key doesn't already say it, e.g. "PRI-BES-Typ (Typ (Produktinformation))".
            private string Named(string key)
            {
                string name = NameOf(key, _first, _second);
                return name == key || key.EndsWith("-" + name, StringComparison.Ordinal) ? key : key + " (" + name + ")";
            }
        }
    }

    /// <summary>What a section of the comparison means, in the order they're listed.</summary>
    public enum ComparisonSectionKind
    {
        /// <summary>The two sources don't give the same.</summary>
        Difference,

        /// <summary>The Excel reader left something out or questions it, so it isn't compared.</summary>
        Remark,

        /// <summary>The two differ, and are known to.</summary>
        ByDesign,
    }

    /// <summary>One part of the comparison, with its lines. Immutable.</summary>
    public sealed class ComparisonSection
    {
        internal ComparisonSection(string title, ComparisonSectionKind kind, IEnumerable<string> lines, int? count = null)
        {
            Title = title;
            Kind = kind;
            Lines = new ReadOnlyCollection<string>(lines.ToList());
            Count = count ?? Lines.Count;
        }

        public string Title { get; }

        public ComparisonSectionKind Kind { get; }

        public IReadOnlyList<string> Lines { get; }

        /// <summary>How many objects, parameters, values or materials; a line can stand for several values.</summary>
        public int Count { get; }
    }
}
