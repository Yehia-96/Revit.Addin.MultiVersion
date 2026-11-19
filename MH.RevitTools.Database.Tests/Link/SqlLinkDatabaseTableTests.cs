using System;
using System.Linq;
using MH.RevitTools.Database.Link;

namespace MH.RevitTools.Database.Tests.Link;

/// <summary>The revit views and the reader together, over stand-in tables with known rows.</summary>
[Trait("Category", "LocalDB")]
public sealed class SqlLinkDatabaseTableTests : IClassFixture<StandInDatabaseFixture>
{
    // Three history rows that count (the login row doesn't); the newest is 3.
    private const long ExpectedStamp = 3L * 4294967296 + 3;

    private readonly SqlLinkDatabase _database;

    public SqlLinkDatabaseTableTests(StandInDatabaseFixture fixture) =>
        _database = new SqlLinkDatabase(fixture.Database.Settings);

    [Fact]
    public void GetProjects_ListsEveryProjectByNumber()
    {
        var projects = _database.GetProjects();

        Assert.Equal(new[] { 1760, 1761 }, projects.Select(p => p.Number));
        LinkProject ulm = projects[1];
        Assert.Equal(13, ulm.ProjectId);
        Assert.Equal(1, ulm.SubNumber);
        Assert.Equal("Ulm", ulm.Name);
    }

    [Fact]
    public void GetStamp_ComesFromTheHistory_AndIsTheSameForEveryProject()
    {
        Assert.Equal(ExpectedStamp, _database.GetStamp(13));
        Assert.Equal(ExpectedStamp, _database.GetStamp(1));
    }

    [Fact]
    public void UnknownProject_HasNoStampAndNoData()
    {
        Assert.Null(_database.GetStamp(999));
        Assert.Null(_database.GetProjectData(999));
    }

    [Fact]
    public void GetProjectData_ReadsOnlyParametersThatGoToRevit()
    {
        LinkProjectData data = _database.GetProjectData(13);

        Assert.Equal(ExpectedStamp, data.Stamp);
        Assert.Equal(new[] { 8, 61, 140 }, data.Parameters.Select(p => p.ParameterId).OrderBy(id => id));

        // A Revit category the views don't map yet: the parameter comes through without a category,
        // so Check parameters can report it.
        LinkParameter wall = data.Parameters.Single(p => p.ParameterId == 8);
        Assert.Empty(wall.Categories);
        Assert.Equal(LinkTarget.Object, wall.Target);

        LinkParameter dicke = data.Parameters.Single(p => p.ParameterId == 61);
        Assert.Equal(new Guid("3f1c2a6e-8b4d-4c1e-9a7f-61d1c4e0a061"), dicke.Guid);
        Assert.Equal("BAU", dicke.SetCode);
        Assert.Equal("GEO", dicke.GroupCode);
        Assert.Equal("Double", dicke.DataType);
        Assert.Equal("m", dicke.Unit);
        Assert.Equal(LinkTarget.Object, dicke.Target);
        Assert.Equal(new[] { "OST_GenericModel" }, dicke.Categories);

        LinkParameter bezeichnung = data.Parameters.Single(p => p.ParameterId == 140);
        Assert.Null(bezeichnung.Unit);
        Assert.Equal(LinkTarget.Material, bezeichnung.Target);
        Assert.Equal(new[] { "OST_Materials" }, bezeichnung.Categories);
    }

    [Fact]
    public void GetProjectData_GivesTheRevitName_OrThePlainNameUntilStudioHasSetOne()
    {
        LinkProjectData data = _database.GetProjectData(13);

        Assert.Equal("Dicke", data.Parameters.Single(p => p.ParameterId == 61).RevitName);
        Assert.Equal("Bezeichnung (Material)", data.Parameters.Single(p => p.ParameterId == 140).RevitName);
        Assert.Equal("Wandstärke", data.Parameters.Single(p => p.ParameterId == 8).RevitName);
    }

    [Fact]
    public void GetProjectData_KeysValuesByBauteilIdsBuiltLikeStudio()
    {
        LinkProjectData data = _database.GetProjectData(13);

        Assert.Equal(
            new[] { ("BT-1101-01", 61, "0.3"), ("ET-2101-12", 61, "[Modeller]") },
            data.ObjectValues.Select(v => (v.BauteilId, v.ParameterId, v.Value)).OrderBy(v => v.BauteilId));
    }

    [Fact]
    public void GetProjectData_SaysWhoChangedAValueLastAndWhen()
    {
        LinkProjectData data = _database.GetProjectData(13);

        LinkObjectValue changed = data.ObjectValues.Single(v => v.BauteilId == "BT-1101-01");
        Assert.Equal(new DateTime(2026, 11, 18, 14, 28, 0), changed.ChangedAtUtc);
        Assert.Equal(DateTimeKind.Utc, changed.ChangedAtUtc.Value.Kind);
        Assert.Equal("B. Tester", changed.ChangedBy);

        LinkObjectValue unchanged = data.ObjectValues.Single(v => v.BauteilId == "ET-2101-12");
        Assert.Null(unchanged.ChangedAtUtc);
        Assert.Null(unchanged.ChangedBy);
    }

    [Fact]
    public void GetProjectData_ReadsTheProjectsMaterialsWithIdsBuiltLikeStudio()
    {
        LinkProjectData data = _database.GetProjectData(13);

        LinkMaterial material = Assert.Single(data.Materials);
        Assert.Equal("MB-1761-01-BET-0031", material.MaterialId);
        Assert.Equal("31-Beton, C35/45", material.Name);
        Assert.Equal("BET", material.GroupCode);
        Assert.Equal("Beton", material.Group);
        Assert.Null(material.SurfacePattern);
        Assert.Equal("Ortbeton - bewehrt", material.CutPattern);

        LinkMaterialValue value = Assert.Single(data.MaterialValues);
        Assert.Equal("MB-1761-01-BET-0031", value.MaterialId);
        Assert.Equal(140, value.ParameterId);
        Assert.Equal("C35/45", value.Value);
        Assert.Equal(new DateTime(2026, 11, 18, 15, 0, 0), value.ChangedAtUtc);
        Assert.Equal("A. Tester", value.ChangedBy);
    }
}
