using System;
using System.Collections.Generic;
using System.Linq;
using Revit.Addin._2027.Services;

namespace MH.RevitTools.Database.Tests.ExcelImport;

/// <summary>Which of a workbook's parameters are put into the shared parameter file, and which are missing from it afterwards.</summary>
public sealed class SharedParameterNamesTests
{
    private static readonly string[] Nothing = Array.Empty<string>();

    [Fact]
    public void Take_IsTrueOnce_AlsoWhenBothSheetsHaveTheName()
    {
        var names = new SharedParameterNames(Nothing);

        Assert.True(names.Take("Projektnummer"));
        Assert.False(names.Take("Projektnummer"));
        Assert.False(names.Take("PROJEKTNUMMER"));
    }

    [Theory]
    [InlineData("Dicke")]
    [InlineData("dicke")]
    public void Take_IsFalse_ForWhatTheFileHas(string name) => Assert.False(new SharedParameterNames(new[] { "Dicke", "Dicke" }).Take(name));

    [Fact]
    public void Missing_IsWhatWasRefused_AndIsntInTheFile()
    {
        var names = new SharedParameterNames(Nothing);
        names.Take("Breite");
        names.Refused("Breite");
        names.Take("Ort");
        names.Refused("Ort");
        names.Refused("ort");

        // Seen in June 2026: Revit refused a parameter, and the file had it all the same.
        Assert.Equal(new[] { "Ort" }, names.Missing(new[] { "breite", "Dicke" }));
    }

    [Fact]
    public void Missing_IsEmpty_WhenNothingWasRefused()
    {
        var names = new SharedParameterNames(Nothing);
        names.Take("Dicke");

        // Whatever the file shows: what Revit has created isn't worth a warning.
        Assert.Empty(names.Missing(Nothing));
    }

    // The report of 25.11.2026: "Some parameters could not be created and were skipped: Projektnummer, Projektunternummer,
    // Materialnummer". They're in both sheets, and Revit refuses a name its group has. Typ is in both too, in two groups.
    [Fact]
    public void TheNewWorkbook_IntoANewFile_WarnsOfNothing_AndCreatesEachOnce()
    {
        var objects = new[] { ("Projektnummer", "Kategorisierung"), ("Projektunternummer", "Kategorisierung"), ("Dicke", "Geometrie"),
            ("Materialnummer", "Kategorisierung"), ("Typ", "Beschreibung") };
        var materials = new[] { ("Projekt-ID", "Kategorisierung"), ("Projektnummer", "Kategorisierung"), ("Projektunternummer", "Kategorisierung"),
            ("Materialnummer", "Kategorisierung"), ("Typ", "Sonstiges") };
        var bauteilId = new[] { ("Bauteil_ID", "Kategorisierung") };
        var file = new FakeFile();
        string[] asOpened = file.Names;
        var names = new SharedParameterNames(asOpened);

        foreach ((string name, string group) in objects.Concat(materials).Concat(bauteilId))
        {
            if (names.Take(name) && !file.TryCreate(name, group))
                names.Refused(name);
        }

        Assert.Empty(names.Missing(file.Names));
        Assert.Equal(new[] { "Bauteil_ID", "Dicke", "Materialnummer", "Projekt-ID", "Projektnummer", "Projektunternummer", "Typ" },
            file.Names.OrderBy(name => name, StringComparer.Ordinal));
    }

    // As Revit's: a name can be in two groups, but once in each.
    private sealed class FakeFile
    {
        private readonly HashSet<(string, string)> _definitions = new();

        public string[] Names => _definitions.Select(definition => definition.Item1).ToArray();

        public bool TryCreate(string name, string group) => _definitions.Add((name, group));
    }
}
