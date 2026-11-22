using System;
using System.Collections.Generic;
using System.Linq;
using MH.RevitTools.Database.Changes;
using MH.RevitTools.Database.Link;
using MH.RevitTools.Database.Tests.Checking;

namespace MH.RevitTools.Database.Tests.Changes;

public sealed class ChangeFinderTests
{
    private static readonly DateTime Monday = new(2026, 11, 16, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Today = new(2026, 11, 22, 13, 28, 0, DateTimeKind.Utc);

    /// <summary>
    /// The model's values, by element (or material) and parameter, as the Revit side would read them. A
    /// parameter an element doesn't have isn't listed; "#bad" stands for a value that can't go in.
    /// </summary>
    private sealed class FakeModel
    {
        private readonly Dictionary<(long, int), string> _values = new();

        public List<(long Id, int ParameterId, string Value)> Asked { get; } = new();

        public FakeModel Has(long id, LinkParameter parameter, string value)
        {
            _values[(id, parameter.ParameterId)] = value;
            return this;
        }

        public ValueCheck Compare(long id, LinkParameter parameter, string databaseValue)
        {
            Asked.Add((id, parameter.ParameterId, databaseValue));
            if (!_values.TryGetValue((id, parameter.ParameterId), out string model))
                return ValueCheck.NoParameter;
            if (databaseValue == "#bad")
                return ValueCheck.CantCompare("\"#bad\" isn't a number");
            return string.Equals(model, databaseValue, StringComparison.Ordinal) ? ValueCheck.Same : ValueCheck.Differs(model);
        }
    }

    private static ChangeReport Find(LinkProjectData data, FakeModel model, IEnumerable<ModelElement> elements,
        IEnumerable<ModelMaterial> materials = null) =>
        ChangeFinder.Find(data, elements, materials ?? Array.Empty<ModelMaterial>(), model.Compare);

    [Fact]
    public void ADifferingValue_IsOneChange_ForAllTheObjectsElements()
    {
        LinkProjectData data = UlmData.With(1, new[]
        {
            UlmData.Value("BT-1101-01", UlmData.Bauteil, "Bodenplatte"),
            UlmData.Value("BT-1101-01", UlmData.Guete, "B500A", Today, "M. Keller"),
        });
        var model = new FakeModel()
            .Has(1, UlmData.Guete, "B500B").Has(1, UlmData.Bauteil, "Bodenplatte")
            .Has(2, UlmData.Guete, "B500B").Has(2, UlmData.Bauteil, "Bodenplatte");

        ChangeReport report = Find(data, model, new[] { new ModelElement(1, "BT-1101-01", null), new ModelElement(2, "bt-1101-01", null) });

        Assert.Equal(1, report.ChangeCount);
        Assert.False(report.IsUpToDate);
        ChangeGroup group = Assert.Single(report.Groups);
        Assert.Equal("BT-1101-01", group.Key);
        Assert.Equal("Bodenplatte", group.Name);
        Assert.False(group.IsMaterial);
        Assert.Equal(new long[] { 1, 2 }, group.ElementIds);
        ChangeRow row = Assert.Single(group.Rows);
        Assert.Equal("Betonstahl Güte", row.Parameter);
        Assert.Equal("B500B", row.From);
        Assert.Equal("B500A", row.To);
        Assert.Equal("M. Keller", row.ChangedBy);
        Assert.Equal(Today, row.ChangedAtUtc);
        Assert.Equal(new long[] { 1, 2 }, row.ElementIds);
        Assert.Equal(new long[] { 1, 2 }, report.Counts.Linked);
    }

    [Fact]
    public void AModelThatMatches_IsUpToDate_AndNamesTheDatabasesNewestChange()
    {
        LinkProjectData data = UlmData.With(1, new[]
        {
            UlmData.Value("BT-1101-01", UlmData.Guete, "B500B", Monday, "TheAdmin"),
            UlmData.Value("ET-2101-01", UlmData.Guete, "B500A", Today, "S. Brandt"),
        });

        ChangeReport report = Find(data, new FakeModel().Has(1, UlmData.Guete, "B500B"), new[] { new ModelElement(1, "BT-1101-01", null) });

        Assert.True(report.IsUpToDate);
        Assert.Empty(report.Groups);
        Assert.Equal(Today, report.NewestChangeUtc);
        Assert.Equal("S. Brandt", report.NewestChangeBy);
    }

    [Fact]
    public void Elements_AreCounted_ByWhetherTheProjectHasTheirBauteilId()
    {
        LinkProjectData data = UlmData.With(1, new[] { UlmData.Value("BT-1101-01", UlmData.Guete, "B500B") });

        ChangeReport report = Find(data, new FakeModel(), new[]
        {
            new ModelElement(1, "BT-1101-01", null),
            new ModelElement(2, " ", null),
            new ModelElement(3, null, null),
            new ModelElement(4, "BT-9999-01", null),
        });

        Assert.Equal(new long[] { 1 }, report.Counts.Linked);
        Assert.Equal(new long[] { 2, 3 }, report.Counts.WithoutId);
        Assert.Equal(new long[] { 4 }, report.Counts.IdNotInProject);
    }

    [Fact]
    public void Placeholders_AndTheBauteilIdItself_AreNeverChanges()
    {
        LinkProjectData data = UlmData.With(1, new[]
        {
            UlmData.Value("BT-1101-01", UlmData.Dicke, "[Modeller]"),
            UlmData.Value("BT-1101-01", UlmData.Guete, "TBT"),
            UlmData.Value("BT-1101-01", UlmData.Bewehrt, "  "),
            UlmData.Value("BT-1101-01", UlmData.Bauteil, "-"),
            UlmData.Value("BT-1101-01", UlmData.Teilelement, "[ET]"),
            UlmData.Value("BT-1101-01", UlmData.BauteilId, "BT-1101-01-stale"),
        });
        var model = new FakeModel();

        ChangeReport report = Find(data, model, new[] { new ModelElement(1, "BT-1101-01", null) });

        Assert.True(report.IsUpToDate);
        Assert.Empty(model.Asked);
        Assert.Empty(report.MissingParameters);
    }

    [Fact]
    public void AnObjectsMaterialReferences_AreLeftToTheMaterial()
    {
        LinkProjectData data = UlmData.With(1, new[] { UlmData.Value("BT-1101-01", UlmData.MaterialId, "MB-1761-01-BET-0031") });
        var model = new FakeModel();

        ChangeReport report = Find(data, model, new[] { new ModelElement(1, "BT-1101-01", null) });

        Assert.Empty(model.Asked);
        Assert.Empty(report.MissingParameters);
    }

    [Fact]
    public void ElementsWithDifferentValues_ShowHowManyThereAre_AndAnEmptyValueIsSaidSo()
    {
        LinkProjectData data = UlmData.With(1, new[]
        {
            UlmData.Value("BT-1101-01", UlmData.Guete, "B500A"),
            UlmData.Value("ET-2101-01", UlmData.Guete, "B500A"),
        });
        var model = new FakeModel()
            .Has(1, UlmData.Guete, "B500B").Has(2, UlmData.Guete, "").Has(3, UlmData.Guete, "B500A")
            .Has(4, UlmData.Guete, null);

        ChangeReport report = Find(data, model, new[]
        {
            new ModelElement(1, "BT-1101-01", null), new ModelElement(2, "BT-1101-01", null), new ModelElement(3, "BT-1101-01", null),
            new ModelElement(4, "ET-2101-01", null),
        });

        ChangeRow various = report.Groups.Single(g => g.Key == "BT-1101-01").Rows.Single();
        Assert.Equal("2 different values", various.From);
        Assert.Equal(new long[] { 1, 2 }, various.ElementIds);
        Assert.Equal("(empty)", report.Groups.Single(g => g.Key == "ET-2101-01").Rows.Single().From);
        Assert.Equal(2, report.ChangeCount);
    }

    [Fact]
    public void ParametersTheModelLacks_AreListedOnce_AndValuesThatDontFit_AreProblems()
    {
        LinkProjectData data = UlmData.With(1, new[]
        {
            UlmData.Value("BT-1101-01", UlmData.Guete, "B500A"),
            UlmData.Value("BT-1101-01", UlmData.Dicke, "#bad"),
            UlmData.Value("ET-2101-01", UlmData.Guete, "B500A"),
        });
        var model = new FakeModel().Has(1, UlmData.Dicke, "0.3");

        ChangeReport report = Find(data, model, new[] { new ModelElement(1, "BT-1101-01", null), new ModelElement(2, "ET-2101-01", null) });

        Assert.Equal(new[] { "Betonstahl Güte" }, report.MissingParameters);
        Assert.Equal(new[] { "Dicke: \"#bad\" isn't a number" }, report.Problems);
        Assert.True(report.IsUpToDate);
    }

    [Fact]
    public void Materials_MeetTheirsByName_AndTakeOnlyMaterialParameters()
    {
        var beton = new LinkMaterial("MB-1761-01-BET-0011", "11-Beton, C12/15", "BET", "Beton", null, null);
        var stahl = new LinkMaterial("MB-1761-01-MET-0051", "51-Betonstahl", "MET", "Metall", null, null);
        LinkProjectData data = UlmData.With(1, Array.Empty<LinkObjectValue>(), new[] { beton, stahl }, new[]
        {
            new LinkMaterialValue(beton.MaterialId, UlmData.Festigkeit.ParameterId, "C16/20", Today, "TheAdmin"),
            new LinkMaterialValue(beton.MaterialId, UlmData.Guete.ParameterId, "B500A", Today, "TheAdmin"),
            new LinkMaterialValue(stahl.MaterialId, UlmData.Festigkeit.ParameterId, "B500B", Today, "TheAdmin"),
        });
        var model = new FakeModel().Has(70, UlmData.Festigkeit, "C12/15").Has(70, UlmData.Guete, "B500B");

        ChangeReport report = Find(data, model, Array.Empty<ModelElement>(), new[] { new ModelMaterial(70, " 11-beton, c12/15 ") });

        ChangeGroup group = Assert.Single(report.Groups);
        Assert.True(group.IsMaterial);
        Assert.Equal("MB-1761-01-BET-0011", group.Key);
        Assert.Equal("11-Beton, C12/15", group.Name);
        Assert.Equal(new long[] { 70 }, group.ElementIds);
        ChangeRow row = Assert.Single(group.Rows);
        Assert.Equal("Festigkeitsklasse", row.Parameter);
        Assert.Equal("C12/15", row.From);
        Assert.Equal("C16/20", row.To);
    }

    [Fact]
    public void AnObjectIsNamed_ByItsBauteilAndTeilelement_ElseByItsElementsType()
    {
        LinkProjectData data = UlmData.With(1, new[]
        {
            UlmData.Value("ET-2101-01", UlmData.Bauteil, "Schlitzrinne"),
            UlmData.Value("ET-2101-01", UlmData.Teilelement, "links"),
            UlmData.Value("ET-2101-01", UlmData.Guete, "X"),
            UlmData.Value("BT-1101-01", UlmData.Guete, "X"),
            UlmData.Value("BT-1102-01", UlmData.Guete, "X"),
        });
        var model = new FakeModel().Has(1, UlmData.Guete, "").Has(2, UlmData.Guete, "").Has(3, UlmData.Guete, "").Has(4, UlmData.Guete, "");

        ChangeReport report = Find(data, model, new[]
        {
            new ModelElement(1, "ET-2101-01", "Rinne: 30"),
            new ModelElement(2, "BT-1101-01", "Bodenplatte: 30 cm"),
            new ModelElement(3, "BT-1102-01", "Wand: 30 cm"),
            new ModelElement(4, "BT-1102-01", "Wand: 40 cm"),
        });

        Assert.Equal("Schlitzrinne links", report.Groups.Single(g => g.Key == "ET-2101-01").Name);
        Assert.Equal("Bodenplatte: 30 cm", report.Groups.Single(g => g.Key == "BT-1101-01").Name);
        Assert.Equal("2 types", report.Groups.Single(g => g.Key == "BT-1102-01").Name);
    }

    [Fact]
    public void TheMostRecentlyChanged_ComeFirst_ThenInIdOrder()
    {
        LinkProjectData data = UlmData.With(1, new[]
        {
            UlmData.Value("BT-1101-01", UlmData.Guete, "X", Monday, "A"),
            UlmData.Value("BT-201-01", UlmData.Guete, "X", Monday, "A"),
            UlmData.Value("ET-2101-01", UlmData.Guete, "X", Today, "B"),
            UlmData.Value("ET-2101-01", UlmData.Dicke, "0.4", Monday, "A"),
            UlmData.Value("ET-2101-01", UlmData.Bewehrt, "true", Today.AddMinutes(1), "C"),
        });
        var model = new FakeModel()
            .Has(1, UlmData.Guete, "").Has(2, UlmData.Guete, "")
            .Has(3, UlmData.Guete, "").Has(3, UlmData.Dicke, "0.3").Has(3, UlmData.Bewehrt, "No");

        ChangeReport report = Find(data, model, new[]
        {
            new ModelElement(1, "BT-1101-01", null), new ModelElement(2, "BT-201-01", null), new ModelElement(3, "ET-2101-01", null),
        });

        Assert.Equal(new[] { "ET-2101-01", "BT-201-01", "BT-1101-01" }, report.Groups.Select(g => g.Key));
        Assert.Equal(new[] { "Bewehrt", "Betonstahl Güte", "Dicke" }, report.Groups[0].Rows.Select(r => r.Parameter));
        Assert.Equal(new[] { "Yes", "X", "0.4 m" }, report.Groups[0].Rows.Select(r => r.To));
        Assert.Equal(5, report.ChangeCount);
    }

    [Theory]
    [InlineData("Double", "m", "0.3", "0.3 m")]
    [InlineData("Double", "m2", "12", "12 m²")]
    [InlineData("Double", "m3", "1.5", "1.5 m³")]
    [InlineData("Double", "grad", "90", "90°")]
    [InlineData("Boolean", null, "true", "Yes")]
    [InlineData("Boolean", null, "0", "No")]
    [InlineData("Boolean", null, "vielleicht", "vielleicht")]
    [InlineData("String", null, " B500A ", "B500A")]
    public void DatabaseValues_AreShownWithTheirUnit(string dataType, string unit, string value, string shown)
    {
        var parameter = new LinkParameter(1, Guid.NewGuid(), "P", "S", "G", "P", dataType, unit, LinkTarget.Object, new[] { "OST_GenericModel" });

        Assert.Equal(shown, ChangeFinder.Display(parameter, value));
    }
}
