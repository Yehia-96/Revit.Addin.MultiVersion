using System;
using System.Globalization;
using System.Linq;
using MH.RevitTools.Database.Comparing;
using MH.RevitTools.Database.Link;
using MH.RevitTools.Database.Tests.Checking;

namespace MH.RevitTools.Database.Tests.Comparing;

public sealed class LoadComparisonTests
{
    private const string Dicke = "BAU-GEO-Dicke";
    private const string Guete = "BAU-SPE-Betonstahl Güte";
    private const string Festigkeit = "MAT-BET-Festigkeitsklasse";

    /// <summary>As Revit would convert it, in metres: mm and cm become m, everything else stays.</summary>
    private static LoadedValue Convert(string dataType, string unit, string raw)
    {
        string plainUnit = (unit ?? "").Trim('[', ']');
        bool isNumber = dataType.Trim('[', ']').ToLowerInvariant() is "double" or "integer";
        double? number = isNumber && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? value * (plainUnit == "mm" ? 0.001 : plainUnit == "cm" ? 0.01 : 1)
            : null;
        return new LoadedValue(plainUnit is "" or "-" ? raw : raw + " " + plainUnit, raw, number);
    }

    private static LoadedValue Text(string text) => new(text, text, null);

    private static LoadedProjectBuilder Excel()
    {
        var excel = new LoadedProjectBuilder("Excel");
        excel.Parameter(new LoadedParameter(Dicke, "Dicke", "Length"));
        excel.Parameter(new LoadedParameter(Guete, "Betonstahl Güte", "Text"));
        excel.Parameter(new LoadedParameter(Festigkeit, "Festigkeitsklasse", "Text"));
        return excel;
    }

    private static LoadedProjectBuilder Database()
    {
        var database = new LoadedProjectBuilder("database");
        database.Parameter(new LoadedParameter(Dicke, "Dicke", "Length"));
        database.Parameter(new LoadedParameter(Guete, "Betonstahl Güte", "Text"));
        database.Parameter(new LoadedParameter(Festigkeit, "Festigkeitsklasse", "Text"));
        return database;
    }

    private static string[] Lines(LoadComparison comparison, string title) =>
        comparison.Sections.SingleOrDefault(section => section.Title.StartsWith(title, StringComparison.Ordinal))?.Lines.ToArray() ?? Array.Empty<string>();

    [Fact]
    public void TheSameProject_IsIdentical_WhateverTheUnitsCaseAndTrailingZeros()
    {
        LoadedProjectBuilder excel = Excel(), database = Database();
        excel.ObjectValue("BT-1101-01", Dicke, Convert("[double]", "[m]", "0.30"));
        database.ObjectValue("bt-1101-01", "bau-geo-dicke", Convert("Double", "mm", "300"));
        excel.ObjectValue("BT-1101-01", Guete, Text("B500B "));
        database.ObjectValue("BT-1101-01", Guete, Text("B500B"));
        excel.Material("MB-1761-01-BET-0011", "11-Beton", "Beton", null, " ");
        database.Material("MB-1761-01-BET-0011", "11-Beton", "Beton", "", null);
        excel.MaterialValue("MB-1761-01-BET-0011", Festigkeit, Text("C12/15"));
        database.MaterialValue("MB-1761-01-BET-0011", Festigkeit, Text("C12/15"));

        LoadComparison comparison = LoadComparison.Of(excel.Build(), database.Build());

        Assert.True(comparison.IsIdentical);
        Assert.Equal("Identical", comparison.Title);
        Assert.Empty(comparison.Sections);
        Assert.Equal("Excel: 1 object, 1 material. Database: 1 object, 1 material. In both: 1 object, 1 material.", comparison.Summary);
    }

