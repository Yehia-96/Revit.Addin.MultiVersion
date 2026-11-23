using MH.RevitTools.Database.Comparing;

namespace MH.RevitTools.Database.Tests.Comparing;

public sealed class WorkbookColumnsTests
{
    private static WorkbookColumns Columns()
    {
        var columns = new WorkbookColumns();
        columns.Add(" 109 ", "BAU", "GEO", "Dicke");
        columns.Add("120", "BAU", "BES", "Beschreibung");
        columns.Add("320", "PRI", "BES", "Beschreibung");
        return columns;
    }

    [Fact]
    public void AColumn_IsFoundByItsIdAndItsHeader() => Assert.Equal("BAU-GEO-Dicke", Columns().KeyOf("109", "Dicke"));

    [Theory]
    [InlineData("120", "Beschreibung (Bauteilinformation)", "BAU-BES-Beschreibung")]
    [InlineData("320", "Beschreibung (Produktinformation)", "PRI-BES-Beschreibung")]
    [InlineData("320", "beschreibung (Produktinformation / Beschreibung)", "PRI-BES-Beschreibung")]
    public void ARepeatedHeader_IsFoundUnderTheNameTheReaderGivesIt(string id, string name, string expected) =>
        Assert.Equal(expected, Columns().KeyOf(id, name));

    [Fact]
    public void AColumnWithAnotherColumnsId_IsntTakenForThatOther()
    {
        WorkbookColumns columns = Columns();
        columns.Add("109", "BAU", "GEO", "Breite");

        Assert.Equal("BAU-GEO-Dicke", columns.KeyOf("109", "Dicke"));
        Assert.Equal("BAU-GEO-Breite", columns.KeyOf("109", "Breite"));
    }

    [Fact]
    public void TwoColumnsWithTheSameIdAndHeader_ThatMeanDifferentParameters_GiveNeithersKey()
    {
        WorkbookColumns columns = Columns();
        columns.Add("120", "PRI", "BES", "Beschreibung");

        Assert.Equal("?-?-Beschreibung (Bauteilinformation)", columns.KeyOf("120", "Beschreibung (Bauteilinformation)"));
    }

    [Theory]
    [InlineData("999", "Dicke")]
    [InlineData("109", "Dicke2")]
    [InlineData("109", "Breite")]
    [InlineData(null, "Dicke")]
    public void WhatFitsNoColumn_GetsAKeyTheDictionaryCantHave(string id, string name) =>
        Assert.Equal("?-?-" + name, Columns().KeyOf(id, name));

    [Fact]
    public void AColumnWithoutAnId_IsStillFoundByItsHeader()
    {
        WorkbookColumns columns = Columns();
        columns.Add("", "BAU", "GEO", "Neigung");

        Assert.Equal("BAU-GEO-Neigung", columns.KeyOf(null, "Neigung"));
    }
}
