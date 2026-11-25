using System;
using System.IO;
using System.Linq;
using MH.RevitTools.Database.Checking;
using MH.RevitTools.Database.Connection;
using MH.RevitTools.Database.Link;
using MH.RevitTools.Database.Linking;

namespace MH.RevitTools.Database.Tests.Checking;

public sealed class PullCacheTests : IDisposable
{
    private const string FakePassword = "fake-password-for-tests";

    private static readonly ConnectionSettings Office = new("CADSQL.mh-frlg.local,1433", "MH_Database", false, "reader", FakePassword, true, true);
    private static readonly ConnectionSettings Dev = new("(localdb)\\MSSQLLocalDB", "MH_Database", true, null, null, false, false);
    private static readonly DateTime ReadAt = new(2026, 11, 22, 9, 12, 0, DateTimeKind.Utc);
    private static readonly ModelLink UlmLink = new(UlmData.ProjectId, 1761, 1, "Ulm", null, null);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "MH.RevitTools.Database.Tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    private static LinkProjectData Everything()
    {
        DateTime at = new(2026, 11, 11, 20, 47, 53, DateTimeKind.Utc);
        return UlmData.With(4930622457727,
            new[] { UlmData.Value("BT-1101-01", UlmData.Guete, "B500B", at, "TheAdmin"), UlmData.Value("ET-2101-01", UlmData.Dicke, "0.3") },
            new[] { new LinkMaterial("MB-1761-01-BET-0011", "11-Beton, C12/15", "BET", "Beton", "Beton Ansicht", null) },
            new[] { new LinkMaterialValue("MB-1761-01-BET-0011", UlmData.Festigkeit.ParameterId, "C12/15", at, "TheAdmin") },
            new[]
            {
                new LinkObjectMaterial("BT-1101-01", "MB-1761-01-BET-0011", "11-Beton, C12/15", at, "TheAdmin"),
                new LinkObjectMaterial("ET-2101-01", null, "Ohne Nummer", null, null),
            });
    }

    [Fact]
    public void APull_ComesBackWhole()
    {
        var cache = new PullCache(_folder, Office);

        Assert.True(cache.TrySave(Everything(), UlmLink, ReadAt, out string problem), problem);
        CachedPull kept = cache.Load(UlmLink);

        Assert.Equal(ReadAt, kept.ReadAtUtc);
        Assert.Equal(DateTimeKind.Utc, kept.ReadAtUtc.Kind);
        LinkProjectData data = kept.Data;
        Assert.Equal(UlmData.ProjectId, data.ProjectId);
        Assert.Equal(4930622457727, data.Stamp);

        LinkParameter dicke = data.Parameters.Single(p => p.ParameterId == UlmData.Dicke.ParameterId);
        Assert.Equal(UlmData.Dicke.Guid, dicke.Guid);
        Assert.Equal("m", dicke.Unit);
        Assert.Equal("Double", dicke.DataType);
        Assert.Equal(new[] { "OST_GenericModel" }, dicke.Categories);
        Assert.Equal(LinkTarget.Material, data.Parameters.Single(p => p.ParameterId == UlmData.Festigkeit.ParameterId).Target);

        LinkObjectValue guete = data.ObjectValues.Single(v => v.BauteilId == "BT-1101-01");
        Assert.Equal("B500B", guete.Value);
        Assert.Equal("TheAdmin", guete.ChangedBy);
        Assert.Equal(new DateTime(2026, 11, 11, 20, 47, 53, DateTimeKind.Utc), guete.ChangedAtUtc);
        Assert.Equal(DateTimeKind.Utc, guete.ChangedAtUtc.Value.Kind);
        Assert.Null(data.ObjectValues.Single(v => v.BauteilId == "ET-2101-01").ChangedAtUtc);

        LinkMaterial material = Assert.Single(data.Materials);
        Assert.Equal("11-Beton, C12/15", material.Name);
        Assert.Equal("Beton Ansicht", material.SurfacePattern);
        Assert.Null(material.CutPattern);
        Assert.Equal("C12/15", Assert.Single(data.MaterialValues).Value);

        LinkObjectMaterial madeOf = data.ObjectMaterials.Single(m => m.BauteilId == "BT-1101-01");
        Assert.Equal(("MB-1761-01-BET-0011", "11-Beton, C12/15", "TheAdmin"), (madeOf.MaterialId, madeOf.MaterialName, madeOf.ChangedBy));
        Assert.Equal(new DateTime(2026, 11, 11, 20, 47, 53, DateTimeKind.Utc), madeOf.ChangedAtUtc);
        Assert.Equal(DateTimeKind.Utc, madeOf.ChangedAtUtc.Value.Kind);
        LinkObjectMaterial withoutId = data.ObjectMaterials.Single(m => m.BauteilId == "ET-2101-01");
        Assert.Null(withoutId.MaterialId);
        Assert.Null(withoutId.ChangedAtUtc);
    }