    [Fact]
    public void ValuesThatDiffer_AreListedOneByOne_WithBothSides()
    {
        LoadedProjectBuilder excel = Excel(), database = Database();
        excel.ObjectValue("BT-1101-01", Dicke, Convert("[double]", "[m]", "0.3"));
        database.ObjectValue("BT-1101-01", Dicke, Convert("Double", "m", "0.31"));
        excel.ObjectValue("BT-201-01", Guete, Text("B500B"));
        database.ObjectValue("BT-201-01", Guete, Text("b500b"));

        LoadComparison comparison = LoadComparison.Of(excel.Build(), database.Build());

        Assert.Equal(2, comparison.DifferenceCount);
        Assert.Equal("2 differences between the Excel and the database", comparison.Title);
        Assert.Equal(
            new[]
            {
                "BT-201-01 · Betonstahl Güte: Excel “B500B”, database “b500b”",
                "BT-1101-01 · Dicke: Excel “0.3 m”, database “0.31 m”",
            },
            Lines(comparison, "Object values that differ"));
    }

    [Theory]
    [InlineData(0.1 + 0.2, 0.3, true)] // rounding
    [InlineData(300 * 0.001, 0.3, true)]
    [InlineData(1e-9, 2e-9, false)] // a soil's permeability
    [InlineData(99990101, 99990102, false)] // a Materialnummer
    [InlineData(2147483646, 2147483647, false)]
    [InlineData(0, 0, true)]
    [InlineData(0, 0.0001, false)]
    [InlineData(12.5, 12.500001, false)]
    [InlineData(-0.3, 0.3, false)]
    public void Numbers_AreTheSame_UpToRoundingOnly(double first, double second, bool same)
    {
        LoadedValue one = new("first", "first", first), other = new("second", "second", second);

        Assert.Equal(same, one.IsSameAs(other));
        Assert.Equal(same, other.IsSameAs(one));
    }

    [Fact]
    public void ANumberAndSomethingThatIsntOne_AreComparedAsText()
    {
        Assert.False(Convert("double", "m", "0.3").IsSameAs(Text("0.30")));
        Assert.True(Convert("double", "m", "0.3").IsSameAs(Text("0.3")));
        Assert.False(Text("0.3").IsSameAs(null));
    }

    // A note typed in Studio has CR LF, a cell of the workbook LF.
    [Fact]
    public void ALineBreak_IsTheSame_HoweverItsWritten()
    {
        LoadedProjectBuilder excel = Excel(), database = Database();
        excel.Material("MB-1", "Beton", "Beton", null, null);
        database.Material("MB-1", "Beton", "Beton", null, null);
        excel.MaterialValue("MB-1", "MAT-BES-Bemerkung", Text("Zeile 1\nZeile 2"));
        database.MaterialValue("MB-1", "MAT-BES-Bemerkung", Text("Zeile 1\r\nZeile 2"));

        LoadComparison comparison = LoadComparison.Of(excel.Build(), database.Build());

        Assert.True(comparison.IsIdentical);
        Assert.False(Text("Zeile 1\nZeile 2").IsSameAs(Text("Zeile 1 Zeile 2")));
    }

    [Fact]
    public void WhatTheExcelReaderLeftOut_IsSaid_SoNothingIsCalledIdenticalThatWasntCompared()
    {
        LoadedProjectBuilder excel = Excel(), database = Database();
        excel.ObjectValue("BT-1101-01", Guete, Text("B500B"));
        database.ObjectValue("BT-1101-01", Guete, Text("B500B"));
        excel.Remark("Row 14: Bauteil_ID BT-1101-01 appears more than once. Only the first was kept.");
        excel.Remark(" ");

        LoadComparison comparison = LoadComparison.Of(excel.Build(), database.Build());

        Assert.False(comparison.IsIdentical);
        Assert.Equal(0, comparison.DifferenceCount);
        Assert.Equal(1, comparison.RemarkCount);
        Assert.Equal("No differences, but 1 remark about the workbook", comparison.Title);
        Assert.Equal(
            new[] { "Row 14: Bauteil_ID BT-1101-01 appears more than once. Only the first was kept." },
            Lines(comparison, "The Excel reader's remarks"));
        Assert.Contains("  Row 14: Bauteil_ID BT-1101-01 appears more than once.", comparison.ToText());
    }

