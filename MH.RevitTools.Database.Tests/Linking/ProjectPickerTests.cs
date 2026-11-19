using System;
using System.Collections.Generic;
using System.Linq;
using MH.RevitTools.Database.Link;
using MH.RevitTools.Database.Linking;
using static MH.RevitTools.Database.Tests.Linking.TestProjects;

namespace MH.RevitTools.Database.Tests.Linking;

public sealed class ProjectPickerTests
{
    private const string Model = "1761_Ulm_Tunnel.rvt";
    private const string NotLinked =
        Model + " is not linked yet. The link fills its elements by Bauteil_ID from this project's objects.";

    private static readonly DateTime LinkedAt = new(2026, 11, 19, 14, 32, 0, DateTimeKind.Utc);

    private static ProjectPicker Picker(string projectNumber, ModelLink link = null, IEnumerable<LinkProject> projects = null) =>
        new(Model, projectNumber, link, projects ?? All, TimeZoneInfo.Utc);

    private static string Text(IEnumerable<TextPart> parts) => string.Concat(parts.Select(p => p.Text));

    private static string[] Strong(IEnumerable<TextPart> parts) =>
        parts.Where(p => p.Style == TextStyle.Strong).Select(p => p.Text).ToArray();

    [Fact]
    public void NotLinked_SuggestsTheProjectFromTheProjectNumber()
    {
        ProjectPicker picker = Picker("1761-01");

        Assert.Equal(NotLinked, Text(picker.Subtitle));
        Assert.Equal(TextStyle.Code, picker.Subtitle[0].Style);
        Assert.False(picker.SubtitleIsWarning);
        Assert.Equal("Suggested from Project Information: Project Number 1761-01 matches 1761-01 Ulm.", Text(picker.Banner));
        Assert.Equal(new[] { "1761-01", "1761-01 Ulm" }, Strong(picker.Banner));
        Assert.False(picker.BannerIsWarning);
        Assert.Same(Ulm, picker.InitialPick);
        Assert.True(picker.CanLink(Ulm));
        Assert.Equal("Link to 1761-01 Ulm", picker.LinkButtonText(Ulm));
    }

    [Fact]
    public void NotLinked_WithoutASubNumber_AsksWhenTheNumberFitsSeveralProjects()
    {
        ProjectPicker picker = Picker("1761");

        Assert.Equal("Project Number 1761 has no sub-number and fits 2 projects. Pick the right one.", Text(picker.Banner));
        Assert.True(picker.BannerIsWarning);
        Assert.Null(picker.InitialPick);
        Assert.False(picker.CanLink(null));
        Assert.Equal("Pick a project", picker.LinkButtonText(null));
    }

    [Theory]
    [InlineData("", "Project Information has no Project Number. Pick the project.")]
    [InlineData("Ulm", "Project Number Ulm isn't written like 1761 or 1761-01. Pick the project.")]
    [InlineData("1761-03", "Project Number 1761-03 matches no project in the database. Pick the project.")]
    public void NotLinked_SaysWhyItSuggestsNothing(string projectNumber, string banner)
    {
        ProjectPicker picker = Picker(projectNumber);

        Assert.Equal(banner, Text(picker.Banner));
        Assert.True(picker.BannerIsWarning);
        Assert.Null(picker.InitialPick);
    }

    [Fact]
    public void NotLinked_WhenTwoProjectsShareNumberAndSubNumber_SaysSo()
    {
        var twin = new LinkProject(30, 1761, 1, "Ulm (copy)");

        ProjectPicker picker = Picker("1761-01", projects: new[] { Ulm, twin });

        Assert.Equal("Project Number 1761-01 fits 2 projects. Pick the right one.", Text(picker.Banner));
        Assert.Null(picker.InitialPick);
    }

    [Fact]
    public void Rows_ListTheProjectsWithTheModelsNumberFirst()
    {
        IReadOnlyList<ProjectRow> rows = Picker("1761").Rows("");

        Assert.Equal(new[] { Ulm, UlmSecond, Pfaffensteig, Muenchen, Brenner }, rows.Select(r => r.Project));
        Assert.Equal(new[] { "Same number", "Same number", "", "", "" }, rows.Select(r => r.Note));
        Assert.Equal("1761-01", rows[0].Code);
        Assert.Equal("Ulm", rows[0].Name);
    }

    [Fact]
    public void Rows_MarkTheSuggestedProject()
    {
        IReadOnlyList<ProjectRow> rows = Picker("1761-02").Rows(null);

        Assert.Equal(new[] { "Ulm", "Ulm, 2. Bauabschnitt" }, rows.Take(2).Select(r => r.Name));
        Assert.Equal(new[] { "Same number", "Suggested" }, rows.Take(2).Select(r => r.Note));
    }

    [Theory]
    [InlineData("münchen", "2. S-Bahn-Stammstrecke München")]
    [InlineData("1547", "Pfaffensteigtunnel")]
    [InlineData(" 1761-02 ", "Ulm, 2. Bauabschnitt")]
    [InlineData("1821-01 brenner", "Brenner Basistunnel")]
    public void Rows_FilterByNumberOrName_InAnyCase(string query, string name)
    {
        ProjectRow row = Assert.Single(Picker("1761-01").Rows(query));

        Assert.Equal(name, row.Name);
    }

    [Fact]
    public void Rows_KeepProjectsThatHoldEveryWordOfTheSearch()
    {
        Assert.Equal(new[] { Ulm, UlmSecond }, Picker("1761-01").Rows("1761 ulm").Select(r => r.Project));
        Assert.Equal(new[] { UlmSecond }, Picker("1761-01").Rows("ulm 2.").Select(r => r.Project));
    }

