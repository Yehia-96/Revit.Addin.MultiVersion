using MH.RevitTools.Database.Link;

namespace MH.RevitTools.Database.Tests.Link;

public sealed class SqlLinkDatabaseTargetTests
{
    [Theory]
    [InlineData("Object", LinkTarget.Object)]
    [InlineData("material", LinkTarget.Material)]
    [InlineData("MATERIAL", LinkTarget.Material)]
    public void ParseTarget_KnowsObjectAndMaterial(string text, LinkTarget expected)
    {
        Assert.Equal(expected, SqlLinkDatabase.ParseTarget(text, 61));
    }

    [Theory]
    [InlineData("Element")]
    [InlineData(null)]
    public void ParseTarget_AnythingElse_NamesTheParameter(string text)
    {
        var error = Assert.Throws<LinkDatabaseException>(() => SqlLinkDatabase.ParseTarget(text, 61));

        Assert.Contains("Parameter 61", error.Message);
    }
}