    [Fact]
    public void RemarksComeAfterTheDifferences_AndDontCountAsDifferences()
    {
        LoadedProjectBuilder excel = Excel(), database = Database();
        excel.Object("BT-1101-01");
        excel.Remark("Row 15: no Bauteil_ID, so it cannot be matched to an element. Row skipped.");
        database.Parameter(new LoadedParameter("PER-BES-Vorname", "Vorname", "Text"));

        LoadComparison comparison = LoadComparison.Of(excel.Build(), database.Build());

        Assert.Equal("1 difference between the Excel and the database", comparison.Title);
        Assert.Equal(1, comparison.RemarkCount);
        Assert.Equal(
            new[] { ComparisonSectionKind.Difference, ComparisonSectionKind.Remark, ComparisonSectionKind.ByDesign },
            comparison.Sections.Select(section => section.Kind));
    }

    [Fact]
    public void ObjectsOnOneSide_AreListed_WithHowMuchTheyHold()
    {
        LoadedProjectBuilder excel = Excel(), database = Database();
        excel.Object("ET-3333-01");
        excel.ObjectValue("BT-1102-01", Guete, Text("B500B"));
        database.ObjectValue("ET-2101-01", Guete, Text("B500B"));
        database.ObjectValue("ET-2101-01", Dicke, Convert("Double", "m", "0.3"));

        LoadComparison comparison = LoadComparison.Of(excel.Build(), database.Build());

        Assert.Equal(new[] { "BT-1102-01 (1 value)", "ET-3333-01 (no values)" }, Lines(comparison, "Objects only in the Excel"));
        Assert.Equal(new[] { "ET-2101-01 (2 values)" }, Lines(comparison, "Objects only in the database"));
        Assert.Equal(3, comparison.DifferenceCount);
    }

    [Fact]
    public void ValuesOnlyOneSideHas_AreOneLinePerParameter_AndCountOneByOne()
    {
        LoadedProjectBuilder excel = Excel(), database = Database();
        foreach (string id in new[] { "MB-1", "MB-2", "MB-10" })
        {
            excel.Material(id, id, "Beton", null, null);
            database.Material(id, id, "Beton", null, null);
            excel.MaterialValue(id, "MAT-BES-Materialname", Text("Name " + id));
        }
        excel.Parameter(new LoadedParameter("MAT-BES-Materialname", "Materialname", "Text"));
        database.Parameter(new LoadedParameter("MAT-BES-Materialname", "Materialname", "Text"));
        database.MaterialValue("MB-2", Festigkeit, Text("C12/15"));

        LoadComparison comparison = LoadComparison.Of(excel.Build(), database.Build());

        Assert.Equal(new[] { "MAT-BES-Materialname: 3 materials, e.g. MB-1 “Name MB-1”" }, Lines(comparison, "Material values only in the Excel"));
        Assert.Equal(new[] { "MAT-BET-Festigkeitsklasse: 1 material, MB-2 “C12/15”" }, Lines(comparison, "Material values only in the database"));
        Assert.Equal(4, comparison.DifferenceCount);
    }

    [Fact]
    public void Parameters_AreComparedByTheirKey_AndByWhatRevitMakesOfThem()
    {
        LoadedProjectBuilder excel = Excel(), database = new("database");
        database.Parameter(new LoadedParameter(Dicke, "Dicke", "Number"));
        database.Parameter(new LoadedParameter(Guete, "Betonstahl Güte (Bauteilinformation)", "Text"));
        database.Parameter(new LoadedParameter("PER-BES-Vorname", "Vorname", "Text"));

        LoadComparison comparison = LoadComparison.Of(excel.Build(), database.Build());

        Assert.Equal(new[] { "BAU-GEO-Dicke: Excel Length, database Number" }, Lines(comparison, "Parameters that Revit takes differently"));
        Assert.Equal(new[] { "MAT-BET-Festigkeitsklasse (Text)" }, Lines(comparison, "Parameters only in the Excel"));
        Assert.Equal(new[] { "PER-BES-Vorname" }, Lines(comparison, "By design: parameters only in the database"));
        Assert.Equal(2, comparison.DifferenceCount);
    }

