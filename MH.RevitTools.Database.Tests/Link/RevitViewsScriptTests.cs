using System;
using MH.RevitTools.Database.Link;
using Microsoft.Data.SqlClient;

namespace MH.RevitTools.Database.Tests.Link;

/// <summary>The handover script itself: what the office login will be able to read, and running it again.</summary>
[Trait("Category", "LocalDB")]
public sealed class RevitViewsScriptTests
{
    // A database user with nothing but the grant the request asks for, like the office login's role.
    private const string Reader = "link_reader";

    private const int PermissionDenied = 229;

    private static readonly string[] Views =
        { "Projects", "Parameters", "ParameterCategories", "ObjectValues", "Materials", "MaterialValues", "ProjectStamps", "ObjectMaterials" };

    [Fact]
    public void AUserWithOnlyTheRevitGrant_ReadsEveryView_ButNoTable()
    {
        using var database = new ThrowawayDatabase(StandInDatabaseFixture.StandInScripts);
        database.Execute(
            "CREATE TABLE dbo.Users (UserId int NOT NULL, PasswordHash nvarchar(max) NULL);"
            + " CREATE USER " + Reader + " WITHOUT LOGIN;"
            + " GRANT SELECT, EXECUTE ON SCHEMA::revit TO " + Reader + ";");

        // The views read dbo's tables through ownership chaining (schema revit is owned by dbo).
        foreach (string view in Views)
            Assert.True(Convert.ToInt64(database.QueryAs(Reader, "SELECT COUNT_BIG(*) FROM revit." + view + ";")) > 0, view);

        foreach (string table in new[] { "dbo.Users", "dbo.AuditLogs", "dbo.Projects", "dbo.ProjectObjectParameters", "dbo.ProjectObjects" })
        {
            var denied = Assert.Throws<SqlException>(() => database.QueryAs(Reader, "SELECT COUNT_BIG(*) FROM " + table + ";"));
            Assert.Equal(PermissionDenied, denied.Number);
        }
    }

    [Fact]
    public void TheScript_CanRunAgainOverItself()
    {
        using var database = new ThrowawayDatabase("StandInTables.sql", "RevitViews.sql", "RevitViews.sql");

        Assert.Equal(2, new SqlLinkDatabase(database.Settings).GetProjects().Count);
    }
}
