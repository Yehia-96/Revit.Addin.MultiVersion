using System;
using System.Linq;
using MH.RevitTools.Database.Link;
using MH.RevitTools.Database.Parameters;

namespace MH.RevitTools.Database.Tests.Parameters;

public sealed class ReplaceDeletionsTests
{
    private const string GenericModels = "OST_GenericModel";

    private static readonly LinkParameter Dicke = Parameter(61, "Dicke");
    private static readonly LinkParameter Breite = Parameter(62, "Breite");
    private static readonly LinkParameter Hoehe = Parameter(63, "Hoehe");

    private static readonly MasterParameterFile File = MasterFileText.Read(MasterFileText.With(
        (Dicke.Guid, "Dicke", "LENGTH"), (Breite.Guid, "Breite", "LENGTH"), (Hoehe.Guid, "Hoehe", "LENGTH")));

    // The model has all three with other GUIDs: Dicke is its element 501, Breite 502, Hoehe 503.
    private static readonly ParameterCheck Check = ParameterCheck.Run(
        new[] { Dicke, Breite, Hoehe }, File, new[] { Other("Dicke", 501), Other("Breite", 502), Other("Hoehe", 503) });

    [Fact]
    public void SplitsTheSwaps_ByWhetherRevitDeletesOtherElementsWithTheParameter()
    {
        ReplaceDeletions deletions = ReplaceDeletions.Found(Check.ToReplace, new[]
        {
            new DeletedElement(501, 9001, "Dicke > 30", "View filter"),
            new DeletedElement(501, 9002, "Wandliste", "Schedule"),
            new DeletedElement(503, 9003, "", null),
        });

        Assert.Equal(new[] { "Dicke", "Hoehe" }, deletions.Deleting.Select(s => s.Old.Name));
        Assert.Equal(new[] { "Breite" }, deletions.Others.Select(s => s.Old.Name));
        Assert.Equal(new long[] { 9001, 9002, 9003 }, deletions.Elements.Select(e => e.Id));
        Assert.Equal(new[] { "Dicke", "Dicke", "Hoehe" }, deletions.Elements.Select(deletions.ParametersOf));
        Assert.Equal("(no name)", deletions.Elements[2].Name);
        Assert.Equal("Element", deletions.Elements[2].Kind);
    }

    [Fact]
    public void AnElementTwoParametersTake_IsListedOnce_WithBoth_AndNeitherGoesOnItsOwn()
    {
        ReplaceDeletions deletions = ReplaceDeletions.Found(Check.ToReplace, new[]
        {
            new DeletedElement(501, 9001, "Dicke > 30", "View filter"),
            new DeletedElement(502, 9001, "Dicke > 30", "View filter"),
        });

        DeletedElement element = Assert.Single(deletions.Elements);
        Assert.Equal("Dicke, Breite", deletions.ParametersOf(element));
        Assert.Equal(new[] { "Dicke", "Breite" }, deletions.DeletingParameters.Select(p => p.Name));
        Assert.Equal(new[] { "Hoehe" }, deletions.Others.Select(s => s.Old.Name));
        Assert.Equal("Replacing 2 parameters also deletes 1 element", ParameterCheckTexts.DeletionTitle(deletions));
        Assert.Equal("Replace only the other one", ParameterCheckTexts.DeletionOthersButton(deletions));
    }

    [Fact]
    public void NeverListsAParameterBeingReplaced()
    {
        ReplaceDeletions deletions = ReplaceDeletions.Found(Check.ToReplace, new[]
        {
            new DeletedElement(501, 9001, "Dicke > 30", "View filter"),
            new DeletedElement(501, 503, "Hoehe", "Parameter"),
        });

        Assert.Equal(new long[] { 9001 }, deletions.Elements.Select(e => e.Id));
        Assert.DoesNotContain(503L, deletions.AllowedFor(ReplaceChoice.ReplaceAll));
    }