    [Fact]
    public void AParameterFiledUnderAnotherSet_IsTheSameParameter_AndSaidOnce()
    {
        LoadedProjectBuilder excel = Excel(), database = Database();
        excel.Parameter(new LoadedParameter("IDE-SPE-Betonstahl Mat.-ID", "Betonstahl Mat.-ID", "Text"));
        database.Parameter(new LoadedParameter("BAU-SPE-Betonstahl Mat.-ID", "Betonstahl Mat.-ID", "Text"));
        excel.ObjectValue("BT-1101-01", "IDE-SPE-Betonstahl Mat.-ID", Text("MB-1761-01-MET-0051"));
        database.ObjectValue("BT-1101-01", "BAU-SPE-Betonstahl Mat.-ID", Text("MB-1761-01-MET-0051"));

        LoadComparison comparison = LoadComparison.Of(excel.Build(), database.Build());

        Assert.Equal(
            new[] { "IDE-SPE-Betonstahl Mat.-ID in the Excel is BAU-SPE-Betonstahl Mat.-ID in the database" },
            Lines(comparison, "Parameters filed under another set"));
        Assert.Equal(1, comparison.DifferenceCount);
    }

    [Fact]
    public void AParameterThatCouldBeTwoOthers_IsntGuessed()
    {
        LoadedProjectBuilder excel = Excel(), database = Database();
        excel.Parameter(new LoadedParameter("IDE-SPE-Typ", "Typ", "Text"));
        database.Parameter(new LoadedParameter("BAU-SPE-Typ", "Typ (Bauteilinformation)", "Text"));
        database.Parameter(new LoadedParameter("PRI-SPE-Typ", "Typ (Produktinformation)", "Text"));

        LoadComparison comparison = LoadComparison.Of(excel.Build(), database.Build());

        Assert.Equal(new[] { "IDE-SPE-Typ (Text)" }, Lines(comparison, "Parameters only in the Excel"));
        Assert.Empty(Lines(comparison, "Parameters filed under another set"));
    }

    [Fact]
    public void TheWorkbooksFillPatternColumns_AreKnown_AsTheDatabaseHoldsPatternsWithTheMaterial()
    {
        LoadedProjectBuilder excel = Excel(), database = Database();
        excel.Parameter(new LoadedParameter("IDE-DAR-Oberfläche-Ansicht_ID", "Oberfläche-Ansicht_ID", "Text"));
        database.Parameter(new LoadedParameter("REV-DAR-Oberfläche-Ansicht_ID", "Oberfläche-Ansicht_ID", "Text"));
        excel.Material("MB-1", "Beton", "Beton", "Solid", null);
        database.Material("MB-1", "Beton", "Beton", "Solid", null);
        excel.MaterialValue("MB-1", "IDE-DAR-Oberfläche-Ansicht_ID", Text("Solid"));

        LoadComparison comparison = LoadComparison.Of(excel.Build(), database.Build());

        Assert.Equal(new[] { "REV-DAR-Oberfläche-Ansicht_ID: 1 material, MB-1 “Solid”" }, Lines(comparison, "By design: fill patterns"));
        Assert.Empty(Lines(comparison, "Material values only in the Excel"));
    }

    [Fact]
    public void AnObjectsFillPattern_IsAValueLikeAnyOther()
    {
        LoadedProjectBuilder excel = Excel(), database = Database();
        excel.ObjectValue("BT-1101-01", "IDE-DAR-Oberfläche-Ansicht_ID", Text("Solid"));
        database.Object("BT-1101-01");

        LoadComparison comparison = LoadComparison.Of(excel.Build(), database.Build());

        Assert.Equal(
            new[] { "IDE-DAR-Oberfläche-Ansicht_ID: 1 object, BT-1101-01 “Solid”" },
            Lines(comparison, "Object values only in the Excel"));
        Assert.Equal(1, comparison.DifferenceCount);
    }

