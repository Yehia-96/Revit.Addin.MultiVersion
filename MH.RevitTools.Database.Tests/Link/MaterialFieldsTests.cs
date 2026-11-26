using System;
using System.Collections.Generic;
using System.Linq;
using MH.RevitTools.Database.Link;

namespace MH.RevitTools.Database.Tests.Link;

/// <summary>
/// A material's own fields among its values (step 12): the view and the reader together, over stand-in tables. Each test has a
/// database of its own.
/// </summary>
[Trait("Category", "LocalDB")]
public sealed class MaterialFieldsTests : IDisposable
{
    private const string Beton = "MB-1761-01-BET-0031";

    // The parameters of the twelve fields, by their number; the ids are made up, as they differ per database.
    private static readonly string[] Codes =
        { "00031", "00032", "00033", "00036", "00037", "00038", "00039", "00040", "00041", "00042", "00043", "00044" };

    private readonly ThrowawayDatabase _database = new(StandInDatabaseFixture.StandInScripts);

    public MaterialFieldsTests()
    {
        _database.Execute(string.Concat(Codes.Select(code =>
            "INSERT dbo.Parameters (ParameterId, Name, DataType, Unit, ParameterSetId, ParameterGroupId, RevitCategory, SharedParameterGuid, RevitName, Code)"
            + " VALUES (" + IdOf(code) + ", N'P" + code + "', N'String', N'', 2, 2, N'Material', NEWID(), NULL, N'" + code + "');")));
        _database.Execute(
            "UPDATE dbo.Materials SET LegacyNumber = 131, AlternativeName = N'31 alt', Designation = N'Beton',"
            + " Note = N'Zeile 1' + NCHAR(13) + NCHAR(10) + N'Zeile 2', Norm = N'DIN 1045-2, EN 206-1' WHERE MaterialId = 123;");
    }

    public void Dispose() => _database.Dispose();

    private static int IdOf(string code) => 1000 + int.Parse(code);

    private LinkProjectData Read(int projectId = 13) => new SqlLinkDatabase(_database.Settings).GetProjectData(projectId);

    // By the parameter's number; the group's own value (parameter 140) isn't one of them.
    private Dictionary<string, string> Fields(string materialId = Beton, int projectId = 13) => Read(projectId).MaterialValues
        .Where(value => value.MaterialId == materialId && value.ParameterId > 1000)
        .ToDictionary(value => (value.ParameterId - 1000).ToString("00000"), value => value.Value);

    [Fact]
    public void EveryField_IsARow_AsStored_BesideTheGroupsValues()
    {
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["00031"] = Beton,
                ["00032"] = "MB",
                ["00033"] = "Bauteil",
                ["00036"] = "BET",
                ["00037"] = "Beton",
                ["00038"] = "31",
                ["00039"] = "131",
                ["00040"] = "31-Beton, C35/45",
                ["00041"] = "31 alt",
                ["00042"] = "Beton",
                ["00043"] = "Zeile 1\r\nZeile 2",
                ["00044"] = "DIN 1045-2, EN 206-1",
            },
            Fields());
        Assert.Equal("C35/45", Read().MaterialValues.Single(value => value.ParameterId == 140).Value);
    }

    [Fact]
    public void TheCodes_AreWrittenAsTheMaterialIdHasThem()
    {
        _database.Execute("UPDATE dbo.MaterialCategories SET Code = N' mb ' WHERE MaterialCategoryId = 1; UPDATE dbo.MaterialGroups SET Code = N'bet' WHERE MaterialGroupId = 1;");

        Dictionary<string, string> fields = Fields();

        Assert.Equal((Beton, "MB", "BET"), (fields["00031"], fields["00032"], fields["00036"]));
    }

    [Fact]
    public void ABlankField_HasNoRow()
    {
        _database.Execute(
            "UPDATE dbo.Materials SET LegacyNumber = NULL, AlternativeName = N'', Designation = N'  ', Note = NCHAR(13) + NCHAR(10) + NCHAR(9), Norm = NULL"
            + " WHERE MaterialId = 123;");

        Assert.Equal(new[] { "00031", "00032", "00033", "00036", "00037", "00038", "00040" }, Fields().Keys.OrderBy(code => code));
    }

    // One row per material and parameter: for these twelve the group's values don't count, also where the field is blank.
    [Fact]
    public void OnlyTheOwnField_Counts()
    {
        _database.Execute(
            "UPDATE dbo.Materials SET Norm = NULL WHERE MaterialId = 123;"
            + " INSERT dbo.MaterialParameterValues VALUES (123, " + IdOf("00040") + ", 13, N'aus der Gruppe'), (123, " + IdOf("00044") + ", 13, N'aus der Gruppe');");

        Dictionary<string, string> fields = Fields();

        Assert.Equal("31-Beton, C35/45", fields["00040"]);
        Assert.False(fields.ContainsKey("00044"));
        Assert.All(Read().MaterialValues.GroupBy(value => (value.MaterialId, value.ParameterId)), rows => Assert.Single(rows));
    }

    [Fact]
    public void AParameterThatDoesntGoToRevit_HasNoRow()
    {
        _database.Execute("UPDATE dbo.Parameters SET RevitCategory = NULL WHERE Code = N'00042';");

        Dictionary<string, string> fields = Fields();

        Assert.False(fields.ContainsKey("00042"));
        Assert.Equal(11, fields.Count);
    }

    // Not a global one, not one without a full ID, not another project's.
    [Fact]
    public void OnlyTheProjectsOwnMaterials_HaveThem()
    {
        Assert.All(Read().MaterialValues, value => Assert.Equal(Beton, value.MaterialId));
        Assert.Equal("MB-1760-01-BET-0001", Fields("MB-1760-01-BET-0001", 1)["00031"]);
    }

    [Fact]
    public void WhoAndWhen_AreTheMaterialsNewestHistoryRow()
    {
        Assert.All(Read().MaterialValues.Where(value => value.ParameterId > 1000), value => Assert.Null(value.ChangedAtUtc ?? (object)value.ChangedBy));

        _database.Execute(
            "INSERT dbo.AuditLogs VALUES (7, '2026-11-20T09:00:00', N'C. Tester', N'Material', N'123'), (8, '2026-11-21T10:30:00', N'D. Tester', N'Material', N'123'),"
            + " (9, '2026-11-22T11:00:00', N'E. Tester', N'Material', N'125'), (10, '2026-11-23T12:00:00', N'F. Tester', N'MaterialGroup', N'1');");

        Assert.All(Read().MaterialValues.Where(value => value.ParameterId > 1000), value =>
        {
            Assert.Equal(new DateTime(2026, 11, 21, 10, 30, 0), value.ChangedAtUtc);
            Assert.Equal(DateTimeKind.Utc, value.ChangedAtUtc.Value.Kind);
            Assert.Equal("D. Tester", value.ChangedBy);
        });
        Assert.Equal("A. Tester", Read().MaterialValues.Single(value => value.ParameterId == 140).ChangedBy);
    }

    [Fact]
    public void ARenamedGroup_ShowsAtOnce_AndMovesTheStamp()
    {
        long before = Read().Stamp;

        _database.Execute(
            "UPDATE dbo.MaterialGroups SET Name = N'Beton neu' WHERE MaterialGroupId = 1;"
            + " INSERT dbo.AuditLogs VALUES (7, '2026-11-20T09:00:00', N'C. Tester', N'MaterialGroup', N'1');");

        Assert.Equal("Beton neu", Fields()["00037"]);
        Assert.NotEqual(before, Read().Stamp);
    }
}
