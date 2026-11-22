using System;
using System.Linq;
using MH.RevitTools.Database.Link;
using MH.RevitTools.Database.Parameters;

namespace MH.RevitTools.Database.Tests.Parameters;

public sealed class ReplaceUsesTests
{
    private const string GenericModels = "OST_GenericModel";
    private const string Materials = "OST_Materials";

    private static readonly LinkParameter Dicke = Parameter(61, "Dicke");
    private static readonly LinkParameter Breite = Parameter(62, "Breite");
    private static readonly LinkParameter Hoehe = Parameter(63, "Hoehe");

    private static readonly MasterParameterFile File = MasterFileText.Read(MasterFileText.With(
        (Dicke.Guid, "Dicke", "LENGTH"), (Breite.Guid, "Breite", "LENGTH"), (Hoehe.Guid, "Hoehe", "LENGTH")));

    // The model has all three with other GUIDs: Dicke is its element 501, Breite 502, Hoehe 503.
    private static readonly ParameterCheck Check = ParameterCheck.Run(
        new[] { Dicke, Breite, Hoehe }, File, new[] { Other("Dicke", 501), Other("Breite", 502), Other("Hoehe", 503) });

    [Fact]
    public void SplitsTheSwaps_ByWhetherSchedulesOrViewFiltersUseTheModelsParameter()
    {
        ReplaceUses uses = ReplaceUses.Found(Check.ToReplace, new[]
        {
            new ParameterUse(501, ParameterUseKind.Schedule, "Wandliste"),
            new ParameterUse(501, ParameterUseKind.ViewFilter, "Dicke > 30"),
            new ParameterUse(503, ParameterUseKind.Schedule, "Mengen"),
        });

        Assert.Equal(new[] { "Dicke", "Hoehe" }, uses.Used.Select(s => s.Old.Name));
        Assert.Equal(new[] { "Breite" }, uses.Others.Select(s => s.Old.Name));
        Assert.Null(uses.Problem);
        Assert.Equal(new[] { "Wandliste", "Dicke > 30" }, uses.UsesOf(uses.Used[0].Old).Select(u => u.Name));
        Assert.True(uses.AnyUse(ParameterUseKind.ViewFilter));
    }

    [Fact]
    public void Choose_ReplacesAll_OnlyTheOthers_OrNothing()
    {
        ReplaceUses uses = ReplaceUses.Found(Check.ToReplace, new[] { new ParameterUse(502, ParameterUseKind.Schedule, "Wandliste") });

        Assert.Equal(new[] { "Dicke", "Breite", "Hoehe" }, uses.Choose(ReplaceChoice.ReplaceAll).Select(s => s.Old.Name));
        Assert.Equal(new[] { "Dicke", "Hoehe" }, uses.Choose(ReplaceChoice.ReplaceOthers).Select(s => s.Old.Name));
        Assert.Empty(uses.Choose(ReplaceChoice.Cancel));
    }

    [Fact]
    public void WithNothingInUse_EverySwapIsAmongTheOthers()
    {
        ReplaceUses uses = ReplaceUses.Found(Check.ToReplace, Array.Empty<ParameterUse>());

        Assert.Empty(uses.Used);
        Assert.Equal(3, uses.Others.Count);
        Assert.False(uses.AnyUse(ParameterUseKind.Schedule));
    }

    [Fact]
    public void WhenRevitCantTell_EverySwapCountsAsUsed()
    {
        ReplaceUses uses = ReplaceUses.Unknown(Check.ToReplace, " The schedule is busy ");

        Assert.Equal(3, uses.Used.Count);
        Assert.Empty(uses.Others);
        Assert.Equal("The schedule is busy", uses.Problem);
        Assert.Empty(uses.Choose(ReplaceChoice.ReplaceOthers));
    }