    [Fact]
    public void AValueGivenTwiceDifferently_IsADifference_SoItCantHideBehindTheFirst()
    {
        LoadedProjectBuilder excel = Excel(), database = Database();
        excel.ObjectValue("ET-2101-01", Guete, Text("B500B"));
        database.ObjectValue("ET-2101-01", Guete, Text("B500B"));
        database.ObjectValue("et-2101-01", Guete, Text("B500A"));
        database.ObjectValue("ET-2101-01", Dicke, Convert("Double", "m", "0.3"));
        database.ObjectValue("ET-2101-01", Dicke, Convert("Double", "cm", "30"));
        excel.ObjectValue("ET-2101-01", Dicke, Convert("Double", "m", "0.3"));

        LoadComparison comparison = LoadComparison.Of(excel.Build(), database.Build());

        Assert.Equal(new[] { "et-2101-01 · BAU-SPE-Betonstahl Güte: “B500B” and “B500A”" }, Lines(comparison, "Given twice in the database"));
        Assert.Equal(1, comparison.DifferenceCount);
        Assert.False(comparison.IsIdentical);
    }

    [Fact]
    public void AParameterOrAMaterialGivenTwiceDifferently_IsADifferenceToo()
    {
        LoadedProjectBuilder excel = Excel(), database = Database();
        excel.Parameter(new LoadedParameter(Guete, "Betonstahl Güte", "Text"));
        excel.Parameter(new LoadedParameter("bau-geo-dicke", "Dicke", "Number"));
        excel.Material("MB-1", "Beton", "Beton", null, null);
        database.Material("MB-1", "Beton", "Beton", null, null);
        database.Material("mb-1", "Beton", "Metall", null, null);

        LoadComparison comparison = LoadComparison.Of(excel.Build(), database.Build());

        Assert.Equal(new[] { "BAU-GEO-Dicke: Length and Number" }, Lines(comparison, "Given twice in the Excel"));
        Assert.Equal(new[] { "mb-1 · Materialgruppe: “Beton” and “Metall”" }, Lines(comparison, "Given twice in the database"));
        Assert.Equal(2, comparison.DifferenceCount);
    }

    [Fact]
    public void AMaterialsNameGroupAndPatterns_AreComparedToo()
    {
        LoadedProjectBuilder excel = Excel(), database = Database();
        excel.Material("MB-1", "11-Beton", "Beton", "_Waben", null);
        database.Material("MB-1", "11-Beton, C12/15", "Beton", null, null);

        LoadComparison comparison = LoadComparison.Of(excel.Build(), database.Build());

        Assert.Equal(
            new[] { "MB-1 · Materialname: Excel “11-Beton”, database “11-Beton, C12/15”", "MB-1 · surface pattern: Excel “_Waben”, database “”" },
            Lines(comparison, "Materials that differ"));
    }

    [Fact]
    public void AMaterialOneSideOnlyGivesValuesFor_DiffersInWhatItIs()
    {
        LoadedProjectBuilder excel = Excel(), database = Database();
        excel.MaterialValue("MB-1", Festigkeit, Text("C12/15"));
        database.Material("MB-1", "11-Beton", "Beton", null, null);
        database.MaterialValue("MB-1", Festigkeit, Text("C12/15"));

        LoadComparison comparison = LoadComparison.Of(excel.Build(), database.Build());

        Assert.Equal(
            new[] { "MB-1 · Materialname: Excel “”, database “11-Beton”", "MB-1 · Materialgruppe: Excel “”, database “Beton”" },
            Lines(comparison, "Materials that differ"));
    }

