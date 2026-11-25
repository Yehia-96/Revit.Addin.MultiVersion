using System;
using MH.RevitTools.Database.Link;

namespace MH.RevitTools.Database.Tests.Link;

public sealed class FakeLinkDatabaseTests
{
    private static readonly LinkProject Ulm = new(13, 1761, 1, "Ulm");
    private static readonly LinkProject Munich = new(1, 1760, 1, "2. S-Bahn-Stammstrecke München");

    private static LinkProjectData UlmData(long stamp, string thickness) => new(
        13,
        stamp,
        new[] { new LinkParameter(61, Guid.NewGuid(), "Dicke", "BAU", "GEO", "Dicke", "Double", "m", LinkTarget.Object, new[] { "OST_GenericModel" }) },
        new[] { new LinkObjectValue("BT-1101-01", 61, thickness, null, null) },
        Array.Empty<LinkMaterial>(),
        Array.Empty<LinkMaterialValue>(),
        Array.Empty<LinkObjectMaterial>());

    [Fact]
    public void ReturnsWhatItWasGiven()
    {
        var database = new FakeLinkDatabase(new[] { Munich, Ulm }, new[] { UlmData(7, "0.3") });

        Assert.Equal(new[] { Munich, Ulm }, database.GetProjects());
        Assert.Equal(7, database.GetStamp(13));
        Assert.Equal("0.3", Assert.Single(database.GetProjectData(13).ObjectValues).Value);
    }

    [Fact]
    public void ProjectWithoutData_HasNoStamp()
    {
        var database = new FakeLinkDatabase(new[] { Munich, Ulm }, new[] { UlmData(7, "0.3") });

        Assert.Null(database.GetStamp(1));
        Assert.Null(database.GetProjectData(1));
        Assert.Null(database.GetStamp(999));
    }

    [Fact]
    public void Update_MovesTheStampOnByOne()
    {
        var database = new FakeLinkDatabase(new[] { Ulm }, new[] { UlmData(7, "0.3") });

        database.Update(UlmData(0, "0.35"));

        Assert.Equal(8, database.GetStamp(13));
        Assert.Equal("0.35", Assert.Single(database.GetProjectData(13).ObjectValues).Value);
    }

    [Fact]
    public void Update_KeepsEverythingElse()
    {
        LinkProjectData read = UlmData(7, "0.3");
        var database = new FakeLinkDatabase(new[] { Ulm }, new[] { read });

        database.Update(new LinkProjectData(13, 0, read.Parameters, read.ObjectValues, read.Materials, read.MaterialValues,
            new[] { new LinkObjectMaterial("BT-1101-01", "MB-1761-01-BET-0013", "13-Beton, C20/25", null, null) }));

        LinkProjectData now = database.GetProjectData(13);
        Assert.Equal("13-Beton, C20/25", Assert.Single(now.ObjectMaterials).MaterialName);
        Assert.Single(now.ObjectValues);
        Assert.Empty(read.ObjectMaterials);
    }

    [Fact]
    public void GetParameters_IsTheDictionaryOfTheProjectsData()
    {
        var database = new FakeLinkDatabase(new[] { Munich, Ulm }, new[] { UlmData(7, "0.3") });

        Assert.Equal("Dicke", Assert.Single(database.GetParameters()).RevitName);
        Assert.Empty(new FakeLinkDatabase(new[] { Ulm }, Array.Empty<LinkProjectData>()).GetParameters());
    }

    [Fact]
    public void DataForAProjectNotInTheList_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => new FakeLinkDatabase(new[] { Munich }, new[] { UlmData(7, "0.3") }));

        var database = new FakeLinkDatabase(new[] { Munich }, Array.Empty<LinkProjectData>());
        Assert.Throws<ArgumentException>(() => database.Update(UlmData(7, "0.3")));
    }
}
