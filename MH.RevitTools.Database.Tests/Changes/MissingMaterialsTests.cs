using System;
using System.Collections.Generic;
using System.Linq;
using MH.RevitTools.Database.Changes;
using MH.RevitTools.Database.Link;
using MH.RevitTools.Database.Tests.Checking;

namespace MH.RevitTools.Database.Tests.Changes;

/// <summary>The project's materials the model doesn't have, and what Create makes of them.</summary>
public sealed class MissingMaterialsTests
{
    private static readonly LinkMaterial Beton = new("MB-1761-01-BET-0011", "11-Beton, C12/15", "BET", "Beton", "_Waben", "Ortbeton - bewehrt");
    private static readonly LinkMaterial Stahl = new("MB-1761-01-MET-0051", "51-Betonstahl", "MET", "Metall", null, " Solid ");
    private static readonly LinkMaterial Sand = new("MB-1761-01-BOD-0002", "2-Sand", "BOD", "Boden (in situ)", null, null);

    private static LinkMaterialValue Value(LinkMaterial material, LinkParameter parameter, string value) =>
        new(material.MaterialId, parameter.ParameterId, value, null, null);

    private static LinkProjectData Data(IEnumerable<LinkMaterial> materials, params LinkMaterialValue[] values) =>
        UlmData.With(1, Array.Empty<LinkObjectValue>(), materials, values);

    private static ChangeReport Find(LinkProjectData data, params ModelMaterial[] model) => ChangeFinder.Find(
        data, Array.Empty<ModelElement>(), model, (id, parameter, value) => ValueCheck.Same, (id, which, pattern) => ValueCheck.Same);

    [Fact]
    public void AMaterialTheModelDoesntHave_IsMissing_AndNoChange()
    {
        ChangeReport report = Find(Data(new[] { Beton, Stahl, Sand }, Value(Beton, UlmData.Festigkeit, "C12/15")), new ModelMaterial(70, "51-Betonstahl"));

        Assert.Equal(new[] { "2-Sand", "11-Beton, C12/15" }, report.MissingMaterials.Select(material => material.Name));
        Assert.Equal(new[] { Sand.MaterialId, Beton.MaterialId }, report.MissingMaterials.Select(material => material.MaterialId));
        Assert.True(report.IsUpToDate);
        Assert.Empty(report.Groups);
        Assert.Empty(report.NotApplied);
    }

    [Fact]
    public void WithEveryMaterialThere_NoneIsMissing()
    {
        ChangeReport report = Find(Data(new[] { Beton }), new ModelMaterial(70, " 11-beton, c12/15 "));

        Assert.Empty(report.MissingMaterials);
    }

    // Revit tells materials apart by name, so only one of them can be created.
    [Fact]
    public void TwoMissingOnesOfOneName_OnlyTheFirstIsOffered_AndItSaysSo()
    {
        var second = new LinkMaterial("MB-1761-01-BET-0099", "11-BETON, C12/15 ", "BET", "Beton", null, null);

        ChangeReport report = Find(Data(new[] { Beton, second }));

        Assert.Equal(Beton.MaterialId, Assert.Single(report.MissingMaterials).MaterialId);
        NotAppliedItem item = Assert.Single(report.NotApplied);
        Assert.Equal(NotAppliedReason.SameMaterialName, item.Reason);
        Assert.Equal(second.MaterialId, item.Text);
    }

    [Fact]
    public void TheSameMaterialTwice_IsOffered_Once()
    {
        ChangeReport report = Find(Data(new[] { Beton, Beton }));

        Assert.Single(report.MissingMaterials);
        Assert.Empty(report.NotApplied);
    }

    // Every later step takes the IDs trimmed and in any case, so the list does, too.
    [Fact]
    public void IdsThatDifferOnlyInCaseOrSpaces_AreOneMaterial()
    {
        var same = new LinkMaterial(" mb-1761-01-bet-0011 ", "Anders benannt", "BET", "Beton", null, null);
        LinkProjectData data = Data(new[] { Beton, same }, Value(same, UlmData.Festigkeit, "C12/15"));

        ChangeReport report = Find(data);

        Assert.Equal(Beton.MaterialId, Assert.Single(report.MissingMaterials).MaterialId);
        PlannedMaterial planned = Assert.Single(CreationPlan.For(data, report.MissingMaterials).Materials);
        Assert.Equal(Beton.Name, planned.Name);
        Assert.Single(planned.Values);
    }

    // As in the Excel import: without a name or a group there's nothing to make.
    [Theory]
    [InlineData(" ", "Beton", "MB-1761-01-BET-0098", "Materialname")]
    [InlineData("98-Beton", " ", "98-Beton", "Materialgruppe")]
    [InlineData("98-Beton", null, "98-Beton", "Materialgruppe")]
    public void AMaterialWithoutNameOrGroup_IsNotOffered_AndItSaysWhy(string name, string group, string shown, string lacks)
    {
        ChangeReport report = Find(Data(new[] { new LinkMaterial("MB-1761-01-BET-0098", name, "BET", group, null, null) }));

        Assert.Empty(report.MissingMaterials);
        NotAppliedItem item = Assert.Single(report.NotApplied);
        Assert.Equal(NotAppliedReason.NotCreatable, item.Reason);
        Assert.Equal(shown, item.Name);
        Assert.Equal(lacks, item.Text);
    }

