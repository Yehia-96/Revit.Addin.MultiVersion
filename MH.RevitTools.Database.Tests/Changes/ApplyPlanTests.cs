using System;
using System.Linq;
using MH.RevitTools.Database.Changes;
using MH.RevitTools.Database.Link;
using MH.RevitTools.Database.Tests.Checking;

namespace MH.RevitTools.Database.Tests.Changes;

public sealed class ApplyPlanTests
{
    private static readonly LinkMaterial Beton = new("MB-1761-01-BET-0011", "11-Beton, C12/15", "BET", "Beton", null, null);

    private static LinkProjectData Data(string guete = "B500A", string festigkeit = "C16/20") => UlmData.With(1,
        new[]
        {
            UlmData.Value("BT-1101-01", UlmData.Guete, guete),
            UlmData.Value("BT-1101-01", UlmData.Dicke, "0.3"),
            UlmData.Value("ET-2101-01", UlmData.Guete, "B500A"),
        },
        new[] { Beton },
        new[] { new LinkMaterialValue(Beton.MaterialId, UlmData.Festigkeit.ParameterId, festigkeit, null, null) });

    // Everything differs from the model, so the pane lists every value.
    private static ChangeReport Listed(LinkProjectData data) => ChangeFinder.Find(
        data,
        new[] { new ModelElement(1, "BT-1101-01", null), new ModelElement(2, "ET-2101-01", null) },
        new[] { new ModelMaterial(70, Beton.Name) },
        (id, parameter, value) => ValueCheck.Differs(""));

    [Fact]
    public void ApplyAll_WritesExactlyWhatIsListed()
    {
        LinkProjectData data = Data();

        ApplyPlan plan = ApplyPlan.For(data, Listed(data).Groups);

        Assert.False(plan.IsEmpty);
        Assert.Equal(0, plan.Outdated);
        Assert.Equal(new[] { "BT-1101-01", "ET-2101-01" }, plan.Objects.Select(o => o.Key).OrderBy(key => key));
        PlannedValues bodenplatte = plan.Objects.Single(o => o.Key == "BT-1101-01");
        Assert.Equal(new[] { ("Dicke", "0.3"), ("Betonstahl Güte", "B500A") }, bodenplatte.Values.Select(v => (v.Parameter.RevitName, v.Value)));
        PlannedValues material = Assert.Single(plan.Materials);
        Assert.Equal(Beton.MaterialId, material.Key);
        Assert.Equal(Beton.Name, material.Name);
        Assert.Equal("C16/20", Assert.Single(material.Values).Value);
    }

    [Fact]
    public void OnlyTheListedElements_GetTheListedValues()
    {
        LinkProjectData data = Data();
        ChangeReport listed = ChangeFinder.Find(
            data,
            new[] { new ModelElement(1, "BT-1101-01", null), new ModelElement(2, "BT-1101-01", null) },
            new[] { new ModelMaterial(70, Beton.Name) },
            (id, parameter, value) => id == 2 && parameter == UlmData.Dicke ? ValueCheck.Same : ValueCheck.Differs(""));

        ApplyPlan plan = ApplyPlan.For(data, listed.Groups);

        Assert.True(plan.IsListed(false, "bt-1101-01", UlmData.Guete.Guid, 1));
        Assert.True(plan.IsListed(false, "BT-1101-01", UlmData.Guete.Guid, 2));
        Assert.True(plan.IsListed(false, "BT-1101-01", UlmData.Dicke.Guid, 1));
        Assert.False(plan.IsListed(false, "BT-1101-01", UlmData.Dicke.Guid, 2));
        Assert.False(plan.IsListed(false, "BT-1101-01", UlmData.Guete.Guid, 3));
        Assert.True(plan.IsListed(true, Beton.MaterialId, UlmData.Festigkeit.Guid, 70));
        Assert.False(plan.IsListed(false, Beton.MaterialId, UlmData.Festigkeit.Guid, 70));
    }

