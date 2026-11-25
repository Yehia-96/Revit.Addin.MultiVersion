using System;
using System.Collections.Generic;
using System.Linq;
using MH.RevitTools.Database.Changes;
using MH.RevitTools.Database.Checking;
using MH.RevitTools.Database.Link;
using MH.RevitTools.Database.Tests.Checking;

namespace MH.RevitTools.Database.Tests.Changes;

/// <summary>A material's fill patterns, which the database holds with the material: listed and applied like its values.</summary>
public sealed class MaterialPatternTests
{
    private static readonly DateTime Today = new(2026, 11, 24, 9, 0, 0, DateTimeKind.Utc);
    private static readonly LinkMaterial Beton = new("MB-1761-01-BET-0011", "11-Beton, C12/15", "BET", "Beton", " _Waben ", "Ortbeton - bewehrt");
    private static readonly LinkMaterial Stahl = new("MB-1761-01-MET-0051", "51-Betonstahl", "MET", "Metall", null, "Ortbeton - bewehrt");

    private static readonly ModelMaterial[] Model = { new(70, "11-Beton, C12/15"), new(71, "51-Betonstahl") };

    private static LinkProjectData Data(LinkMaterial[] materials, params LinkMaterialValue[] values) =>
        UlmData.With(1, Array.Empty<LinkObjectValue>(), materials, values);

    private static ChangeReport Find(LinkProjectData data, ComparePattern compare, CompareValue values = null) => ChangeFinder.Find(
        data, Array.Empty<ModelElement>(), Model, values ?? ((id, parameter, value) => ValueCheck.Same), compare);

    [Fact]
    public void ADifferingPattern_IsAChangeOfTheMaterial()
    {
        var asked = new List<(long, MaterialPattern, string)>();
        ChangeReport report = Find(Data(new[] { Beton, Stahl }), (id, which, pattern) =>
        {
            asked.Add((id, which, pattern));
            return which == MaterialPattern.Surface ? ValueCheck.Differs("Diagonal") : ValueCheck.Same;
        });

        // Stahl has no surface pattern in the database, so the material keeps its own and nothing asks.
        Assert.Equal(new[]
        {
            (70L, MaterialPattern.Surface, "_Waben"), (70L, MaterialPattern.Cut, "Ortbeton - bewehrt"), (71L, MaterialPattern.Cut, "Ortbeton - bewehrt"),
        }, asked);
        Assert.Equal(1, report.ChangeCount);
        ChangeGroup group = Assert.Single(report.Groups);
        Assert.True(group.IsMaterial);
        Assert.Equal((Beton.MaterialId, Beton.Name), (group.Key, group.Name));
        ChangeRow row = Assert.Single(group.Rows);
        Assert.Equal(MaterialPattern.Surface, row.Pattern);
        Assert.Equal(("Surface pattern", "Diagonal", "_Waben", "_Waben"), (row.Parameter, row.From, row.To, row.Value));
        Assert.Equal(new[] { 70L }, row.ElementIds);
        Assert.Null(row.ChangedAtUtc);
        Assert.Equal("", PaneTexts.Who(row, Today, TimeZoneInfo.Utc));
    }

    // As for values: they say that there's none yet, or on purpose.
    [Theory]
    [InlineData("-")]
    [InlineData("TBT")]
    [InlineData("[Modeller]")]
    [InlineData("  ")]
    public void APlaceholder_LeavesTheMaterialsPatternAsItIs(string pattern)
    {
        var material = new LinkMaterial(Beton.MaterialId, Beton.Name, "BET", "Beton", pattern, pattern);

        ChangeReport report = Find(Data(new[] { material }), (id, which, name) => throw new InvalidOperationException("Nothing to ask for."));

        Assert.True(report.IsUpToDate);
        Assert.Empty(report.NotApplied);
    }

    [Fact]
    public void WithoutAPatternInTheModel_TheRowSaysEmpty()
    {
        ChangeReport report = Find(Data(new[] { Stahl }), (id, which, pattern) => ValueCheck.Differs(""));

        Assert.Equal("(empty)", Assert.Single(Assert.Single(report.Groups).Rows).From);
    }

