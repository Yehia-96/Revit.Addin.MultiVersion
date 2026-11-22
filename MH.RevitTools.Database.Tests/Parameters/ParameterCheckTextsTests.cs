using System;
using System.Linq;
using MH.RevitTools.Database.Link;
using MH.RevitTools.Database.Linking;
using MH.RevitTools.Database.Parameters;

namespace MH.RevitTools.Database.Tests.Parameters;

public sealed class ParameterCheckTextsTests
{
    private static readonly LinkParameter Dicke = new(61, Guid.NewGuid(), "Dicke", "BAU", "GEO", "Dicke", "Double", "m", LinkTarget.Object, new[] { "OST_GenericModel" });
    private static readonly LinkParameter Breite = new(62, Guid.NewGuid(), "Breite", "BAU", "GEO", "Breite", "Double", "m", LinkTarget.Object, new[] { "OST_GenericModel" });

    [Fact]
    public void Subtitle_NamesTheMasterFile_AndWhenStudioWroteIt()
    {
        MasterParameterFile file = MasterFileText.Read(MasterFileText.With((Dicke.Guid, "Dicke", "LENGTH")));
        TimeZoneInfo plusOne = TimeZoneInfo.CreateCustomTimeZone("Test+1", TimeSpan.FromHours(1), "Test+1", "Test+1");

        var parts = ParameterCheckTexts.Subtitle(file, plusOne);

        Assert.Equal(
            "The link finds each parameter by its GUID from the master shared parameter file, MH_Parameter.txt (2026-11-19). "
            + "Revit shows the plain names; where two would look alike on one category, the set name is added.",
            string.Concat(parts.Select(p => p.Text)));
        Assert.Equal(TextStyle.Code, parts[1].Style);
    }

    [Fact]
    public void Subtitle_WithoutAFile_LeavesTheDateOut()
    {
        string text = string.Concat(ParameterCheckTexts.Subtitle(null, TimeZoneInfo.Utc).Select(p => p.Text));

        Assert.Contains("file, MH_Parameter.txt. Revit", text);
    }

    [Fact]
    public void TabsSummaryAndButton_CountWhatTheCheckFound()
    {
        MasterParameterFile file = MasterFileText.Read(MasterFileText.With((Dicke.Guid, "Dicke", "LENGTH"), (Breite.Guid, "Breite", "LENGTH")));
        ParameterCheck check = ParameterCheck.Run(new[] { Dicke, Breite }, file, new[] { new ModelBinding("Dicke", Dicke.Guid, true, new[] { "OST_GenericModel" }) });

        Assert.Equal("Other GUID · 0", ParameterCheckTexts.TabLabel(ParameterFilter.OtherGuid, check.Count(ParameterFilter.OtherGuid)));
        Assert.Equal("All · 2", ParameterCheckTexts.TabLabel(ParameterFilter.All, check.Count(ParameterFilter.All)));
        Assert.Equal("1 of 2 ready", ParameterCheckTexts.Summary(check));
        Assert.Equal("Add 1 missing parameter", ParameterCheckTexts.AddButton(check));
        Assert.Equal("Add 2 missing parameters", ParameterCheckTexts.AddButton(ParameterCheck.Run(new[] { Dicke, Breite }, file, Array.Empty<ModelBinding>())));
        Assert.Equal("Nothing to add", ParameterCheckTexts.AddButton(ParameterCheck.Run(
            new[] { Dicke }, file, new[] { new ModelBinding("Dicke", Dicke.Guid, true, new[] { "OST_GenericModel" }) })));
    }

    [Fact]
    public void Replace_AsksFirst_NamingWhatGoes()
    {
        MasterParameterFile file = MasterFileText.Read(MasterFileText.With((Dicke.Guid, "Dicke", "LENGTH"), (Breite.Guid, "Breite", "LENGTH")));
        ParameterCheck one = ParameterCheck.Run(new[] { Dicke, Breite }, file, new[] { Other("Dicke") });
        ParameterCheck two = ParameterCheck.Run(new[] { Dicke, Breite }, file, new[] { Other("Dicke"), Other("Breite") });

        Assert.Equal("Replace 1…", ParameterCheckTexts.ReplaceButton(one));
        Assert.Equal("Replace “Dicke”?", ParameterCheckTexts.ReplaceTitle(one));
        Assert.Equal("Replace 2 parameters?", ParameterCheckTexts.ReplaceTitle(two));
        string question = ParameterCheckTexts.ReplaceQuestion(two);
        Assert.Contains("with its values in every element", question);
        Assert.Contains("• Dicke\n• Breite", question);
        Assert.Contains("Undo puts everything back.", question);
        Assert.Contains("Tags show the new parameters once their tag families use the master file's.", question);
        Assert.DoesNotContain("schedule", question);
        Assert.Equal("", ParameterCheckTexts.ReplaceButton(ParameterCheck.Run(new[] { Dicke }, file, Array.Empty<ModelBinding>())));
    }

