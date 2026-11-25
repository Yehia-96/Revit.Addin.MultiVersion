using System.Linq;
using Revit.Addin._2027.Services;

namespace MH.RevitTools.Database.Tests.ExcelImport;

/// <summary>What Add Parameters says when it's done.</summary>
public sealed class ParameterRunTests
{
    private const string NoMatch = "No matching elements found.";
    private const string Workbook = "Planungsdatenbank.xlsx, saved 25.11.2026 10:48";

    [Fact]
    public void NoElement_SaysThatNoneMatches()
    {
        var run = new ParameterRun(0, 0, 0);

        Assert.Equal(NoMatch, run.Message(NoMatch, Workbook));
        Assert.False(run.ChangedAnything);
    }

    // The report of 25.11.2026: it said "No matching elements found", with the element and its Bauteil_ID on the screen.
    [Fact]
    public void AnElementWithNothingToChange_IsntNoMatch()
    {
        var run = new ParameterRun(1, 0, 0);

        Assert.Equal(
            "1 element matches, and nothing was changed: its parameters already have the values of the Excel file."
            + "\n\nExcel file: " + Workbook + ". If you have changed it since then, save it in Excel and run this again.",
            run.Message(NoMatch, Workbook));
        Assert.False(run.ChangedAnything);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void SeveralElements_WithoutAWorkbook(string workbook) =>
        Assert.Equal("12 elements match, and nothing was changed: their parameters already have the values of the Excel file.",
            new ParameterRun(12, 0, 0).Message(NoMatch, workbook));

    [Theory]
    [InlineData(11, 30, "11 parameters applied.\n30 values applied")]
    [InlineData(0, 1, "0 parameters applied.\n1 values applied")]
    [InlineData(3, 0, "3 parameters applied.\n0 values applied")]
    public void ARunThatChangedSomething_CountsIt(int parameters, int values, string message)
    {
        var run = new ParameterRun(2, parameters, values);

        Assert.Equal(message, run.Message(NoMatch, Workbook));
        Assert.True(run.ChangedAnything);
    }

    [Fact]
    public void WhatCouldntBeAdded_IsNamedOnce_WithTheReason()
    {
        var run = new ParameterRun(2, 5, 5, new[] { "Dicke (a type parameter in this project)", "Ort (refused by Revit)", "Dicke (a type parameter in this project)" });

        Assert.Equal("5 parameters applied.\n5 values applied\n\nNot added: Dicke (a type parameter in this project), Ort (refused by Revit).",
            run.Message(NoMatch, Workbook));
    }

    // "Already have the values" must stay true: an element without the parameter can't have its value.
    [Theory]
    [InlineData(1, "1 element matches, and nothing was changed: its other parameters already have the values of the Excel file.")]
    [InlineData(2, "2 elements match, and nothing was changed: their other parameters already have the values of the Excel file.")]
    public void NothingChanged_ThoughSomethingCouldntBeWritten_SaysSo(int elements, string first) =>
        Assert.Equal(first + "\n\nNot written, because an element lacks the parameter, or has it read-only or of another kind: Breite, Dicke.",
            new ParameterRun(elements, 0, 0, notWritten: new[] { "Breite", "Dicke" }).Message(NoMatch, null));

    [Fact]
    public void WhatCouldntBeAddedOrWritten_IsSaidAfterTheCounts() =>
        Assert.Equal("1 parameters applied.\n2 values applied\n\nNot added: Ort (refused by Revit)."
            + "\n\nNot written, because an element lacks the parameter, or has it read-only or of another kind: Dicke.",
            new ParameterRun(1, 1, 2, new[] { "Ort (refused by Revit)" }, new[] { "Dicke", "Dicke" }).Message(NoMatch, Workbook));

    [Theory]
    [InlineData(15, "P14.")]
    [InlineData(16, "P14 and 1 more.")]
    [InlineData(40, "P14 and 25 more.")]
    public void ManyNames_AreCutOff(int count, string end) =>
        Assert.EndsWith(", " + end, new ParameterRun(1, 0, 1, notWritten: Enumerable.Range(0, count).Select(n => "P" + n)).Message(NoMatch, null));
}
