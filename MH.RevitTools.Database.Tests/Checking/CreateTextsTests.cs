using System;
using System.Linq;
using MH.RevitTools.Database.Changes;
using MH.RevitTools.Database.Checking;
using MH.RevitTools.Database.Link;

namespace MH.RevitTools.Database.Tests.Checking;

/// <summary>What the pane says about creating the project's missing materials.</summary>
public sealed class CreateTextsTests
{
    private static readonly TimeZoneInfo PlusOne = TimeZoneInfo.CreateCustomTimeZone("Test+1", TimeSpan.FromHours(1), "Test+1", "Test+1");
    private static readonly DateTime Now = new(2026, 11, 24, 13, 33, 0, DateTimeKind.Utc);

    private static string Detail(ApplyResult result) => ApplyTexts.AppliedDetail(result, Now, Now, PlusOne);

    private static ApplyResult Created(int count, int outdated = 0, params (string Name, string Problem)[] refused)
    {
        var tally = new ApplyTally();
        for (int i = 0; i < count; i++)
            tally.Created(100 + i);
        foreach ((string name, string problem) in refused)
            tally.Refused(name, problem);
        return tally.CreationResult(outdated);
    }

    [Theory]
    [InlineData(38, "Not in this model · 38 materials")]
    [InlineData(1, "Not in this model · 1 material")]
    public void TheSection_SaysHowManyAreMissing(int count, string title) => Assert.Equal(title, ApplyTexts.MissingTitle(count));

    [Fact]
    public void TheUndoStep_IsToldApartByItsPrefix()
    {
        Assert.Equal("MH database: 38 materials", ApplyTexts.CreateUndoName(38));
        Assert.StartsWith(ApplyTexts.UndoPrefix, ApplyTexts.CreateUndoName(1));
    }

    [Fact]
    public void TheQuestion_SaysWhatIsMade_AndHowToUndoIt()
    {
        Assert.Equal("Create 38 materials in this model?", ApplyTexts.CreateQuestion(38));
        Assert.Equal("They're in 1761-01 Ulm, but not in this model. Each gets its material class, a look like one of its kind that's "
            + "in the model, its fill patterns where the model has them, and its values. One undo step (Ctrl+Z).",
            ApplyTexts.CreateText(38, "1761-01 Ulm"));

        Assert.Equal("Create 1 material in this model?", ApplyTexts.CreateQuestion(1));
        Assert.StartsWith("It's in 1761-01 Ulm, but not in this model. It gets its material class,", ApplyTexts.CreateText(1, "1761-01 Ulm"));
    }

    [Fact]
    public void WhatWasCreated_IsCounted()
    {
        ApplyResult result = Created(38);

        Assert.Equal(38, result.Created);
        Assert.True(result.DidSomething);
        Assert.False(result.IsEmpty);
        Assert.Equal("38 materials created", ApplyTexts.AppliedTitle(result));
        Assert.Equal("Today 14:33, one undo step in Revit (Ctrl+Z).", Detail(result));
        Assert.Equal("1 material created", ApplyTexts.AppliedTitle(Created(1)));
    }

    [Fact]
    public void WhatWasNotCreated_IsSaid_WithWhy()
    {
        ApplyResult result = Created(2, 3, ("98-Beton: x", "The name has a character Revit doesn't allow."));

        Assert.Equal(
            "Today 14:33, one undo step in Revit (Ctrl+Z). "
            + "3 materials had changed in the database by then: check the list and create again. "
            + "Revit refused some: 98-Beton: x: The name has a character Revit doesn't allow.",
            Detail(result));
    }

    [Fact]
    public void WhenNothingWasCreated_ItSaysSo()
    {
        ApplyResult result = Created(0, 1);

        Assert.False(result.DidSomething);
        Assert.False(result.IsEmpty);
        Assert.Equal("No material was created", ApplyTexts.AppliedTitle(result));
        Assert.Equal("1 material had changed in the database by then: check the list and create again.", Detail(result));
    }