    // Its values still go in, as the Excel import reads them too.
    [Fact]
    public void AMaterialWithoutAGroup_ThatTheModelHas_IsComparedAsEver()
    {
        var plain = new LinkMaterial("MB-1761-01-BET-0098", "98-Beton", "BET", null, null, null);
        ChangeReport report = ChangeFinder.Find(Data(new[] { plain }, Value(plain, UlmData.Festigkeit, "C16/20")),
            Array.Empty<ModelElement>(), new[] { new ModelMaterial(70, "98-Beton") }, (id, parameter, value) => ValueCheck.Differs(""));

        Assert.Equal(1, report.ChangeCount);
        Assert.Empty(report.NotApplied);
    }

    [Fact]
    public void Create_MakesWhatIsListed_WithGroupPatternsAndValues()
    {
        LinkProjectData data = Data(new[] { Beton, Stahl, Sand },
            Value(Beton, UlmData.Festigkeit, " C12/15 "), Value(Stahl, UlmData.Festigkeit, "B500B"));
        ChangeReport report = Find(data, new ModelMaterial(70, "2-Sand"));

        CreationPlan plan = CreationPlan.For(data, report.MissingMaterials);

        Assert.Equal(0, plan.Outdated);
        Assert.Equal(new[] { Beton.MaterialId, Stahl.MaterialId }, plan.Materials.Select(material => material.MaterialId));
        PlannedMaterial beton = plan.Materials[0];
        Assert.Equal(("11-Beton, C12/15", "Beton", "_Waben", "Ortbeton - bewehrt"), (beton.Name, beton.Group, beton.SurfacePattern, beton.CutPattern));
        PlannedValue value = Assert.Single(beton.Values);
        Assert.Equal(("Festigkeitsklasse", " C12/15 "), (value.Parameter.RevitName, value.Value));
        Assert.Equal(("", "Solid"), (plan.Materials[1].SurfacePattern, plan.Materials[1].CutPattern));
    }

    [Fact]
    public void Create_LeavesOut_WhatApplyWouldLeaveOut()
    {
        var other = new LinkParameter(500, Guid.Empty, "Ohne GUID", "MAT", "BET", "Ohne GUID", "String", null, LinkTarget.Material, new[] { "OST_Materials" });
        LinkProjectData data = new(UlmData.ProjectId, 1, UlmData.Dictionary.Concat(new[] { other }), Array.Empty<LinkObjectValue>(), new[] { Beton },
            new[]
            {
                Value(Beton, UlmData.Festigkeit, "TBT"),
                Value(Beton, UlmData.MaterialId, "a"),
                Value(Beton, UlmData.MaterialId, "b"),
                Value(Beton, UlmData.Guete, "B500B"),
                Value(Beton, other, "x"),
                new LinkMaterialValue(Beton.MaterialId, 9999, "unknown", null, null),
            });

        PlannedMaterial beton = Assert.Single(CreationPlan.For(data, Find(data).MissingMaterials).Materials);

        Assert.Empty(beton.Values);
    }

    [Fact]
    public void Create_LeavesOut_WhatTheDatabaseHasChangedSince()
    {
        IReadOnlyList<MissingMaterial> listed = Find(Data(new[] { Beton, Stahl, Sand })).MissingMaterials;
        var renamed = new LinkMaterial(Beton.MaterialId, "11-Beton, C20/25", "BET", "Beton", null, null);

        CreationPlan plan = CreationPlan.For(Data(new[] { renamed, Sand }), listed);

        Assert.Equal(new[] { Sand.MaterialId }, plan.Materials.Select(material => material.MaterialId));
        Assert.Equal(2, plan.Outdated);
    }

    [Fact]
    public void Create_LeavesOut_AMaterialThatHasLostItsGroup()
    {
        IReadOnlyList<MissingMaterial> listed = Find(Data(new[] { Beton })).MissingMaterials;

        CreationPlan plan = CreationPlan.For(Data(new[] { new LinkMaterial(Beton.MaterialId, Beton.Name, "BET", " ", null, null) }), listed);

        Assert.Empty(plan.Materials);
        Assert.Equal(1, plan.Outdated);
    }

    // What it looks like now isn't what was shown in the list, so the newer one is fine.
    [Fact]
    public void Create_TakesTheDatabasesGroupAndPatterns_AsTheyAreNow()
    {
        IReadOnlyList<MissingMaterial> listed = Find(Data(new[] { Beton })).MissingMaterials;
        var changed = new LinkMaterial(Beton.MaterialId, Beton.Name + " ", "BET", "Metall", "Solid", null);

        PlannedMaterial planned = Assert.Single(CreationPlan.For(Data(new[] { changed }), listed).Materials);

        Assert.Equal(("11-Beton, C12/15", "Metall", "Solid", ""), (planned.Name, planned.Group, planned.SurfacePattern, planned.CutPattern));
    }

    [Fact]
    public void Create_NeverMakesWhatWasNotListed()
    {
        LinkProjectData data = Data(new[] { Beton, Stahl });

        CreationPlan plan = CreationPlan.For(data, Find(data).MissingMaterials.Where(material => material.MaterialId == Stahl.MaterialId));

        Assert.Equal(Stahl.MaterialId, Assert.Single(plan.Materials).MaterialId);
        Assert.Equal(0, plan.Outdated);
    }

    [Fact]
    public void Create_NeedsItsInput()
    {
        Assert.Throws<ArgumentNullException>(() => CreationPlan.For(null, Array.Empty<MissingMaterial>()));
        Assert.Throws<ArgumentNullException>(() => CreationPlan.For(Data(new[] { Beton }), null));
    }
}
