using MH.RevitTools.Database.Linking;
using static MH.RevitTools.Database.Tests.Linking.TestProjects;

namespace MH.RevitTools.Database.Tests.Linking;

public sealed class ProjectCodeTests
{
    [Theory]
    [InlineData(1761, 1, "1761-01")]
    [InlineData(1547, 13, "1547-13")]
    [InlineData(1761, 0, "1761")]
    [InlineData(12, 12, "0012-12")]
    public void WritesProjectsTheWayModelsDo(int number, int subNumber, string code)
    {
        Assert.Equal(code, ProjectCode.Format(number, subNumber));
    }

    [Fact]
    public void Describe_AddsTheName_WhenThereIsOne()
    {
        Assert.Equal("1761-01 Ulm", ProjectCode.Describe(Ulm));
        Assert.Equal("1761-01", ProjectCode.Describe(1761, 1, " "));
    }
}