    [Fact]
    public void WhatMustNeverBeWritten_IsNeverPlanned_EvenIfItWereListed()
    {
        // The dictionary changed after the list was made: the same parameters now identify the element, belong to
        // materials, or have no GUID.
        LinkProjectData data = Data();
        ChangeReport listed = Listed(data);
        LinkParameter Changed(LinkParameter p, string name, Guid guid, LinkTarget target) =>
            new(p.ParameterId, guid, name, p.SetCode, p.GroupCode, name, p.DataType, p.Unit, target, p.Categories);
        LinkProjectData With(LinkParameter guete, LinkParameter dicke) => new(
            data.ProjectId, 2, new[] { guete, dicke }, data.ObjectValues, data.Materials, data.MaterialValues);

        ApplyPlan plan = ApplyPlan.For(With(
            Changed(UlmData.Guete, "Bauteilnummer", UlmData.Guete.Guid, LinkTarget.Object),
            Changed(UlmData.Dicke, "Dicke", Guid.Empty, LinkTarget.Object)), listed.Groups);
        ApplyPlan ofMaterials = ApplyPlan.For(With(
            Changed(UlmData.Guete, "Betonstahl Güte", UlmData.Guete.Guid, LinkTarget.Material), UlmData.Dicke), listed.Groups);

        Assert.True(plan.IsEmpty);
        Assert.Equal("Dicke", Assert.Single(Assert.Single(ofMaterials.Objects).Values).Parameter.RevitName);
    }

    [Fact]
    public void ApplyingOneCard_LeavesTheOthersAlone()
    {
        LinkProjectData data = Data();

        ApplyPlan plan = ApplyPlan.For(data, Listed(data).Groups.Where(group => group.Key == "ET-2101-01"));

        Assert.Equal("ET-2101-01", Assert.Single(plan.Objects).Key);
        Assert.Empty(plan.Materials);
    }

    [Fact]
    public void AValueTheDatabaseHasChangedSince_IsLeftOut_AsNobodyHasSeenTheNewOne()
    {
        ChangeReport listed = Listed(Data());

        ApplyPlan plan = ApplyPlan.For(Data(guete: "B500C", festigkeit: "C30/37"), listed.Groups);

        Assert.Equal(2, plan.Outdated);
        Assert.Equal("Dicke", Assert.Single(plan.Objects.Single(o => o.Key == "BT-1101-01").Values).Parameter.RevitName);
        Assert.Empty(plan.Materials);
    }

    [Fact]
    public void AValueThatsGone_IsLeftOut_AndNothingLeftIsAnEmptyPlan()
    {
        ChangeReport listed = Listed(Data());

        ApplyPlan plan = ApplyPlan.For(UlmData.With(2, Array.Empty<LinkObjectValue>()), listed.Groups);

        Assert.True(plan.IsEmpty);
        Assert.Equal(4, plan.Outdated);
    }

    [Fact]
    public void AnObjectAndAMaterialWithTheSameId_DontGetEachOthersValues()
    {
        var odd = new LinkMaterial("BT-1101-01", "Beton", "BET", "Beton", null, null);
        LinkProjectData data = UlmData.With(1,
            new[] { UlmData.Value("BT-1101-01", UlmData.Guete, "B500A") },
            new[] { odd },
            new[] { new LinkMaterialValue("BT-1101-01", UlmData.Festigkeit.ParameterId, "C16/20", null, null) });
        ChangeReport listed = ChangeFinder.Find(data, new[] { new ModelElement(1, "BT-1101-01", null) },
            new[] { new ModelMaterial(70, "Beton") }, (id, parameter, value) => ValueCheck.Differs(""));

        ApplyPlan plan = ApplyPlan.For(data, listed.Groups);

        Assert.Equal("B500A", Assert.Single(Assert.Single(plan.Objects).Values).Value);
        Assert.Equal("C16/20", Assert.Single(Assert.Single(plan.Materials).Values).Value);
    }
}