    [Fact]
    public void Patterns_ComeAfterTheValues_SurfaceFirst()
    {
        LinkProjectData data = Data(new[] { Beton }, new LinkMaterialValue(Beton.MaterialId, UlmData.Festigkeit.ParameterId, "C16/20", Today, "TheAdmin"));

        ChangeReport report = Find(data, (id, which, pattern) => ValueCheck.Differs(""), (id, parameter, value) => ValueCheck.Differs("C12/15"));

        ChangeGroup group = Assert.Single(report.Groups);
        Assert.Equal(new[] { "Festigkeitsklasse", "Surface pattern", "Cut pattern" }, group.Rows.Select(row => row.Parameter));
        Assert.Equal(3, report.ChangeCount);
        Assert.Equal(Today, group.NewestChangeUtc);
    }

    // Values have no pattern, whatever they're called.
    [Fact]
    public void AValuesRow_HasNoPattern()
    {
        LinkProjectData data = Data(new[] { Beton }, new LinkMaterialValue(Beton.MaterialId, UlmData.Festigkeit.ParameterId, "C16/20", null, null));

        ChangeReport report = Find(data, (id, which, pattern) => ValueCheck.Same, (id, parameter, value) => ValueCheck.Differs(""));

        Assert.Null(Assert.Single(Assert.Single(report.Groups).Rows).Pattern);
    }

    [Fact]
    public void APatternTheModelDoesntHave_IsNotApplied_OnceForAllMaterials()
    {
        ChangeReport report = Find(Data(new[] { Beton, Stahl }),
            (id, which, pattern) => which == MaterialPattern.Cut ? ValueCheck.NoPattern : ValueCheck.Same);

        Assert.True(report.IsUpToDate);
        NotAppliedItem item = Assert.Single(report.NotApplied);
        Assert.Equal(NotAppliedReason.MissingPattern, item.Reason);
        Assert.Equal("Ortbeton - bewehrt", item.Name);
        Assert.Equal(new[] { "Cut pattern" }, item.Parameters);
        Assert.Equal(new[] { 70L, 71L }, item.ElementIds);
        Assert.Equal("The fill pattern “Ortbeton - bewehrt” is not in this model", ApplyTexts.Title(item));
        Assert.Equal("It's the cut pattern of 2 materials. Add it to the model (Manage, Additional Settings, Fill Patterns), or copy "
            + "something that uses it from another model; then it can be applied.", ApplyTexts.Detail(item));
    }

    [Fact]
    public void AMissingPattern_ForSurfaceAndCut_NamesBoth()
    {
        var both = new LinkMaterial(Beton.MaterialId, Beton.Name, "BET", "Beton", "_Waben", "_waben");

        NotAppliedItem item = Assert.Single(Find(Data(new[] { both }), (id, which, pattern) => ValueCheck.NoPattern).NotApplied);

        Assert.Equal(new[] { "Surface pattern", "Cut pattern" }, item.Parameters);
        Assert.StartsWith("It's the surface pattern and the cut pattern of 1 material.", ApplyTexts.Detail(item));
    }

    [Fact]
    public void WhatCantBeSetNow_SaysWhy_AsForValues()
    {
        ChangeReport report = Find(Data(new[] { Beton, Stahl }), (id, which, pattern) => id == 71
            ? ValueCheck.OutOfDate
            : which == MaterialPattern.Surface ? ValueCheck.Borrowed("M. Keller") : ValueCheck.CantCompare("Revit couldn't read it"));

        Assert.Empty(report.Groups);
        Assert.Equal(new[] { NotAppliedReason.Borrowed, NotAppliedReason.OutOfDate, NotAppliedReason.DoesntFit }, report.NotApplied.Select(item => item.Reason));
        Assert.Equal("M. Keller", report.NotApplied[0].Name);
        Assert.Equal(new[] { 70L }, report.NotApplied[0].ElementIds);
        Assert.Equal("Cut pattern: Revit couldn't read it", report.NotApplied[2].Text);
        Assert.True(report.HasBlockedDifferences);
    }

    // A material can be there more than once, e.g. with a trailing space.
    [Fact]
    public void SeveralMaterialsOfOneName_AreOneChange()
    {
        ChangeReport report = ChangeFinder.Find(Data(new[] { Stahl }), Array.Empty<ModelElement>(),
            new[] { new ModelMaterial(71, "51-Betonstahl"), new ModelMaterial(72, "51-betonstahl ") },
            (id, parameter, value) => ValueCheck.Same, (id, which, pattern) => ValueCheck.Differs(id == 71 ? "Diagonal" : "Beton"));

        ChangeRow row = Assert.Single(Assert.Single(report.Groups).Rows);
        Assert.Equal(new[] { 71L, 72L }, row.ElementIds);
        Assert.Equal("2 different values", row.From);
    }

