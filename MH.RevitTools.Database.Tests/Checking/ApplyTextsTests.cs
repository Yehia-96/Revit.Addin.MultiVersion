using System;
using System.Collections.Generic;
using System.Linq;
using MH.RevitTools.Database.Changes;
using MH.RevitTools.Database.Checking;
using MH.RevitTools.Database.Link;

namespace MH.RevitTools.Database.Tests.Checking;

public sealed class ApplyTextsTests
{
    private static readonly TimeZoneInfo PlusOne = TimeZoneInfo.CreateCustomTimeZone("Test+1", TimeSpan.FromHours(1), "Test+1", "Test+1");
    private static readonly DateTime Now = new(2026, 11, 23, 13, 33, 0, DateTimeKind.Utc);

    private static string Detail(ApplyResult result) => ApplyTexts.AppliedDetail(result, Now, Now, PlusOne);

    private static ChangeReport Report(LinkProjectData data, CompareValue compare, int elements = 1) => ChangeFinder.Find(
        data, Enumerable.Range(1, elements).Select(id => new ModelElement(id, "BT-1101-01", null)), Array.Empty<ModelMaterial>(), compare);

    private static NotAppliedItem Item(ValueCheck check, int elements = 1, params LinkParameter[] parameters) => Assert.Single(Report(
        UlmData.With(1, parameters.Select(parameter => UlmData.Value("BT-1101-01", parameter, "0.3"))), (id, parameter, value) => check, elements).NotApplied);

    [Fact]
    public void WhatWentIn_IsCounted_AsThePaneCountsChanges()
    {
        var tally = new ApplyTally();
        tally.Written("BT-1101-01", false, UlmData.Guete.Guid, 1);
        tally.Written("bt-1101-01", false, UlmData.Guete.Guid, 2);
        tally.Written("BT-1101-01", false, UlmData.Dicke.Guid, 1);
        tally.Written("MB-1761-01-BET-0011", true, UlmData.Festigkeit.Guid, 70);

        ApplyResult result = tally.Result(0);

        Assert.Equal((3, 2, 1), (result.Changes, result.Elements, result.Materials));
        Assert.Equal("3 changes applied to 2 elements and 1 material", ApplyTexts.AppliedTitle(result));
        Assert.Equal("Today 14:33, one undo step in Revit (Ctrl+Z).", Detail(result));
    }

    [Fact]
    public void OneChangeToOneMaterial_ReadsAsOne()
    {
        var tally = new ApplyTally();
        tally.Written("MB-1761-01-BET-0011", true, UlmData.Festigkeit.Guid, 70);

        Assert.Equal("1 change applied to 1 material", ApplyTexts.AppliedTitle(tally.Result(0)));
    }

    [Fact]
    public void WhatWasLeftOut_IsSaid_WithWhatToDo()
    {
        var tally = new ApplyTally();
        tally.Written("BT-1101-01", false, UlmData.Guete.Guid, 1);
        tally.Borrowed("M. Keller", 2);
        tally.Borrowed("m. keller", 3);
        tally.Borrowed(null, 4);
        tally.OutOfDate(5);
        tally.Refused("Dicke", "The parameter is read-only.");
        tally.Refused("Dicke", "The parameter is read-only.");

        Assert.Equal(
            "Today 14:33, one undo step in Revit (Ctrl+Z). "
            + "Not applied to 1 element that someone else has borrowed: apply again once it's released. "
            + "Not applied to 2 elements that M. Keller has borrowed: apply again once they're released. "
            + "Not applied to 1 element with newer changes in the central model: Reload Latest, then apply again. "
            + "2 values had changed in the database by then: check the list and apply again. "
            + "Revit refused some values: Dicke: The parameter is read-only.",
            Detail(tally.Result(2)));
    }

    [Fact]
    public void WhenEveryElementIsBorrowed_NothingWasApplied_AndItSaysWhy()
    {
        var tally = new ApplyTally();
        tally.Borrowed("M. Keller", 2);
        ApplyResult result = tally.Result(0);

        Assert.Equal("Nothing was applied", ApplyTexts.AppliedTitle(result));
        Assert.Equal("Not applied to 1 element that M. Keller has borrowed: apply again once it's released.", Detail(result));
    }

    [Fact]
    public void AnApplyWithNothingLeftToDo_IsEmpty_SoADoubleClickKeepsTheFirstResult()
    {
        var borrowed = new ApplyTally();
        borrowed.Borrowed(null, 1);

        Assert.True(new ApplyTally().Result(0).IsEmpty);
        Assert.False(new ApplyTally().Result(1).IsEmpty);
        Assert.False(borrowed.Result(0).IsEmpty);
        Assert.False(ApplyResult.Stop("x").IsEmpty);
    }