    // By its name, which is what Revit gets, whatever the Material-IDs are.
    [Fact]
    public void WhatAnObjectIsMadeOf_IsComparedByTheMaterialsName()
    {
        LoadedProjectBuilder excel = Excel(), database = Database();
        excel.ObjectMaterial("BT-1101-01", "31-Beton, C35/45 ");
        database.ObjectMaterial("bt-1101-01", "13-Beton, C20/25");
        excel.ObjectMaterial("BT-1102-01", "13-Beton, C20/25");
        database.ObjectMaterial("BT-1102-01", "13-Beton, C20/25");
        excel.Object("ET-2101-01");
        database.ObjectMaterial("ET-2101-01", "0101-Beton");
        excel.Object("ET-2102-01");
        database.Object("ET-2102-01");

        LoadComparison comparison = LoadComparison.Of(excel.Build(), database.Build());

        Assert.Equal(
            new[]
            {
                "BT-1101-01 · Material: Excel “31-Beton, C35/45”, database “13-Beton, C20/25”",
                "ET-2101-01 · Material: Excel “”, database “0101-Beton”",
            },
            Lines(comparison, "Objects made of another material"));
        Assert.Equal(2, comparison.DifferenceCount);
    }

    [Fact]
    public void AnObjectGivenTwoMaterials_IsSaid_AndOnlyTheFirstIsCompared()
    {
        LoadedProjectBuilder excel = Excel(), database = Database();
        excel.ObjectMaterial("BT-1101-01", "13-Beton, C20/25");
        database.ObjectMaterial("BT-1101-01", "13-Beton, C20/25");
        database.ObjectMaterial("BT-1101-01", "31-Beton, C35/45");

        LoadComparison comparison = LoadComparison.Of(excel.Build(), database.Build());

        Assert.Equal(new[] { "BT-1101-01 · Material: “13-Beton, C20/25” and “31-Beton, C35/45”" }, Lines(comparison, "Given twice in the database"));
        Assert.Equal(1, comparison.DifferenceCount);
    }

    // The workbook has it among the object's values too. The database has it once, and nothing differs by that.
    [Fact]
    public void WhatTheWorkbookSaysOfAnObjectsMaterial_AmongItsValues_IsKnown()
    {
        LoadedProjectBuilder excel = Excel(), database = Database();
        excel.Parameter(new LoadedParameter("IDE-KAT-Material-ID", "Material-ID", "Text"));
        database.Parameter(new LoadedParameter("IDE-KAT-Material-ID", "Material-ID", "Text", true));
        excel.ObjectValue("BT-1101-01", "IDE-KAT-Material-ID", Text("MB-1761-01-BET-0013"));
        excel.ObjectMaterial("BT-1101-01", "13-Beton, C20/25");
        database.ObjectMaterial("BT-1101-01", "13-Beton, C20/25");

        LoadComparison comparison = LoadComparison.Of(excel.Build(), database.Build());

        Assert.True(comparison.IsIdentical);
        Assert.Equal(
            new[] { "IDE-KAT-Material-ID: 1 object, BT-1101-01 “MB-1761-01-BET-0013”" },
            Lines(comparison, "By design: what an object is made of"));
    }