    [Fact]
    public void ReplaceUsed_NamesWhereEachIsUsed_AndWhatReplacingDoesThere()
    {
        ReplaceUses uses = Uses(
            new ParameterUse(1, ParameterUseKind.Schedule, "Wandliste"),
            new ParameterUse(1, ParameterUseKind.Schedule, "Mengen"),
            new ParameterUse(1, ParameterUseKind.ViewFilter, "Dicke > 30"),
            new ParameterUse(2, ParameterUseKind.Schedule, "Wandliste"));

        Assert.Equal("Replace 2 parameters that 2 schedules and a view filter use?", ParameterCheckTexts.ReplaceUsedTitle(uses));
        string question = ParameterCheckTexts.ReplaceUsedQuestion(uses);
        Assert.StartsWith("• Dicke: schedules “Wandliste” and “Mengen”; view filter “Dicke > 30”\n• Breite: schedule “Wandliste”\n\n", question);
        Assert.Contains("the schedules lose its column, and the view filters can no longer filter by it", question);
        Assert.Contains("Add the new parameter to them by hand afterwards, and check the views that use those filters.", question);
        Assert.EndsWith("Undo puts everything back.", question);
    }

    [Fact]
    public void ReplaceUsedTitle_NamesASingleParameter_AndAgreesWithOnePlace()
    {
        ReplaceUses uses = Uses(new ParameterUse(1, ParameterUseKind.Schedule, "Wandliste"));

        Assert.Equal("Replace “Dicke” although a schedule uses it?", ParameterCheckTexts.ReplaceUsedTitle(uses));
        Assert.Equal("Replace “Dicke” although 2 view filters use it?", ParameterCheckTexts.ReplaceUsedTitle(Uses(
            new ParameterUse(1, ParameterUseKind.ViewFilter, "A"), new ParameterUse(1, ParameterUseKind.ViewFilter, "B"))));
        Assert.Equal("Replace “Dicke” although a schedule and a view filter use it?", ParameterCheckTexts.ReplaceUsedTitle(Uses(
            new ParameterUse(1, ParameterUseKind.Schedule, "A"), new ParameterUse(1, ParameterUseKind.ViewFilter, "B"))));
    }

    [Fact]
    public void ReplaceUsedQuestion_NamesThreePlaces_ThenCountsTheRest()
    {
        ReplaceUses uses = Uses(Enumerable.Range(1, 5).Select(i => new ParameterUse(1, ParameterUseKind.Schedule, "S" + i)).ToArray());

        Assert.StartsWith("• Dicke: schedules “S1”, “S2”, “S3” and 2 more\n\n", ParameterCheckTexts.ReplaceUsedQuestion(uses));
    }

    [Fact]
    public void ReplaceUsedAnswers_OfferToReplaceOnlyTheOthers_WhenSomeAreNotInUse()
    {
        var answers = ParameterCheckTexts.ReplaceUsedAnswers(Uses(new ParameterUse(1, ParameterUseKind.Schedule, "Wandliste")));

        Assert.Equal(new[] { ReplaceChoice.ReplaceAll, ReplaceChoice.ReplaceOthers }, answers.Select(a => a.Choice));
        Assert.Equal(new[] { "Replace all 2", "Replace only the other one" }, answers.Select(a => a.Label));
        Assert.Equal("Also the one in use: its schedules and view filters lose it until you add the new one.", answers[0].Note);
        Assert.Equal("Leave the one in use as it is; the link leaves it empty.", answers[1].Note);
    }

