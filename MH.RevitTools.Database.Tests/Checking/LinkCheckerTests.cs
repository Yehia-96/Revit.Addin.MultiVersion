using System;
using System.Collections.Generic;
using System.IO;
using MH.RevitTools.Database.Checking;
using MH.RevitTools.Database.Connection;
using MH.RevitTools.Database.Link;
using MH.RevitTools.Database.Linking;

namespace MH.RevitTools.Database.Tests.Checking;

public sealed class LinkCheckerTests : IDisposable
{
    private static readonly ModelLink UlmLink = new(UlmData.ProjectId, 1761, 1, "Ulm", null, null);
    private static readonly ConnectionSettings Dev = new("(localdb)\\MSSQLLocalDB", "MH_Database", true, null, null, false, false);
    private static readonly ConnectionSettings Office = new("CADSQL.mh-frlg.local,1433", "MH_Database", false, "reader", "fake-password-for-tests", true, true);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "MH.RevitTools.Database.Tests", Guid.NewGuid().ToString("N"));
    private DateTime _now = new(2026, 11, 22, 13, 32, 0, DateTimeKind.Utc);
    private ConnectionFileResult _file = ConnectionFileResult.Loaded("database.json", Dev, null);

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    private LinkChecker Checker(ILinkDatabase database) => new(() => _file, _ => database, _folder, () => _now);

    private static FakeLinkDatabase Database(LinkProjectData data) => new(new[] { UlmData.Project }, new[] { data });

    [Fact]
    public void TheFirstCheck_ReadsEverything_AndKeepsThePull()
    {
        var database = new CountingDatabase(Database(UlmData.Bodenplatte(7)));

        CheckOutcome outcome = Checker(database).Check(UlmLink);

        Assert.Equal(CheckState.Answered, outcome.State);
        Assert.Equal(7, outcome.Data.Stamp);
        Assert.Equal(_now, outcome.CheckedAtUtc);
        Assert.Equal(_now, outcome.LastAnswerUtc);
        Assert.Equal(_now, outcome.DataReadAtUtc);
        Assert.False(outcome.DataFromCache);
        Assert.Equal(1, database.DataReads);
        Assert.Equal("(localdb)\\MSSQLLocalDB · MH_Database", outcome.Where);
        Assert.Null(outcome.CacheProblem);
        Assert.Equal(7, new PullCache(_folder, Dev).Load(UlmLink).Data.Stamp);
    }

    [Fact]
    public void LaterChecks_ReadOnlyTheStamp_WhileItStays()
    {
        var database = new CountingDatabase(Database(UlmData.Bodenplatte(7)));
        LinkChecker checker = Checker(database);

        CheckOutcome first = checker.Check(UlmLink);
        _now = _now.AddMinutes(1);
        CheckOutcome second = checker.Check(UlmLink);

        Assert.Equal(CheckState.Answered, second.State);
        Assert.Equal(1, database.DataReads);
        Assert.Equal(1, database.StampReads);
        Assert.Equal(first.DataVersion, second.DataVersion);
        Assert.Equal(_now, second.LastAnswerUtc);
        Assert.Equal(first.DataReadAtUtc, second.DataReadAtUtc);
    }

    [Fact]
    public void AMovedStamp_ReadsEverythingAgain()
    {
        FakeLinkDatabase fake = Database(UlmData.Bodenplatte(7));
        var database = new CountingDatabase(fake);
        LinkChecker checker = Checker(database);
        CheckOutcome first = checker.Check(UlmLink);

        fake.Update(UlmData.Bodenplatte(7, guete: "B500A"));
        CheckOutcome second = checker.Check(UlmLink);

        Assert.Equal(2, database.DataReads);
        Assert.Equal(8, second.Data.Stamp);
        Assert.True(second.DataVersion > first.DataVersion);
        Assert.Contains(second.Data.ObjectValues, value => value.Value == "B500A");
    }

    [Fact]
    public void EachNewSession_ReadsEverything_WhateverTheStampSays()
    {
        FakeLinkDatabase fake = Database(UlmData.Bodenplatte(7));
        Checker(new CountingDatabase(fake)).Check(UlmLink);
        var nextSession = new CountingDatabase(fake);

        CheckOutcome outcome = Checker(nextSession).Check(UlmLink);

        Assert.Equal(CheckState.Answered, outcome.State);
        Assert.Equal(1, nextSession.DataReads);
        Assert.False(outcome.DataFromCache);
    }

    [Fact]
    public void WithoutAConnectionFile_ItsNotSetUp()
    {
        _file = ConnectionFileResult.Failed("C:\\ProgramData\\MH\\RevitTools\\database.json", "No connection file at C:\\ProgramData\\MH\\RevitTools\\database.json.");
        var database = new CountingDatabase(Database(UlmData.Bodenplatte(7)));

        CheckOutcome outcome = Checker(database).Check(UlmLink);

        Assert.Equal(CheckState.NotSetUp, outcome.State);
        Assert.StartsWith("No connection file at", outcome.Problem);
        Assert.Null(outcome.Data);
        Assert.Equal(0, database.DataReads + database.StampReads);
    }

    [Fact]
    public void AnUnreachableDatabase_ShowsThePullKeptFromAnEarlierSession()
    {
        DateTime earlier = _now;
        Checker(Database(UlmData.Bodenplatte(7))).Check(UlmLink);
        _now = _now.AddHours(3);
        var offline = new CountingDatabase(Database(UlmData.Bodenplatte(7))) { Fails = true };

        CheckOutcome outcome = Checker(offline).Check(UlmLink);

        Assert.Equal(CheckState.NotReachable, outcome.State);
        Assert.Equal("The server doesn't answer.", outcome.Problem);
        Assert.True(outcome.DataFromCache);
        Assert.Equal(7, outcome.Data.Stamp);
        Assert.Equal(earlier, outcome.DataReadAtUtc);
        Assert.Equal(earlier, outcome.LastAnswerUtc);
    }

    [Fact]
    public void AnUnreachableDatabase_WithNothingKept_HasNoValues()
    {
        var offline = new CountingDatabase(Database(UlmData.Bodenplatte(7))) { Fails = true };

        CheckOutcome outcome = Checker(offline).Check(UlmLink);

        Assert.Equal(CheckState.NotReachable, outcome.State);
        Assert.Null(outcome.Data);
        Assert.Null(outcome.LastAnswerUtc);
    }

    [Fact]
    public void GoingOffline_KeepsThisSessionsValues_AndTheTimeOfTheLastAnswer()
    {
        var database = new CountingDatabase(Database(UlmData.Bodenplatte(7)));
        LinkChecker checker = Checker(database);
        DateTime answered = _now;
        CheckOutcome first = checker.Check(UlmLink);

        database.Fails = true;
        _now = _now.AddMinutes(5);
        CheckOutcome offline = checker.Check(UlmLink);

        Assert.Equal(CheckState.NotReachable, offline.State);
        Assert.False(offline.DataFromCache);
        Assert.Equal(first.DataVersion, offline.DataVersion);
        Assert.Equal(answered, offline.LastAnswerUtc);
        Assert.Equal(_now, offline.CheckedAtUtc);
    }

    [Fact]
    public void BackOnline_TheStampCheckCarriesOn()
    {
        var database = new CountingDatabase(Database(UlmData.Bodenplatte(7)));
        LinkChecker checker = Checker(database);
        checker.Check(UlmLink);
        database.Fails = true;
        checker.Check(UlmLink);

        database.Fails = false;
        CheckOutcome back = checker.Check(UlmLink);

        Assert.Equal(CheckState.Answered, back.State);
        Assert.Equal(1, database.DataReads);
    }

    [Fact]
    public void AProjectTheDatabaseDoesntHave_IsMissing()
    {
        var database = new CountingDatabase(Database(UlmData.Bodenplatte(7)));
        var elsewhere = new ModelLink(99, 1999, 1, "Nirgendwo", null, null);

        CheckOutcome outcome = Checker(database).Check(elsewhere);

        Assert.Equal(CheckState.ProjectMissing, outcome.State);
        Assert.Null(outcome.Data);
        Assert.Contains("This database has no project 1999-01 Nirgendwo (id 99).", outcome.Problem);
    }

    [Fact]
    public void AnIdThatMeansAnotherProjectNow_IsChanged_AndShowsNoValues()
    {
        var other = new LinkProject(UlmData.ProjectId, 1762, 1, "Neu-Ulm");
        var fake = new FakeLinkDatabase(new[] { other }, new[] { UlmData.Bodenplatte(7) });

        CheckOutcome outcome = Checker(fake).Check(UlmLink);

        Assert.Equal(CheckState.ProjectChanged, outcome.State);
        Assert.Same(other, outcome.OtherProject);
        Assert.Null(outcome.Data);
        Assert.Equal("In this database the model's project id means 1762-01 Neu-Ulm, not 1761-01 Ulm.", outcome.Problem);
    }

    [Fact]
    public void AnotherConnectionFile_StartsAfresh()
    {
        var database = new CountingDatabase(Database(UlmData.Bodenplatte(7)));
        LinkChecker checker = Checker(database);
        checker.Check(UlmLink);

        _file = ConnectionFileResult.Loaded("database.json", Office, null);
        CheckOutcome outcome = checker.Check(UlmLink);

        Assert.Equal(2, database.DataReads);
        Assert.Equal("CADSQL · MH_Database", outcome.Where);
    }

    [Fact]
    public void AnotherDatabasesData_NeverGetsAVersionAlreadySeen()
    {
        var database = new CountingDatabase(Database(UlmData.Bodenplatte(7)));
        LinkChecker checker = Checker(database);
        CheckOutcome dev = checker.Check(UlmLink);

        _file = ConnectionFileResult.Loaded("database.json", Office, null);
        CheckOutcome office = checker.Check(UlmLink);

        Assert.True(office.DataVersion > dev.DataVersion);
    }

    [Fact]
    public void AProjectThatsGone_TakesItsKeptPullWithIt()
    {
        Checker(Database(UlmData.Bodenplatte(7))).Check(UlmLink);
        var emptied = new FakeLinkDatabase(Array.Empty<LinkProject>(), Array.Empty<LinkProjectData>());

        CheckOutcome gone = Checker(emptied).Check(UlmLink);
        var offline = new CountingDatabase(Database(UlmData.Bodenplatte(7))) { Fails = true };
        CheckOutcome later = Checker(offline).Check(UlmLink);

        Assert.Equal(CheckState.ProjectMissing, gone.State);
        Assert.Null(new PullCache(_folder, Dev).Load(UlmLink));
        Assert.Equal(CheckState.NotReachable, later.State);
        Assert.Null(later.Data);
    }

    [Fact]
    public void AnIdThatMeansAnotherProjectNow_TakesItsKeptPullWithIt()
    {
        Checker(Database(UlmData.Bodenplatte(7))).Check(UlmLink);
        var renumbered = new FakeLinkDatabase(new[] { new LinkProject(UlmData.ProjectId, 1762, 1, "Neu-Ulm") }, new[] { UlmData.Bodenplatte(7) });

        Checker(renumbered).Check(UlmLink);

        Assert.Null(new PullCache(_folder, Dev).Load(UlmLink));
    }

    [Fact]
    public void Faults_AreThrown_NotPassedOffAsAnUnreachableDatabase()
    {
        LinkChecker checker = new(() => _file, _ => throw new InvalidOperationException("a bug"), _folder, () => _now);

        Assert.Throws<InvalidOperationException>(() => checker.Check(UlmLink));
    }

    /// <summary>Counts the reads, and can play a database that doesn't answer.</summary>
    private sealed class CountingDatabase : ILinkDatabase
    {
        private readonly ILinkDatabase _inner;

        public CountingDatabase(ILinkDatabase inner) => _inner = inner;

        public bool Fails { get; set; }

        public int StampReads { get; private set; }

        public int DataReads { get; private set; }

        public IReadOnlyList<LinkProject> GetProjects()
        {
            FailIfAsked();
            return _inner.GetProjects();
        }

        public IReadOnlyList<LinkParameter> GetParameters()
        {
            FailIfAsked();
            return _inner.GetParameters();
        }

        public long? GetStamp(int projectId)
        {
            FailIfAsked();
            StampReads++;
            return _inner.GetStamp(projectId);
        }

        public LinkProjectData GetProjectData(int projectId)
        {
            FailIfAsked();
            DataReads++;
            return _inner.GetProjectData(projectId);
        }

        private void FailIfAsked()
        {
            if (Fails)
                throw new LinkDatabaseException("The server doesn't answer.");
        }
    }
}
