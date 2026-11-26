using System.Linq;
using MH.RevitTools.Database.Changes;

namespace MH.RevitTools.Database.Tests.Changes;

/// <summary>When two texts say the same.</summary>
public sealed class SameTextTests
{
    [Theory]
    [InlineData("Beton", "Beton")]
    [InlineData(" Beton\t", "Beton")]
    [InlineData(null, "")]
    [InlineData(null, null)]
    [InlineData("", " \r\n ")]
    [InlineData("Zeile 1\r\nZeile 2", "Zeile 1\nZeile 2")]
    [InlineData("Zeile 1\r\nZeile 2", "Zeile 1\rZeile 2")]
    [InlineData("Zeile 1\nZeile 2\r\n\r\nZeile 4", "Zeile 1\r\nZeile 2\n\nZeile 4")]
    [InlineData("Zeile 1\r\nZeile 2\r\n", "Zeile 1\nZeile 2")]
    public void TheSame(string one, string other)
    {
        Assert.True(SameText.Is(one, other));
        Assert.True(SameText.Is(other, one));
        Assert.True(SameText.Comparer.Equals(one, other));
        Assert.Equal(SameText.Comparer.GetHashCode(one), SameText.Comparer.GetHashCode(other));
    }

    [Theory]
    [InlineData("Beton", "beton")]
    [InlineData("Beton", null)]
    [InlineData("Zeile 1\r\nZeile 2", "Zeile 1 Zeile 2")]
    [InlineData("Zeile 1\r\nZeile 2", "Zeile 1Zeile 2")]
    [InlineData("Zeile 1\r\nZeile 2", "Zeile 1\r\n\r\nZeile 2")]
    [InlineData("Zeile 1\nZeile 2", "Zeile 1\n Zeile 2")]
    public void NotTheSame(string one, string other)
    {
        Assert.False(SameText.Is(one, other));
        Assert.False(SameText.Is(other, one));
        Assert.False(SameText.Comparer.Equals(one, other));
    }

    [Fact]
    public void TheComparer_FindsWhatDiffers()
    {
        string[] texts = { "Zeile 1\r\nZeile 2", "Zeile 1\nZeile 2", " Zeile 1\rZeile 2", "Zeile 1", null, "" };

        Assert.Equal(new[] { "Zeile 1\r\nZeile 2", "Zeile 1", null }, texts.Distinct(SameText.Comparer));
    }
}
