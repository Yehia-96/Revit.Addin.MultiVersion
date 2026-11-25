using System;
using System.Linq;
using System.Text.RegularExpressions;
using MH.RevitTools.Database.Connection;
using MH.RevitTools.Database.Link;

namespace MH.RevitTools.Database.Tests.Link;

/// <summary>
/// The reader over the real revit views in the local dev MH_Database, which Studio's migrations
/// AddRevitLink and AddRevitObjectMaterials made. Only reads. Depends on the dev data for project 1761 Ulm: objects
/// with Bauteil-IDs, materials, what BT-1101-01 is made of, and Studio's history of all of them.
/// </summary>
[Trait("Category", "LocalMhDatabase")]
public sealed class SqlLinkDatabaseLocalMhTests
{
    private static readonly ConnectionSettings LocalMhDatabase =
        new(@"(localdb)\MSSQLLocalDB", "MH_Database", true, null, null, true, false);

    private readonly SqlLinkDatabase Database = new(LocalMhDatabase);

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
    public void Parameters_HaveTheirOwnGuid_AndARevitNameNoOtherHasOnTheirCategory()
    {
        LinkProjectData data = Database.GetProjectData(Ulm.ProjectId);

        Assert.All(data.Parameters, p =>
        {
            Assert.NotEqual(Guid.Empty, p.Guid);
            Assert.False(string.IsNullOrWhiteSpace(p.RevitName), "Parameter " + p.ParameterId + " has no Revit name.");
        });
        Assert.Equal(data.Parameters.Count, data.Parameters.Select(p => p.Guid).Distinct().Count());

        // Revit can't tell two parameters apart on one category if they share a name.
        var clashes = data.Parameters
            .SelectMany(p => p.Categories.Select(category => (category, name: p.RevitName.ToUpperInvariant())))
            .GroupBy(pair => pair)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key);
        Assert.Empty(clashes);
    }

    [Fact]
    public void GetParameters_IsTheDictionary_EachOnARevitCategory()
    {
        var parameters = Database.GetParameters();

        Assert.Equal(Database.GetProjectData(Ulm.ProjectId).Parameters.Count, parameters.Count);
        Assert.All(parameters, p => Assert.NotEmpty(p.Categories));
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

        // Objects point at materials by Material-ID, e.g. BAU-SPE-Betonstahl Mat.-ID.
        var materialIdPattern = new Regex(@"^[A-Z]{2}-\d{4}-\d{2}-[A-Z]{2,3}-\d{4}$");
        var pointedAt = data.ObjectValues.Select(v => v.Value).Where(value => value != null && materialIdPattern.IsMatch(value)).ToList();
        Assert.NotEmpty(pointedAt);
        Assert.All(pointedAt, id => Assert.Contains(data.Materials, m => m.MaterialId == id));
        Assert.All(data.MaterialValues, v => Assert.Contains(data.Materials, m => m.MaterialId == v.MaterialId));
    }

    [Fact]
    public void ObjectMaterials_NameTheMaterial_AndSayWhoSetItAndWhen()
    {
        LinkProjectData data = Database.GetProjectData(Ulm.ProjectId);

        Assert.Contains(data.ObjectMaterials, m => m.BauteilId == "BT-1101-01");
        Assert.All(data.ObjectMaterials, m =>
        {
            Assert.Matches(@"^[A-Z]{2}-\d{4}-\d{2}$", m.BauteilId);
            Assert.False(string.IsNullOrWhiteSpace(m.MaterialName), m.BauteilId + " is made of a material without a name.");
            Assert.NotNull(m.ChangedAtUtc);
            Assert.False(string.IsNullOrWhiteSpace(m.ChangedBy), "Nobody set what " + m.BauteilId + " is made of.");
            // The project's own is in the project's list, under the same name.
            LinkMaterial own = data.Materials.SingleOrDefault(material => material.MaterialId == m.MaterialId);
            Assert.True(own == null || own.Name == m.MaterialName, m.BauteilId + ": " + m.MaterialName);
        });
    }
}
