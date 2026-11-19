using System;
using MH.RevitTools.Database.Link;
using MH.RevitTools.Database.Linking;
using static MH.RevitTools.Database.Tests.Linking.TestProjects;

namespace MH.RevitTools.Database.Tests.Linking;

public sealed class ModelLinkTests
{
    private static readonly DateTime LinkedAt = new(2026, 11, 19, 14, 32, 5, DateTimeKind.Utc);

    [Fact]
    public void To_KeepsTheProject_WhoLinkedIt_AndWhen()
    {
        ModelLink link = ModelLink.To(Ulm, LinkedAt, " Y.Abdelazziz ");

        Assert.Equal(13, link.ProjectId);
        Assert.Equal(1761, link.Number);
        Assert.Equal(1, link.SubNumber);
        Assert.Equal("Ulm", link.Name);
        Assert.Equal(LinkedAt, link.LinkedAtUtc);
        Assert.Equal("Y.Abdelazziz", link.LinkedBy);
        Assert.Equal("1761-01 Ulm", link.Describe());
    }

    [Fact]
    public void KeepsTimesInUtc()
    {
        DateTime local = new(2026, 11, 19, 15, 32, 5, DateTimeKind.Local);

        Assert.Equal(local.ToUniversalTime(), new ModelLink(13, 1761, 1, "Ulm", local, null).LinkedAtUtc);
        Assert.Equal(DateTimeKind.Utc, new ModelLink(13, 1761, 1, "Ulm", new DateTime(2026, 11, 19), null).LinkedAtUtc.Value.Kind);
        Assert.Null(new ModelLink(13, 1761, 1, "Ulm", null, "  ").LinkedBy);
    }

    [Fact]
    public void Check_Holds_WhileTheIdHasTheSameNumberAndSubNumber_EvenUnderANewName()
    {
        var renamed = new LinkProject(13, 1761, 1, "Ulm Hauptbahnhof");

        ModelLinkCheck check = ModelLink.To(Ulm, LinkedAt, null).Check(new[] { Muenchen, renamed });

        Assert.Equal(ModelLinkStatus.Matches, check.Status);
        Assert.Same(renamed, check.Project);
    }

    [Fact]
    public void Check_SaysTheProjectIsGone_WhenTheDatabaseHasNoSuchId()
    {
        ModelLinkCheck check = ModelLink.To(Ulm, LinkedAt, null).Check(new[] { Muenchen });

        Assert.Equal(ModelLinkStatus.ProjectGone, check.Status);
        Assert.Null(check.Project);
    }

    [Fact]
    public void Check_SaysTheProjectChanged_WhenTheIdIsAnotherProjectHere()
    {
        // E.g. a model linked in another database, where id 13 was 1762-01.
        var link = new ModelLink(13, 1762, 1, "Neu-Ulm", LinkedAt, null);

        ModelLinkCheck check = link.Check(All);

        Assert.Equal(ModelLinkStatus.ProjectChanged, check.Status);
        Assert.Same(Ulm, check.Project);
    }
}