    [Fact]
    public void AStoppedApply_GivesTheReason()
    {
        Assert.Equal("Nothing was applied", ApplyTexts.AppliedTitle(ApplyResult.Stop("x")));
        Assert.Equal("The model is open read-only.", Detail(ApplyResult.Stop("The model is open read-only")));
        Assert.Equal("Revit gave no reason.", Detail(ApplyResult.Stop(" ")));
    }

    [Fact]
    public void BorrowedElements_AreNamedWithWhoHasThem()
    {
        NotAppliedItem two = Item(ValueCheck.Borrowed("M. Keller"), 2, UlmData.Dicke);
        NotAppliedItem one = Item(ValueCheck.Borrowed(null), 1, UlmData.Dicke, UlmData.Guete);

        Assert.Equal("2 elements are borrowed by M. Keller", ApplyTexts.Title(two));
        Assert.Equal("They get the new values when you apply again after M. Keller releases them.", ApplyTexts.Detail(two));
        Assert.Equal("1 element is borrowed by someone else", ApplyTexts.Title(one));
        Assert.Equal("It gets the new values when you apply again after someone else releases it.", ApplyTexts.Detail(one));
    }

    [Fact]
    public void OtherReasons_NameTheirParametersAndElements()
    {
        Assert.Equal("Dicke is read-only in 1 element", ApplyTexts.Title(Item(ValueCheck.ReadOnly, 1, UlmData.Dicke)));
        Assert.Equal("Dicke and Betonstahl Güte are read-only in 3 elements", ApplyTexts.Title(Item(ValueCheck.ReadOnly, 3, UlmData.Guete, UlmData.Dicke)));
        Assert.Equal("Dicke sits on the type of 2 elements", ApplyTexts.Title(Item(ValueCheck.OnType, 2, UlmData.Dicke)));
        Assert.Equal("2 elements have newer changes in the central model", ApplyTexts.Title(Item(ValueCheck.OutOfDate, 2, UlmData.Dicke)));
        Assert.Equal("Dicke: \"dick\" isn't a number", ApplyTexts.Title(Item(ValueCheck.CantCompare("\"dick\" isn't a number"), 1, UlmData.Dicke)));
    }

    [Fact]
    public void MissingParameters_PointToCheckParameters()
    {
        NotAppliedItem one = Item(ValueCheck.NoParameter, 2, UlmData.Dicke);
        NotAppliedItem two = Item(ValueCheck.NoParameter, 2, UlmData.Dicke, UlmData.Guete);

        Assert.Equal("Dicke is not in this model", ApplyTexts.Title(one));
        Assert.Equal("Check parameters adds it from the master file.", ApplyTexts.Detail(one));
        Assert.Equal("Dicke and Betonstahl Güte are not in this model", ApplyTexts.Title(two));
        Assert.Equal("Check parameters adds them from the master file.", ApplyTexts.Detail(two));
    }

    [Theory]
    [InlineData("[Modeller]", "1 value is [Modeller]", "Dicke is entered in the model and never overwritten.")]
    [InlineData("[ET]", "1 value is [ET]", "Dicke doesn't apply to these objects.")]
    [InlineData("-", "1 value is -", "Dicke is left empty on purpose.")]
    [InlineData("TBT", "1 value is TBT", "Dicke is still to be determined.")]
    public void Placeholders_SayWhatTheyMean(string placeholder, string title, string detail)
    {
        NotAppliedItem item = Assert.Single(Report(UlmData.With(1, new[] { UlmData.Value("BT-1101-01", UlmData.Dicke, placeholder) }),
            (id, parameter, value) => ValueCheck.Same).NotApplied);

        Assert.Equal(title, ApplyTexts.Title(item));
        Assert.Equal(detail, ApplyTexts.Detail(item));
    }

    [Fact]
    public void ManyParameters_AreCounted()
    {
        LinkParameter[] many = Enumerable.Range(1, 6).Select(i => new LinkParameter(900 + i, Guid.NewGuid(), "P" + i, "S", "G", "P" + i, "String",
            null, LinkTarget.Object, new[] { "OST_GenericModel" })).ToArray();
        var data = new LinkProjectData(13, 1, many, many.Select(p => new LinkObjectValue("BT-1101-01", p.ParameterId, "[Modeller]", null, null)),
            Array.Empty<LinkMaterial>(), Array.Empty<LinkMaterialValue>());

        NotAppliedItem item = Assert.Single(Report(data, (id, parameter, value) => ValueCheck.Same).NotApplied);

        Assert.Equal("6 values are [Modeller]", ApplyTexts.Title(item));
        Assert.Equal("6 parameters are entered in the model and never overwritten.", ApplyTexts.Detail(item));
    }

