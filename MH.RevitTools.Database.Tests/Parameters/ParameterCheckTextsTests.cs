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
        Assert.Contains("A parameter that a schedule or a view filter uses is left as it is", question);
        Assert.Equal("", ParameterCheckTexts.ReplaceButton(ParameterCheck.Run(new[] { Dicke }, file, Array.Empty<ModelBinding>())));
    }

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