    [Fact]
    public void WithoutAnAnswerFromRevit_ItIsListedAsNotReadable()
    {
        NotAppliedItem item = Assert.Single(Find(Data(new[] { Stahl }), (id, which, pattern) => null).NotApplied);

        Assert.Equal(NotAppliedReason.DoesntFit, item.Reason);
    }

    [Fact]
    public void WithNobodyToAskAboutPatterns_OnlyValuesAreCompared()
    {
        Assert.True(Find(Data(new[] { Beton, Stahl }), null).IsUpToDate);
    }

    [Fact]
    public void Apply_SetsTheListedPatterns()
    {
        LinkProjectData data = Data(new[] { Beton, Stahl });
        ChangeReport report = Find(data, (id, which, pattern) => ValueCheck.Differs(""));

        ApplyPlan plan = ApplyPlan.For(data, report.Groups);

        Assert.False(plan.IsEmpty);
        Assert.Empty(plan.Objects);
        Assert.Equal(0, plan.Outdated);
        PlannedValues beton = plan.Materials.Single(material => material.Key == Beton.MaterialId);
        Assert.Empty(beton.Values);
        Assert.Equal(new[] { (MaterialPattern.Surface, "_Waben"), (MaterialPattern.Cut, "Ortbeton - bewehrt") },
            beton.Patterns.Select(pattern => (pattern.Which, pattern.Name)));
        Assert.True(plan.IsListed(Beton.MaterialId, MaterialPattern.Surface, 70));
        Assert.False(plan.IsListed(Beton.MaterialId, MaterialPattern.Surface, 71));
        Assert.True(plan.IsListed(Stahl.MaterialId.ToLowerInvariant(), MaterialPattern.Cut, 71));
        Assert.False(plan.IsListed(Stahl.MaterialId, MaterialPattern.Surface, 71));
    }

    [Fact]
    public void Apply_SetsValuesAndPatternsOfOneMaterialTogether()
    {
        LinkProjectData data = Data(new[] { Stahl }, new LinkMaterialValue(Stahl.MaterialId, UlmData.Festigkeit.ParameterId, "B500B", null, null));

        PlannedValues material = Assert.Single(ApplyPlan.For(data,
            Find(data, (id, which, pattern) => ValueCheck.Differs(""), (id, parameter, value) => ValueCheck.Differs("")).Groups).Materials);

        Assert.Equal("B500B", Assert.Single(material.Values).Value);
        Assert.Equal("Ortbeton - bewehrt", Assert.Single(material.Patterns).Name);
    }

    [Theory]
    [InlineData("Solid")]
    [InlineData(null)]
    public void Apply_LeavesOut_APatternTheDatabaseHasChangedSince(string now)
    {
        ChangeReport report = Find(Data(new[] { Stahl }), (id, which, pattern) => ValueCheck.Differs(""));

        ApplyPlan plan = ApplyPlan.For(Data(new[] { new LinkMaterial(Stahl.MaterialId, Stahl.Name, "MET", "Metall", null, now) }), report.Groups);

        Assert.True(plan.IsEmpty);
        Assert.Equal(1, plan.Outdated);
    }

    // Otherwise the values of another material would be meant.
    [Fact]
    public void Apply_LeavesOut_APatternOfAMaterialThatIsGone()
    {
        ChangeReport report = Find(Data(new[] { Stahl }), (id, which, pattern) => ValueCheck.Differs(""));

        Assert.Equal(1, ApplyPlan.For(Data(Array.Empty<LinkMaterial>()), report.Groups).Outdated);
    }

    [Fact]
    public void AnAppliedPattern_CountsAsOneChange()
    {
        var tally = new ApplyTally();
        tally.WrittenPattern(Beton.MaterialId, MaterialPattern.Surface, 70);
        tally.WrittenPattern(Beton.MaterialId.ToLowerInvariant(), MaterialPattern.Surface, 72);
        tally.WrittenPattern(Beton.MaterialId, MaterialPattern.Cut, 70);
        tally.Written(Beton.MaterialId, true, UlmData.Festigkeit.Guid, 70);

        ApplyResult result = tally.Result(0);

        Assert.Equal((3, 0, 2), (result.Changes, result.Elements, result.Materials));
        Assert.Equal("3 changes applied to 2 materials", ApplyTexts.AppliedTitle(result));
    }
}