    [Fact]
    public void SharedIds_AndMaterialReferences_AreExplained()
    {
        NotAppliedItem conflict = Assert.Single(Report(UlmData.With(1, new[]
        {
            UlmData.Value("BT-1101-01", UlmData.Dicke, "0.3"), UlmData.Value("BT-1101-01", UlmData.Dicke, "0.4"),
        }), (id, parameter, value) => ValueCheck.Same).NotApplied);
        NotAppliedItem reference = Assert.Single(Report(UlmData.With(1, new[] { UlmData.Value("BT-1101-01", UlmData.MaterialId, "MB-1") }),
            (id, parameter, value) => ValueCheck.Same).NotApplied);

        Assert.Equal("BT-1101-01 has different values for Dicke", ApplyTexts.Title(conflict));
        Assert.Contains("another Bauteilunternummer in Studio", ApplyTexts.Detail(conflict));
        Assert.Equal("Material-ID says which material an object has", ApplyTexts.Title(reference));
        Assert.Equal("Revit has it on the material, not on the element. Assigning materials comes later.", ApplyTexts.Detail(reference));
    }

    [Fact]
    public void TheButtonsAndTheUndoStep_SayHowMuchTheyApply()
    {
        LinkProjectData data = UlmData.With(1, new[]
        {
            UlmData.Value("BT-1101-01", UlmData.Dicke, "0.3"), UlmData.Value("BT-1101-01", UlmData.Guete, "B500A"),
        });
        ChangeReport report = Report(data, (id, parameter, value) => ValueCheck.Differs(""), 3);

        Assert.Equal("Apply", ApplyTexts.ApplyLabel(1));
        Assert.Equal("Apply 3", ApplyTexts.ApplyLabel(3));
        Assert.Equal("MH database: BT-1101-01", ApplyTexts.UndoName(report.Groups));
        Assert.Equal("MH database: 4 changes", ApplyTexts.UndoName(new List<ChangeGroup> { report.Groups[0], report.Groups[0] }));
        Assert.StartsWith(ApplyTexts.UndoPrefix, ApplyTexts.UndoName(null));
        Assert.Equal("Apply 2 changes from the database?", ApplyTexts.ConfirmTitle(report));
        Assert.Equal("They go into 3 elements of this model, from 1761-01 Ulm, in one undo step (Ctrl+Z). "
            + "The MH Database pane lists each of them.", ApplyTexts.ConfirmText(report, "1761-01 Ulm"));
        Assert.Equal("Not applied · 3", ApplyTexts.NotAppliedTitle(3));
    }

    [Fact]
    public void TheRibbonButton_SaysWhyThereIsNothingToApply()
    {
        LinkProjectData data = UlmData.With(1, new[] { UlmData.Value("BT-1101-01", UlmData.Dicke, "0.3") });
        ChangeReport changes = Report(data, (id, parameter, value) => ValueCheck.Differs(""));
        ChangeReport same = Report(data, (id, parameter, value) => ValueCheck.Same);
        ChangeReport borrowed = Report(data, (id, parameter, value) => ValueCheck.Borrowed("M. Keller"));

        Assert.StartsWith("This model isn't linked", ApplyTexts.WhyNothingToApply(false, null, null));
        Assert.Contains("still being checked", ApplyTexts.WhyNothingToApply(true, null, null));
        Assert.Contains("connection file can't be used", ApplyTexts.WhyNothingToApply(true, CheckState.NotSetUp, null));
        Assert.Contains("Sign in first", ApplyTexts.WhyNothingToApply(true, CheckState.NotSignedIn, changes));
        Assert.Contains("isn't reachable", ApplyTexts.WhyNothingToApply(true, CheckState.NotReachable, changes));
        Assert.Contains("any more", ApplyTexts.WhyNothingToApply(true, CheckState.ProjectChanged, null));
        Assert.Contains("still being compared", ApplyTexts.WhyNothingToApply(true, CheckState.Answered, null));
        Assert.Equal("The model matches the database.", ApplyTexts.WhyNothingToApply(true, CheckState.Answered, same));
        Assert.Contains("lists them under Not applied", ApplyTexts.WhyNothingToApply(true, CheckState.Answered, borrowed));
        Assert.Null(ApplyTexts.WhyNothingToApply(true, CheckState.Answered, changes));
    }

    [Fact]
    public void ThePane_SaysWhenApplyHasToWait_AndWhenNothingCanGoIn()
    {
        LinkProjectData data = UlmData.With(1, new[] { UlmData.Value("BT-1101-01", UlmData.Dicke, "0.3") });

        Assert.Null(ApplyTexts.ApplyWaits(CheckState.Answered, false));
        Assert.Contains("until the database answers", ApplyTexts.ApplyWaits(CheckState.NotReachable, false));
        Assert.Contains("until you sign in", ApplyTexts.ApplyWaits(CheckState.NotSignedIn, false));
        Assert.Contains("read-only", ApplyTexts.ApplyWaits(CheckState.Answered, true));
        Assert.Equal("Up to date", ApplyTexts.UpToDateTitle(Report(data, (id, parameter, value) => ValueCheck.Same)));
        Assert.Equal("Nothing to apply now", ApplyTexts.UpToDateTitle(Report(data, (id, parameter, value) => ValueCheck.ReadOnly)));
    }
}