    [Fact]
    public void AParameterWithoutAnId_IsNeverMatchedToAUse()
    {
        ParameterCheck check = ParameterCheck.Run(new[] { Dicke }, File, new[] { Other("Dicke", 0) });

        ReplaceUses uses = ReplaceUses.Found(check.ToReplace, new[] { new ParameterUse(0, ParameterUseKind.Schedule, "Wandliste") });

        Assert.Empty(uses.Used);
    }

    [Fact]
    public void OneModelParameterInTheWayOfTwo_CountsOnce()
    {
        var onObjects = new LinkParameter(1, Guid.NewGuid(), "Kategorie", "BAU", "KAT", "Kategorie", "String", null, LinkTarget.Object, new[] { GenericModels });
        var onMaterials = new LinkParameter(2, Guid.NewGuid(), "Kategorie", "MAT", "KAT", "Kategorie", "String", null, LinkTarget.Material, new[] { Materials });
        MasterParameterFile file = MasterFileText.Read(MasterFileText.With((onObjects.Guid, "Kategorie", "TEXT"), (onMaterials.Guid, "Kategorie", "TEXT")));
        var old = new ModelBinding("Kategorie", Guid.NewGuid(), true, new[] { GenericModels, Materials }, 99);
        ParameterCheck check = ParameterCheck.Run(new[] { onObjects, onMaterials }, file, new[] { old });

        ReplaceUses uses = ReplaceUses.Found(check.ToReplace, new[] { new ParameterUse(99, ParameterUseKind.Schedule, "Kategorien") });

        Assert.Equal(2, uses.Used.Count);
        Assert.Same(old, Assert.Single(uses.UsedParameters));
        Assert.Empty(uses.OtherParameters);
    }

    [Fact]
    public void ASharedOldParameterInUse_TakesBothItsSwaps_AndLeavesTheOthersApart()
    {
        var onObjects = new LinkParameter(1, Guid.NewGuid(), "Kategorie", "BAU", "KAT", "Kategorie", "String", null, LinkTarget.Object, new[] { GenericModels });
        var onMaterials = new LinkParameter(2, Guid.NewGuid(), "Kategorie", "MAT", "KAT", "Kategorie", "String", null, LinkTarget.Material, new[] { Materials });
        MasterParameterFile file = MasterFileText.Read(MasterFileText.With(
            (onObjects.Guid, "Kategorie", "TEXT"), (onMaterials.Guid, "Kategorie", "TEXT"), (Dicke.Guid, "Dicke", "LENGTH")));
        var shared = new ModelBinding("Kategorie", Guid.NewGuid(), true, new[] { GenericModels, Materials }, 99);
        ParameterCheck check = ParameterCheck.Run(new[] { onObjects, onMaterials, Dicke }, file, new[] { shared, Other("Dicke", 501) });

        ReplaceUses uses = ReplaceUses.Found(check.ToReplace, new[] { new ParameterUse(99, ParameterUseKind.ViewFilter, "Kategorie = A") });

        Assert.Equal(new[] { onObjects, onMaterials }, uses.Used.Select(s => s.Add.Parameter));
        Assert.Equal(new[] { "Dicke" }, uses.Others.Select(s => s.Old.Name));
        Assert.Equal(new[] { "Kategorie" }, uses.UsedParameters.Select(p => p.Name));
        // Three swaps, but the answers count the model's parameters: Kategorie once, and Dicke.
        Assert.Equal(new[] { "Replace all 2", "Replace only the other one" }, ParameterCheckTexts.ReplaceUsedAnswers(uses).Select(a => a.Label));
    }

    [Fact]
    public void AUseWithoutAName_IsStillNamed()
    {
        Assert.Equal("(no name)", new ParameterUse(501, ParameterUseKind.ViewFilter, "  ").Name);
    }

    private static LinkParameter Parameter(int id, string name) =>
        new(id, Guid.NewGuid(), name, "BAU", "GEO", name, "Double", "m", LinkTarget.Object, new[] { GenericModels });

    private static ModelBinding Other(string name, long id) => new(name, Guid.NewGuid(), true, new[] { GenericModels }, id);
}
