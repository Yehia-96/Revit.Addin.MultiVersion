using MH.RevitTools.Database.Link;
using MH.RevitTools.Database.Linking;
using static MH.RevitTools.Database.Tests.Linking.TestProjects;

namespace MH.RevitTools.Database.Tests.Linking;

public sealed class ProjectSuggestionTests
{
    [Fact]
    public void WithASubNumber_SuggestsTheProjectWithThatNumberAndSubNumber()
    {
        ProjectSuggestion suggestion = ProjectSuggestion.For("1761-02", All);

        Assert.Equal(SuggestionKind.Suggested, suggestion.Kind);
        Assert.Same(UlmSecond, suggestion.Project);
        Assert.Equal(new[] { Ulm, UlmSecond }, suggestion.SameNumber);
    }

    [Fact]
    public void WithoutASubNumber_SuggestsTheOnlyProjectWithThatNumber()
    {
        ProjectSuggestion suggestion = ProjectSuggestion.For("1760", All);

        Assert.Equal(SuggestionKind.Suggested, suggestion.Kind);
        Assert.Same(Muenchen, suggestion.Project);
    }

    [Fact]
    public void WithoutASubNumber_SuggestsNothing_WhenSeveralProjectsHaveThatNumber()
    {
        ProjectSuggestion suggestion = ProjectSuggestion.For("1761", All);

        Assert.Equal(SuggestionKind.SeveralMatch, suggestion.Kind);
        Assert.Null(suggestion.Project);
        Assert.Equal(new[] { Ulm, UlmSecond }, suggestion.Matches);
    }

    [Fact]
    public void SuggestsNothing_WhenTwoProjectsShareNumberAndSubNumber()
    {
        // The database doesn't forbid it; the user has to say which.
        var twin = new LinkProject(30, 1761, 1, "Ulm (copy)");

        ProjectSuggestion suggestion = ProjectSuggestion.For("1761-01", new[] { Ulm, twin });

        Assert.Equal(SuggestionKind.SeveralMatch, suggestion.Kind);
        Assert.Null(suggestion.Project);
        Assert.Equal(new[] { Ulm, twin }, suggestion.Matches);
    }

    [Fact]
    public void SuggestsNothing_WhenNoProjectFits_ButKnowsTheOnesWithThatNumber()
    {
        ProjectSuggestion suggestion = ProjectSuggestion.For("1761-03", All);

        Assert.Equal(SuggestionKind.NoMatch, suggestion.Kind);
        Assert.Null(suggestion.Project);
        Assert.Empty(suggestion.Matches);
        Assert.Equal(new[] { Ulm, UlmSecond }, suggestion.SameNumber);
    }

    [Theory]
    [InlineData(null, SuggestionKind.NoProjectNumber)]
    [InlineData(" ", SuggestionKind.NoProjectNumber)]
    [InlineData("Ulm", SuggestionKind.NotAProjectNumber)]
    [InlineData("1761.01", SuggestionKind.NotAProjectNumber)]
    public void SuggestsNothing_WithoutAReadableProjectNumber(string projectNumber, SuggestionKind kind)
    {
        ProjectSuggestion suggestion = ProjectSuggestion.For(projectNumber, All);

        Assert.Equal(kind, suggestion.Kind);
        Assert.Null(suggestion.Project);
        Assert.Empty(suggestion.SameNumber);
    }
}
