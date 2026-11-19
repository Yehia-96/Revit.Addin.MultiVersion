using System.Linq;
using System.Text.RegularExpressions;
using MH.RevitTools.Database.Link;

namespace MH.RevitTools.Database.Tests.Link;

/// <summary>
/// The revit views (RevitViews.sql) and the reader over the local dev MH_Database, which they only read.
/// Depends on the dev data for project 1761 Ulm: objects with Bauteil-IDs, materials, and Studio's
/// history of both.
/// </summary>
[Trait("Category", "LocalMhDatabase")]
public sealed class SqlLinkDatabaseLocalMhTests : IClassFixture<LocalMhViewsFixture>
{
    private readonly SqlLinkDatabase Database;

    public SqlLinkDatabaseLocalMhTests(LocalMhViewsFixture fixture) =>
        Database = new SqlLinkDatabase(fixture.Database.Settings);

    private LinkProject Ulm => Database.GetProjects().Single(p => p.Number == 1761 && p.SubNumber == 1);

    [Fact]
    public void Projects_IncludeUlm()
    {
        Assert.Equal("Ulm", Ulm.Name);
    }

    [Fact]
    public void Stamp_IsSteadyWhileNothingChanges()
    {
        long stamp = Database.GetStamp(Ulm.ProjectId).Value;

        Assert.Equal(stamp, Database.GetStamp(Ulm.ProjectId));
        Assert.Equal(stamp, Database.GetProjectData(Ulm.ProjectId).Stamp);
    }

    [Fact]
    public void ObjectValues_UseBauteilIdsBuiltFromTheCatalog_OnePerObject()
    {
        LinkProjectData data = Database.GetProjectData(Ulm.ProjectId);

        Assert.Contains(data.ObjectValues, v => v.BauteilId == "BT-1101-01");
        Assert.Contains(data.ObjectValues, v => v.BauteilId == "ET-2101-01");

        // Two objects with the same Bauteil-ID would give one value twice; Revit couldn't tell them apart.
        var duplicates = data.ObjectValues.GroupBy(v => (v.BauteilId, v.ParameterId)).Where(g => g.Count() > 1).Select(g => g.Key);
        Assert.Empty(duplicates);
    }

    [Fact]
    public void ObjectValues_KeepPlaceholdersAndUnits()
    {
        LinkProjectData data = Database.GetProjectData(Ulm.ProjectId);

        LinkParameter dicke = data.Parameters.Single(p => p.SetCode == "BAU" && p.GroupCode == "GEO" && p.Name == "Dicke");
        Assert.Equal("m", dicke.Unit);
        Assert.Equal(LinkTarget.Object, dicke.Target);
        Assert.Equal(new[] { "OST_GenericModel" }, dicke.Categories);
        Assert.Equal("[Modeller]", data.ObjectValues.Single(v => v.BauteilId == "BT-1101-01" && v.ParameterId == dicke.ParameterId).Value);
    }

    [Fact]
    public void ObjectValues_SayWhoChangedThemAndWhen()
    {
        LinkProjectData data = Database.GetProjectData(Ulm.ProjectId);

        Assert.All(data.ObjectValues.Where(v => v.BauteilId == "BT-1101-01"), v =>
        {
            Assert.NotNull(v.ChangedAtUtc);
            Assert.False(string.IsNullOrWhiteSpace(v.ChangedBy));
        });
    }

    [Fact]
    public void Materials_MatchTheMaterialIdsTheObjectsPointAt()
    {
        LinkProjectData data = Database.GetProjectData(Ulm.ProjectId);

        Assert.Contains(data.Materials, m => m.MaterialId == "MB-1761-01-BET-0031" && m.GroupCode == "BET");

        // Objects point at materials by Material-ID, e.g. IDE-KAT-Material-ID and BAU-SPE-Betonstahl Mat.-ID.
        var materialIdPattern = new Regex(@"^[A-Z]{2}-\d{4}-\d{2}-[A-Z]{2,3}-\d{4}$");
        var pointedAt = data.ObjectValues.Select(v => v.Value).Where(value => value != null && materialIdPattern.IsMatch(value)).ToList();
        Assert.NotEmpty(pointedAt);
        Assert.All(pointedAt, id => Assert.Contains(data.Materials, m => m.MaterialId == id));
        Assert.All(data.MaterialValues, v => Assert.Contains(data.Materials, m => m.MaterialId == v.MaterialId));
    }
}