    [Fact]
    public void Rows_AreEmpty_WhenNothingMatches()
    {
        Assert.Empty(Picker("1761-01").Rows("Hamburg"));
        Assert.Equal(5, Picker("1761-01").Rows("   ").Count);
    }

    [Fact]
    public void Linked_ShowsTheLinkAndWhoMadeIt_AndOffersNothingToRedo()
    {
        ProjectPicker picker = Picker("1761-01", ModelLink.To(Ulm, LinkedAt, "Y.Abdelazziz"));

        Assert.Equal(Model + " is linked to 1761-01 Ulm. Linked by Y.Abdelazziz, 2026-11-19 14:32.", Text(picker.Subtitle));
        Assert.False(picker.SubtitleIsWarning);
        Assert.Empty(picker.Banner);
        Assert.Same(Ulm, picker.InitialPick);
        Assert.False(picker.CanLink(Ulm));
        Assert.Equal("Already linked", picker.LinkButtonText(Ulm));
        Assert.True(picker.CanLink(UlmSecond));
        Assert.Equal("Linked · Suggested", picker.Rows("")[0].Note);
    }

    [Fact]
    public void Linked_ShowsTheProjectsCurrentName()
    {
        var renamed = new LinkProject(13, 1761, 1, "Ulm Hauptbahnhof");

        ProjectPicker picker = Picker("1761-01", ModelLink.To(Ulm, LinkedAt, null), new[] { renamed });

        Assert.Equal(Model + " is linked to 1761-01 Ulm Hauptbahnhof. Linked 2026-11-19 14:32.", Text(picker.Subtitle));
    }

    [Fact]
    public void Linked_ToAnotherProjectThanTheProjectNumberSays_PointsItOut()
    {
        ProjectPicker picker = Picker("1761-01", ModelLink.To(Muenchen, LinkedAt, "A. Tester"));

        Assert.Equal("Project Number 1761-01 matches 1761-01 Ulm, not the linked project.", Text(picker.Banner));
        Assert.True(picker.BannerIsWarning);
        Assert.Same(Muenchen, picker.InitialPick);
        Assert.Equal(new[] { Muenchen, Ulm, UlmSecond }, picker.Rows("").Take(3).Select(r => r.Project));
        Assert.Equal(new[] { "Linked", "Suggested", "Same number" }, picker.Rows("").Take(3).Select(r => r.Note));
    }

    [Fact]
    public void Linked_ToAProjectTheDatabaseNoLongerHas_AsksAgain()
    {
        ProjectPicker picker = Picker("1761-01", new ModelLink(99, 1761, 1, "Ulm", null, null));

        Assert.Equal(
            Model + " is linked to 1761-01 Ulm, but the database has no such project any more. Pick the project again.",
            Text(picker.Subtitle));
        Assert.True(picker.SubtitleIsWarning);
        Assert.Null(picker.LinkedProject);
        Assert.Same(Ulm, picker.InitialPick);
        Assert.True(picker.CanLink(Ulm));
    }

    [Fact]
    public void Linked_ToAnIdThatIsAnotherProjectHere_AsksAgain()
    {
        // Linked in another database, where id 13 was 1762-01 Neu-Ulm; here id 13 is Ulm.
        ProjectPicker picker = Picker("1761-01", new ModelLink(13, 1762, 1, "Neu-Ulm", null, null));

        Assert.Equal(
            Model + " is linked to 1762-01 Neu-Ulm, but in this database that project is now 1761-01 Ulm. Pick the project again.",
            Text(picker.Subtitle));
        Assert.True(picker.SubtitleIsWarning);
        Assert.Null(picker.LinkedProject);
        Assert.True(picker.CanLink(Ulm));
        Assert.Equal("Link to 1761-01 Ulm", picker.LinkButtonText(Ulm));
    }

    [Fact]
    public void Linked_SaysOnlyWhatItKnowsAboutWhoAndWhen()
    {
        Assert.EndsWith("1761-01 Ulm. Linked by A. Tester.", Text(Picker("1761-01", new ModelLink(13, 1761, 1, "Ulm", null, "A. Tester")).Subtitle));
        Assert.EndsWith("1761-01 Ulm.", Text(Picker("1761-01", new ModelLink(13, 1761, 1, "Ulm", null, null)).Subtitle));
    }

    [Fact]
    public void Linked_ShowsTheTimeInTheGivenTimeZone()
    {
        TimeZoneInfo plusOne = TimeZoneInfo.CreateCustomTimeZone("Test+1", TimeSpan.FromHours(1), "Test+1", "Test+1");

        var picker = new ProjectPicker(Model, "1761-01", ModelLink.To(Ulm, LinkedAt, null), All, plusOne);

        Assert.EndsWith("Linked 2026-11-19 15:32.", Text(picker.Subtitle));
    }

    [Fact]
    public void LinkButton_ShortensLongNames()
    {
        var longName = new LinkProject(40, 1999, 1, "Neubaustrecke Wendlingen–Ulm, Albaufstieg und Filstalbrücke");

        string text = Picker("1999-01", projects: new[] { longName }).LinkButtonText(longName);

        Assert.Equal("Link to 1999-01 Neubaustrecke Wendlingen–Ulm, A…", text);
    }

    [Fact]
    public void SubtitleBeforeCheck_SaysWhatTheModelSaves()
    {
        Assert.Equal(NotLinked, Text(ProjectPicker.SubtitleBeforeCheck(Model, null)));
        Assert.Equal(Model + " is linked to 1761-01 Ulm.", Text(ProjectPicker.SubtitleBeforeCheck(Model, ModelLink.To(Ulm, LinkedAt, null))));
        Assert.StartsWith("This model is", Text(ProjectPicker.SubtitleBeforeCheck(" ", null)));
    }
}