    [Fact]
    public void ReplaceUsedAnswers_OfferToKeepThem_WhenAllAreInUse()
    {
        var both = ParameterCheckTexts.ReplaceUsedAnswers(Uses(
            new ParameterUse(1, ParameterUseKind.Schedule, "Wandliste"), new ParameterUse(2, ParameterUseKind.ViewFilter, "Breit")));
        var one = ParameterCheckTexts.ReplaceUsedAnswers(ReplaceUses.Found(
            ParameterCheck.Run(new[] { Dicke }, DickeAndBreite, new[] { Other("Dicke", 1) }).ToReplace,
            new[] { new ParameterUse(1, ParameterUseKind.Schedule, "Wandliste") }));

        Assert.Equal(new[] { ReplaceChoice.ReplaceAll, ReplaceChoice.Cancel }, both.Select(a => a.Choice));
        Assert.Equal(new[] { "Replace them anyway", "Keep them" }, both.Select(a => a.Label));
        Assert.Equal(new[] { "Replace it anyway", "Keep it" }, one.Select(a => a.Label));
    }

    [Fact]
    public void ReplaceUsed_WhenRevitCantTell_SaysSo_AndAsksAboutThemAll()
    {
        ReplaceUses uses = ReplaceUses.Unknown(TwoToReplace.ToReplace, "the schedule is busy");

        Assert.Equal("Revit couldn't check the schedules and view filters", ParameterCheckTexts.ReplaceUsedTitle(uses));
        Assert.StartsWith("Revit couldn't tell which schedules and view filters use these parameters (the schedule is busy).\n\n",
            ParameterCheckTexts.ReplaceUsedQuestion(uses));
        var answers = ParameterCheckTexts.ReplaceUsedAnswers(uses);
        Assert.Equal(new[] { "Replace them anyway", "Keep them" }, answers.Select(a => a.Label));
        Assert.Equal("Any schedules and view filters that use them lose them until you add the new ones.", answers[0].Note);
    }

    [Fact]
    public void LeftInUse_SaysWhyEachWasLeft()
    {
        ReplaceUses uses = Uses(new ParameterUse(2, ParameterUseKind.ViewFilter, "Breit"));

        Assert.Equal(new[] { "Breite: left as it is, as schedules or view filters use it." }, ParameterCheckTexts.LeftInUse(uses));
    }

    private static readonly MasterParameterFile DickeAndBreite =
        MasterFileText.Read(MasterFileText.With((Dicke.Guid, "Dicke", "LENGTH"), (Breite.Guid, "Breite", "LENGTH")));

    // Both with other GUIDs in the model: Dicke as its element 1, Breite as 2.
    private static readonly ParameterCheck TwoToReplace =
        ParameterCheck.Run(new[] { Dicke, Breite }, DickeAndBreite, new[] { Other("Dicke", 1), Other("Breite", 2) });

    private static ReplaceUses Uses(params ParameterUse[] uses) => ReplaceUses.Found(TwoToReplace.ToReplace, uses);

    private static ModelBinding Other(string name, long id) => new(name, Guid.NewGuid(), true, new[] { "OST_GenericModel" }, id);

    [Fact]
    public void ReplaceQuestion_ListsEightNames_ThenCountsTheRest()
    {
        var parameters = Enumerable.Range(1, 10).Select(i => new LinkParameter(i, Guid.NewGuid(), "P" + i, "BAU", "GEO", "P" + i, "String", null, LinkTarget.Object, new[] { "OST_GenericModel" })).ToList();
        MasterParameterFile file = MasterFileText.Read(MasterFileText.With(parameters.Select(p => (p.Guid, p.RevitName, "TEXT")).ToArray()));

        string question = ParameterCheckTexts.ReplaceQuestion(ParameterCheck.Run(parameters, file, parameters.Select(p => Other(p.RevitName)).ToArray()));

        Assert.Contains("• P8\nand 2 more", question);
        Assert.DoesNotContain("P9", question);
    }

    private static ModelBinding Other(string name) => new(name, Guid.NewGuid(), true, new[] { "OST_GenericModel" });

    [Theory]
    [InlineData(ParameterState.Ready, "Ready")]
    [InlineData(ParameterState.Missing, "Missing")]
    [InlineData(ParameterState.CantAdd, "Can't add")]
    [InlineData(ParameterState.OtherGuid, "Other GUID")]
    [InlineData(ParameterState.Added, "Added")]
    public void State_IsWhatThePillSays(ParameterState state, string text)
    {
        Assert.Equal(text, ParameterCheckTexts.State(state));
    }
}