    // It doesn't have what objects are made of, and would say that nothing differs.
    [Fact]
    public void APullOfAnEarlierLayout_IsntUsed()
    {
        var cache = new PullCache(_folder, Office);
        cache.TrySave(Everything(), UlmLink, ReadAt, out _);
        string path = cache.PathFor(UlmData.ProjectId), text = File.ReadAllText(path);
        Assert.Contains("\"Version\":3,", text);

        File.WriteAllText(path, text.Replace("\"Version\":3,", "\"Version\":2,"));

        Assert.Null(cache.Load(UlmLink));
    }

    [Fact]
    public void EachDatabase_KeepsItsOwnPulls()
    {
        new PullCache(_folder, Office).TrySave(Everything(), UlmLink, ReadAt, out _);

        Assert.Null(new PullCache(_folder, Dev).Load(UlmLink));
        Assert.NotEqual(PullCache.KeyFor(Office), PullCache.KeyFor(Dev));
        Assert.NotEqual(new PullCache(_folder, Office).PathFor(13), new PullCache(_folder, Dev).PathFor(13));
    }

    [Fact]
    public void TheKey_NamesOnlyServerAndDatabase_WhateverTheCaseOrSignIn()
    {
        var sameOtherwise = new ConnectionSettings("cadsql.MH-FRLG.local,1433", "mh_database", true, null, null, false, false);

        Assert.Equal(PullCache.KeyFor(Office), PullCache.KeyFor(sameOtherwise));
        Assert.Matches("^[0-9a-f]{16}$", PullCache.KeyFor(Office));
    }

    [Fact]
    public void TheFile_NeverHoldsTheLoginOrPassword()
    {
        var cache = new PullCache(_folder, Office);
        cache.TrySave(Everything(), UlmLink, ReadAt, out _);

        string path = cache.PathFor(UlmData.ProjectId);
        string text = File.ReadAllText(path);
        Assert.DoesNotContain(FakePassword, text);
        Assert.DoesNotContain("reader", text);
        Assert.DoesNotContain(FakePassword, path);
        Assert.DoesNotContain("CADSQL", path, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NoPull_OrABrokenOne_GivesNothing()
    {
        var cache = new PullCache(_folder, Office);
        Assert.Null(cache.Load(UlmLink));

        Directory.CreateDirectory(_folder);
        File.WriteAllText(cache.PathFor(UlmData.ProjectId), "{ not json");
        Assert.Null(cache.Load(UlmLink));

        File.WriteAllText(cache.PathFor(UlmData.ProjectId), "{\"Version\": 99, \"ProjectId\": 13}");
        Assert.Null(cache.Load(UlmLink));
    }

    [Fact]
    public void APullKeptForAnotherProjectNumber_IsntUsed_EvenUnderTheSameId()
    {
        var cache = new PullCache(_folder, Office);
        cache.TrySave(Everything(), UlmLink, ReadAt, out _);

        Assert.Null(cache.Load(new ModelLink(UlmData.ProjectId, 1762, 1, "Neu-Ulm", null, null)));
        Assert.Null(cache.Load(new ModelLink(UlmData.ProjectId, 1761, 2, "Ulm", null, null)));
        Assert.NotNull(cache.Load(new ModelLink(UlmData.ProjectId, 1761, 1, "Ulm, renamed", null, null)));
    }

    [Fact]
    public void TryRemove_ForgetsThePull_AndAMissingOneIsFine()
    {
        var cache = new PullCache(_folder, Office);
        cache.TrySave(Everything(), UlmLink, ReadAt, out _);

        Assert.True(cache.TryRemove(UlmData.ProjectId, out string problem), problem);
        Assert.Null(cache.Load(UlmLink));
        Assert.True(cache.TryRemove(UlmData.ProjectId, out _));
    }

    [Fact]
    public void SavingAgain_ReplacesTheFile_AndLeavesNoTemporaryFiles()
    {
        var cache = new PullCache(_folder, Office);
        cache.TrySave(Everything(), UlmLink, ReadAt, out _);
        cache.TrySave(UlmData.Bodenplatte(9), UlmLink, ReadAt.AddHours(1), out _);

        Assert.Equal(9, cache.Load(UlmLink).Data.Stamp);
        Assert.Single(Directory.GetFiles(_folder));
    }

    [Fact]
    public void AFolderThatCantBeWritten_SaysWhy()
    {
        string blocker = Path.Combine(Path.GetTempPath(), "MH.RevitTools.Database.Tests", Guid.NewGuid().ToString("N") + ".txt");
        Directory.CreateDirectory(Path.GetDirectoryName(blocker));
        File.WriteAllText(blocker, "a file where the folder should be");
        try
        {
            var cache = new PullCache(blocker, Office);

            Assert.False(cache.TrySave(Everything(), UlmLink, ReadAt, out string problem));
            Assert.StartsWith("Couldn't keep the last pull", problem);
        }
        finally
        {
            File.Delete(blocker);
        }
    }
}