    // Of a material, it's a value like any other; of an object, one that's no parameter of materials is too.
    [Fact]
    public void OnlyAnObjectsValues_OfParametersOfMaterials_AreKnown()
    {
        LoadedProjectBuilder excel = Excel(), database = Database();
        excel.Parameter(new LoadedParameter("MAT-BES-Materialname", "Materialname", "Text"));
        database.Parameter(new LoadedParameter("MAT-BES-Materialname", "Materialname", "Text", true));
        excel.Parameter(new LoadedParameter("PRI-MAT-Material-ID", "Material-ID (Produktinformation)", "Text"));
        database.Parameter(new LoadedParameter("PRI-MAT-Material-ID", "Material-ID", "Text"));
        excel.Material("MB-1", "Beton", "Beton", null, null);
        database.Material("MB-1", "Beton", "Beton", null, null);
        excel.MaterialValue("MB-1", "MAT-BES-Materialname", Text("Beton"));
        excel.ObjectValue("BT-1101-01", "PRI-MAT-Material-ID", Text("89z98"));
        database.Object("BT-1101-01");

        LoadComparison comparison = LoadComparison.Of(excel.Build(), database.Build());

        Assert.Equal(new[] { "MAT-BES-Materialname: 1 material, MB-1 “Beton”" }, Lines(comparison, "Material values only in the Excel"));
        Assert.Equal(
            new[] { "PRI-MAT-Material-ID (Material-ID (Produktinformation)): 1 object, BT-1101-01 “89z98”" },
            Lines(comparison, "Object values only in the Excel"));
        Assert.Empty(Lines(comparison, "By design: what an object is made of"));
        Assert.Equal(2, comparison.DifferenceCount);
    }

    [Fact]
    public void OnlyTheProjectPartOfAMaterialsId_MakesItGlobal()
    {
        LoadedProjectBuilder excel = Excel(), database = Database();
        foreach (string id in new[] { "MP-9999-99-BET-0101", "MP-9999-98-BET-0102", "MB-1761-9999-99-01", "MB-19999-99-BET-0103", "MP-9999-99" })
            excel.Material(id, id, "Beton", null, null);

        LoadComparison comparison = LoadComparison.Of(excel.Build(), database.Build());

        Assert.Equal(
            new[] { "MP-9999-98-BET-0102 (no values)", "MP-9999-99-BET-0101 (no values)" },
            Lines(comparison, "By design: global materials"));
        Assert.Equal(
            new[] { "MB-1761-9999-99-01 (no values)", "MB-19999-99-BET-0103 (no values)", "MP-9999-99 (no values)" },
            Lines(comparison, "Materials only in the Excel"));
    }

    [Fact]
    public void WhatDiffersByDesign_IsListedApart_AndDoesntCount()
    {
        LoadedProjectBuilder excel = Excel(), database = Database();
        excel.ObjectValue("BT-1101-01", Guete, Text("B500B"));
        database.ObjectValue("BT-1101-01", Guete, Text("B500B"));
        database.ObjectValue("BT-1101-01", "IDE-KAT-Bauteil_ID", Text("BT-1101-01"));
        excel.Material("MB-1", "Beton", "Beton", null, null);
        database.Material("MB-1", "Beton", "Beton", null, null);
        database.MaterialValue("MB-1", "IDE-KAT-Material-ID", Text("MB-1"));
        excel.Material("MP-9999-99-BET-0101", "Produkt", "Beton", null, null);

        LoadComparison comparison = LoadComparison.Of(excel.Build(), database.Build());

        Assert.True(comparison.IsIdentical);
        Assert.All(comparison.Sections, section => Assert.Equal(ComparisonSectionKind.ByDesign, section.Kind));
        Assert.Equal(new[] { "IDE-KAT-Bauteil_ID: 1 object, BT-1101-01 “BT-1101-01”" }, Lines(comparison, "By design: what identifies an object"));
        Assert.Equal(new[] { "IDE-KAT-Material-ID: 1 material, MB-1 “MB-1”" }, Lines(comparison, "By design: what identifies a material"));
        Assert.Equal(new[] { "MP-9999-99-BET-0101 (no values)" }, Lines(comparison, "By design: global materials"));
    }

