using System;
using System.Collections.Generic;
using MH.RevitTools.Database.Link;

namespace MH.RevitTools.Database.Tests.Link;

public sealed class PasswordWatchTests
{
    private static readonly LinkProject Ulm = new(13, 1761, 1, "Ulm");

    private int _refusals;

    private PasswordWatch Watch(ILinkDatabase database) => new(database, () => _refusals++);

    private static IEnumerable<Action<ILinkDatabase>> Questions()
    {
        yield return database => database.GetProjects();
        yield return database => database.GetParameters();
        yield return database => database.GetStamp(13);
        yield return database => database.GetProjectData(13);
    }

    [Fact]
    public void Answers_ComeThroughAsTheyAre()
    {
        var data = new LinkProjectData(13, 7, Array.Empty<LinkParameter>(), Array.Empty<LinkObjectValue>(), Array.Empty<LinkMaterial>(),
            Array.Empty<LinkMaterialValue>(), Array.Empty<LinkObjectMaterial>());
        var inner = new FakeLinkDatabase(new[] { Ulm }, new[] { data });
        PasswordWatch watch = Watch(inner);

        Assert.Same(Ulm, Assert.Single(watch.GetProjects()));
        Assert.Equal(7, watch.GetStamp(13));
        Assert.Same(data, watch.GetProjectData(13));
        Assert.Null(watch.GetStamp(99));
        Assert.Equal(inner.GetParameters().Count, watch.GetParameters().Count);
        Assert.Equal(0, _refusals);
    }

    [Fact]
    public void ARefusedPassword_IsTold_WhateverWasAsked_AndStillThrown()
    {
        PasswordWatch watch = Watch(new Failing(new LinkDatabaseException("refused", new InvalidOperationException(), true)));

        int told = 0;
        foreach (Action<ILinkDatabase> question in Questions())
        {
            Assert.True(Assert.Throws<LinkDatabaseException>(() => question(watch)).IsLoginRefused);
            Assert.Equal(++told, _refusals);
        }
    }

    [Fact]
    public void AnyOtherFailure_SaysNothingAboutThePassword()
    {
        PasswordWatch watch = Watch(new Failing(new LinkDatabaseException("The server doesn't answer.")));

        foreach (Action<ILinkDatabase> question in Questions())
            Assert.Throws<LinkDatabaseException>(() => question(watch));
        Assert.Throws<InvalidOperationException>(() => Watch(new Failing(new InvalidOperationException("a bug"))).GetProjects());
        Assert.Equal(0, _refusals);
    }

    [Fact]
    public void BothAreNeeded()
    {
        Assert.Throws<ArgumentNullException>(() => new PasswordWatch(null, () => { }));
        Assert.Throws<ArgumentNullException>(() => new PasswordWatch(new Failing(new InvalidOperationException()), null));
    }

    private sealed class Failing : ILinkDatabase
    {
        private readonly Exception _error;

        public Failing(Exception error) => _error = error;

        public IReadOnlyList<LinkProject> GetProjects() => throw _error;

        public IReadOnlyList<LinkParameter> GetParameters() => throw _error;

        public long? GetStamp(int projectId) => throw _error;

        public LinkProjectData GetProjectData(int projectId) => throw _error;
    }
}