    [Fact]
    public void GroupsTheElements_ByTheirParametersPlace_HoweverTheyWereFound()
    {
        ReplaceDeletions deletions = ReplaceDeletions.Found(Check.ToReplace, new[]
        {
            new DeletedElement(503, 9003, "C", "View filter"),
            new DeletedElement(501, 9001, "A", "View filter"),
            new DeletedElement(503, 9004, "D", "View filter"),
            new DeletedElement(501, 9002, "B", "View filter"),
        });

        Assert.Equal(new[] { "A", "B", "C", "D" }, deletions.Elements.Select(e => e.Name));
    }

    [Fact]
    public void ReplaceAll_MayDeleteTheListedElements_TheOthersNone()
    {
        ReplaceDeletions deletions = ReplaceDeletions.Found(Check.ToReplace, new[] { new DeletedElement(502, 9001, "Breit", "View filter") });

        Assert.Equal(3, deletions.Choose(ReplaceChoice.ReplaceAll).Count);
        Assert.Equal(new long[] { 9001 }, deletions.AllowedFor(ReplaceChoice.ReplaceAll));
        Assert.Equal(new[] { "Dicke", "Hoehe" }, deletions.Choose(ReplaceChoice.ReplaceOthers).Select(s => s.Old.Name));
        Assert.Empty(deletions.AllowedFor(ReplaceChoice.ReplaceOthers));
        Assert.Empty(deletions.Choose(ReplaceChoice.Cancel));
        Assert.Empty(deletions.AllowedFor(ReplaceChoice.Cancel));
    }

    [Fact]
    public void WhenRevitDeletesNothingElse_EverySwapGoesOnItsOwn()
    {
        ReplaceDeletions deletions = ReplaceDeletions.Found(Check.ToReplace, Array.Empty<DeletedElement>());

        Assert.Empty(deletions.Deleting);
        Assert.Equal(3, deletions.Others.Count);
        Assert.Empty(deletions.Elements);
    }

    [Fact]
    public void Texts_NameTheParameter_CountTheElements_AndOfferTheOthers()
    {
        ReplaceDeletions one = ReplaceDeletions.Found(Check.ToReplace, new[]
        {
            new DeletedElement(501, 9001, "Dicke > 30", "View filter"),
            new DeletedElement(501, 9002, "Wandliste", "Schedule"),
        });

        Assert.Equal("Replacing “Dicke” also deletes 2 elements", ParameterCheckTexts.DeletionTitle(one));
        Assert.Equal("Delete 2 elements and replace", ParameterCheckTexts.DeleteAndReplaceButton(one));
        Assert.Equal("Replace only the other 2", ParameterCheckTexts.DeletionOthersButton(one));
        Assert.StartsWith("Revit can't delete this parameter on its own: it deletes the elements below with it.", ParameterCheckTexts.DeletionIntro(one));
        Assert.EndsWith("Undo puts everything back. The other 2 parameters go on their own.", ParameterCheckTexts.DeletionIntro(one));
        Assert.Equal(new[] { "Dicke: left as it is, as Revit would delete other elements with it." }, ParameterCheckTexts.LeftForDeletions(one));
    }

    [Fact]
    public void Texts_ForSeveralParameters_AndNoOthers()
    {
        ReplaceDeletions all = ReplaceDeletions.Found(Check.ToReplace, new[]
        {
            new DeletedElement(501, 9001, "A", "View filter"),
            new DeletedElement(502, 9002, "B", "View filter"),
            new DeletedElement(503, 9003, "C", "View filter"),
        });
        ReplaceDeletions single = ReplaceDeletions.Found(Check.ToReplace, new[] { new DeletedElement(502, 9002, "B", "View filter") });

        Assert.Equal("Replacing 3 parameters also deletes 3 elements", ParameterCheckTexts.DeletionTitle(all));
        Assert.Equal("", ParameterCheckTexts.DeletionOthersButton(all));
        Assert.EndsWith("Check them before you go on; Undo puts everything back.", ParameterCheckTexts.DeletionIntro(all));
        Assert.Equal("Delete 1 element and replace", ParameterCheckTexts.DeleteAndReplaceButton(single));
    }

    private static LinkParameter Parameter(int id, string name) =>
        new(id, Guid.NewGuid(), name, "BAU", "GEO", name, "Double", "m", LinkTarget.Object, new[] { GenericModels });

    private static ModelBinding Other(string name, long id) => new(name, Guid.NewGuid(), true, new[] { GenericModels }, id);
}