    // The Excel import fills them since 25.11.2026, so they're compared like any other value.
    [Fact]
    public void TheNumbersABauteilIdIsMadeOf_AreComparedLikeAnyOtherValue()
    {
        LoadedProjectBuilder excel = Excel(), database = Database();
        foreach (string id in new[] { "BT-1101-01", "BT-1102-01" })
        {
            excel.ObjectValue(id, "BAU-KAT-Bauteilnummer", Convert("[integer]", "[-]", "1101"));
            database.ObjectValue(id, "BAU-KAT-Bauteilnummer", Convert("Integer", null, id.Substring(3, 4)));
        }
        database.ObjectValue("BT-1101-01", "BAU-KAT-Bauteilunternummer", Convert("Integer", null, "1"));

        LoadComparison comparison = LoadComparison.Of(excel.Build(), database.Build());

        Assert.Equal(2, comparison.DifferenceCount);
        Assert.Equal(new[] { "BT-1102-01 · BAU-KAT-Bauteilnummer: Excel “1101”, database “1102”" }, Lines(comparison, "Object values that differ"));
        Assert.Equal(new[] { "BAU-KAT-Bauteilunternummer: 1 object, BT-1101-01 “1”" }, Lines(comparison, "Object values only in the database"));
    }

    [Fact]
    public void DifferencesComeFirst_AndTheTextHoldsThemAll()
    {
        LoadedProjectBuilder excel = Excel(), database = Database();
        excel.ObjectValue("BT-1101-01", Guete, Text("B500A"));
        database.ObjectValue("BT-1101-01", Guete, Text("B500B"));
        database.ObjectValue("BT-1101-01", "IDE-KAT-Bauteil_ID", Text("BT-1101-01"));

        LoadComparison comparison = LoadComparison.Of(excel.Build(), database.Build());

        Assert.Equal(new[] { ComparisonSectionKind.Difference, ComparisonSectionKind.ByDesign }, comparison.Sections.Select(section => section.Kind));
        string text = comparison.ToText();
        Assert.StartsWith("1 difference between the Excel and the database", text);
        Assert.Contains("Object values that differ (1)", text);
        Assert.Contains("  BT-1101-01 · Betonstahl Güte: Excel “B500A”, database “B500B”", text);
        Assert.Contains("By design: what identifies an object", text);
    }

    [Fact]
    public void TheDatabasesSide_LeavesOutPlaceholders_AndCopiesOfAnObjectsMaterial()
    {
        LinkProjectData data = UlmData.With(1,
            new[]
            {
                UlmData.Value("BT-1101-01", UlmData.Dicke, "[Modeller]"),
                UlmData.Value("BT-1101-01", UlmData.Guete, "B500B"),
                UlmData.Value("BT-1101-01", UlmData.MaterialId, "MB-1"),
                UlmData.Value("ET-2101-01", UlmData.Dicke, "TBT"),
            },
            new[] { new LinkMaterial("MB-1", "Beton", "BET", "Beton", "Solid", null) },
            new[]
            {
                new LinkMaterialValue("MB-1", UlmData.Festigkeit.ParameterId, "C12/15", null, null),
                new LinkMaterialValue("MB-1", UlmData.MaterialId.ParameterId, " ", null, null),
            },
            new[]
            {
                new LinkObjectMaterial("BT-1101-01", "MB-1", "Beton", null, null),
                new LinkObjectMaterial("BT-1102-01", null, "0101-Beton", null, null),
            });

        LoadedProject project = DatabaseLoad.From(data, Convert, (dataType, unit) => dataType + "/" + unit);

        Assert.Equal("database", project.Source);
        Assert.Equal("Double/m", project.Parameters[Dicke].Kind);
        Assert.Equal(new[] { Guete }, project.Objects["BT-1101-01"].Values.Keys);
        Assert.Equal("Beton", project.Objects["BT-1101-01"].Facts["Material"]);
        Assert.Equal("0101-Beton", project.Objects["BT-1102-01"].Facts["Material"]);
        Assert.Empty(project.Objects["ET-2101-01"].Values);
        Assert.Empty(project.Objects["ET-2101-01"].Facts);
        Assert.True(project.Parameters["IDE-KAT-Material-ID"].IsOfMaterials);
        Assert.False(project.Parameters[Dicke].IsOfMaterials);
        Assert.Equal("Solid", project.Materials["MB-1"].Facts["surface pattern"]);
        Assert.Equal(new[] { Festigkeit }, project.Materials["MB-1"].Values.Keys);
    }
}
