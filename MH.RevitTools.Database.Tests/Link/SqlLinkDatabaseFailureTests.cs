using MH.RevitTools.Database.Link;

namespace MH.RevitTools.Database.Tests.Link;

/// <summary>Each test changes or breaks a stand-in database of its own on purpose.</summary>
[Trait("Category", "LocalDB")]
public sealed class SqlLinkDatabaseFailureTests
{
    private const string AddHistoryRow = "INSERT dbo.AuditLogs VALUES ({0}, '2026-11-19T09:00:00', N'A. Tester', N'{1}', N'{2}');";

    private static ThrowawayDatabase StandIn() => new(StandInDatabaseFixture.StandInScripts);

    [Fact]
    public void GetStamp_MovesWithAChangeThatCommittedLate()
    {
        using var database = StandIn();
        var link = new SqlLinkDatabase(database.Settings);
        long before = link.GetStamp(13).Value;

        // A history row with a lower id than the newest: a transaction that drew its number first
        // but committed after the add-in last looked. A MAX of the ids alone would never see it.
        database.Execute(string.Format(AddHistoryRow, 0, "ProjectObjectParameter", "13|392|61"));

        Assert.NotEqual(before, link.GetStamp(13).Value);
    }

    [Fact]
    public void GetStamp_IgnoresLoginsAndSettings()
    {
        using var database = StandIn();
        var link = new SqlLinkDatabase(database.Settings);
        long before = link.GetStamp(13).Value;

        database.Execute(string.Format(AddHistoryRow, 10, "User", "5") + string.Format(AddHistoryRow, 11, "AppSetting", "1"));

        Assert.Equal(before, link.GetStamp(13).Value);
    }

    [Fact]
    public void MaterialWithAnEmptyGroupCode_GetsTheIdStudioGivesIt()
    {
        using var database = StandIn();
        database.Execute(
            "INSERT dbo.MaterialGroups VALUES (2, N' ', N'Ohne Code');"
            + " INSERT dbo.Materials (MaterialId, Name, ProjectId, MaterialCategoryId, MaterialGroupId, MaterialNumber)"
            + " VALUES (130, N'Ohne Gruppe', 13, 1, 2, 5);");
        var link = new SqlLinkDatabase(database.Settings);

        // Studio trims the code to nothing and keeps the material: MB-1761-01--0005.
        Assert.Contains(link.GetProjectData(13).Materials, m => m.MaterialId == "MB-1761-01--0005");
    }

    [Fact]
    public void GetProjects_WithoutTheViews_SaysTheyAreMissing()
    {
        using var database = new ThrowawayDatabase();
        var link = new SqlLinkDatabase(database.Settings);

        var error = Assert.Throws<LinkDatabaseException>(() => link.GetProjects());

        Assert.Contains("revit.Projects", error.Message);
        Assert.Contains("revit views are missing", error.Message);
    }

    [Fact]
    public void GetProjectData_StampThatNeverSettles_GivesUpInPlainWords()
    {
        using var database = StandIn();
        database.Execute(
            "DROP TABLE dbo.AuditLogs;\nGO\n"
            + "CREATE VIEW dbo.AuditLogs AS SELECT CAST(ABS(CHECKSUM(NEWID())) AS bigint) AS AuditLogId,"
            + " SYSUTCDATETIME() AS OccurredAtUtc, N'A. Tester' AS UserDisplay, N'Parameter' AS EntityName, N'1' AS EntityKey;");
        var link = new SqlLinkDatabase(database.Settings);

        var error = Assert.Throws<LinkDatabaseException>(() => link.GetProjectData(13));

        Assert.Contains("kept changing", error.Message);
    }

    [Fact]
    public void GetStamp_ColumnOfAnotherKind_SaysTheViewsDontMatch()
    {
        using var database = StandIn();
        database.Execute(
            "DROP VIEW revit.ProjectStamps;\nGO\n"
            + "CREATE VIEW revit.ProjectStamps AS SELECT ProjectId, N'not a number' AS Stamp FROM dbo.Projects;");
        var link = new SqlLinkDatabase(database.Settings);

        var error = Assert.Throws<LinkDatabaseException>(() => link.GetStamp(13));

        Assert.Contains("don't match", error.Message);
    }
}