    [Fact]
    public void WhenItStopped_ItSaysWhy()
    {
        ApplyResult result = ApplyResult.StopCreating("The model is open read-only, so nothing can be created.");

        Assert.Equal("No material was created", ApplyTexts.AppliedTitle(result));
        Assert.Equal("The model is open read-only, so nothing can be created.", Detail(result));
        Assert.False(result.IsEmpty);
    }

    [Fact]
    public void CreateWaits_LikeApply_AndSaysWhy()
    {
        Assert.Null(ApplyTexts.CreateWaits(CheckState.Answered, false));
        Assert.Equal("The model is open read-only, so nothing can be created.", ApplyTexts.CreateWaits(CheckState.Answered, true));
        Assert.Equal("Create waits until you sign in, so that nothing outdated goes in.", ApplyTexts.CreateWaits(CheckState.NotSignedIn, false));
        Assert.Equal("Create waits until the database answers again, so that nothing outdated goes in.", ApplyTexts.CreateWaits(CheckState.NotReachable, false));
    }

    [Fact]
    public void TheRibbonsApply_WithNothingToApply_PointsToTheMissingMaterials()
    {
        var beton = new LinkMaterial("MB-1761-01-BET-0011", "11-Beton, C12/15", "BET", "Beton", null, null);
        ChangeReport report = ChangeFinder.Find(UlmData.With(1, Array.Empty<LinkObjectValue>(), new[] { beton }),
            Array.Empty<ModelElement>(), Array.Empty<ModelMaterial>(), (id, parameter, value) => ValueCheck.Same);

        Assert.Equal("What the model has matches the database. The MH Database pane lists the materials it doesn't have.",
            ApplyTexts.WhyNothingToApply(true, CheckState.Answered, report));
    }

    // As on a double-click: the first click has made them all.
    [Fact]
    public void NothingToDo_IsEmpty() => Assert.True(Created(0).IsEmpty);

    [Fact]
    public void AnApply_IsNoCreation()
    {
        var tally = new ApplyTally();
        tally.Written("BT-1101-01", false, UlmData.Guete.Guid, 1);

        Assert.Null(tally.Result(0).Created);
        Assert.True(tally.Result(0).DidSomething);
        Assert.Null(ApplyResult.Stop("x").Created);
    }

    [Fact]
    public void AMaterialThatCantBeCreated_SaysWhatItLacks()
    {
        var noName = new LinkMaterial("MB-1761-01-BET-0098", "", "BET", "Beton", null, null);
        var noGroup = new LinkMaterial("MB-1761-01-BET-0099", "99-Beton", "BET", "", null, null);
        ChangeReport report = ChangeFinder.Find(UlmData.With(1, Array.Empty<LinkObjectValue>(), new[] { noName, noGroup }),
            Array.Empty<ModelElement>(), Array.Empty<ModelMaterial>(), (id, parameter, value) => ValueCheck.Same);

        Assert.Equal(new[] { "99-Beton has no Materialgruppe", "MB-1761-01-BET-0098 has no Materialname" },
            report.NotApplied.Select(ApplyTexts.Title).OrderBy(title => title, StringComparer.Ordinal));
        Assert.All(report.NotApplied, item => Assert.Equal("So it can't be created in Revit. Give it one in Studio.", ApplyTexts.Detail(item)));
    }

    [Fact]
    public void UpToDate_WithMissingMaterials_SpeaksOfWhatTheModelHas()
    {
        var beton = new LinkMaterial("MB-1761-01-BET-0011", "11-Beton, C12/15", "BET", "Beton", null, null);
        ChangeReport report = ChangeFinder.Find(UlmData.With(1, Array.Empty<LinkObjectValue>(), new[] { beton }),
            Array.Empty<ModelElement>(), Array.Empty<ModelMaterial>(), (id, parameter, value) => ValueCheck.Same);

        Assert.Equal("What the model has matches the database.", PaneTexts.UpToDate(report, Now, PlusOne));
    }
}
