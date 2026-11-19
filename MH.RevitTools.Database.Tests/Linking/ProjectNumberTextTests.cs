using MH.RevitTools.Database.Linking;

namespace MH.RevitTools.Database.Tests.Linking;

public sealed class ProjectNumberTextTests
{
    [Theory]
    [InlineData("1761-01", 1761, 1)]
    [InlineData("1761-1", 1761, 1)]
    [InlineData("  1761 - 01  ", 1761, 1)]
    [InlineData("1761/01", 1761, 1)]
    [InlineData("1761–01", 1761, 1)] // an en dash, as Word writes it
    [InlineData("1547-13", 1547, 13)]
    [InlineData("1761-01 Ulm", 1761, 1)]
    [InlineData("1761-01-A", 1761, 1)]
    [InlineData("1761-01\n", 1761, 1)]
    [InlineData("1761-1 2", 1761, 1)]
    [InlineData("1761-00", 1761, 0)]
    public void ReadsTheNumberAndTheSubNumber(string text, int number, int subNumber)
    {
        ProjectNumberText read = ProjectNumberText.Parse(text);

        Assert.True(read.IsReadable);
        Assert.Equal(number, read.Number);
        Assert.Equal(subNumber, read.SubNumber);
    }

    [Theory]
    [InlineData("1761")]
    [InlineData(" 1761 ")]
    [InlineData("1761 Ulm")]
    [InlineData("1761 - Ulm")]
    [InlineData("1761-Ulm")]
    [InlineData("1761-")]
    public void ReadsANumberWithoutASubNumber(string text)
    {
        ProjectNumberText read = ProjectNumberText.Parse(text);

        Assert.True(read.IsReadable);
        Assert.Equal(1761, read.Number);
        Assert.Null(read.SubNumber);
    }

    [Theory]
    [InlineData("17610")]
    [InlineData("176")]
    [InlineData("P-1761")]
    [InlineData("1761-012")]
    [InlineData("1761.01")] // a sub-number written another way, or not? Better no suggestion than the wrong one.
    [InlineData("1761_01")]
    [InlineData("1761 01")]
    [InlineData("Projektnummer")]
    [InlineData("１７６１")] // full-width digits: not what models write
    public void SaysWhenTheTextIsNoProjectNumber(string text)
    {
        ProjectNumberText read = ProjectNumberText.Parse(text);

        Assert.False(read.IsEmpty);
        Assert.False(read.IsReadable);
        Assert.Null(read.SubNumber);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SaysWhenThereIsNone(string text)
    {
        ProjectNumberText read = ProjectNumberText.Parse(text);

        Assert.True(read.IsEmpty);
        Assert.False(read.IsReadable);
        Assert.Equal("", read.Text);
    }

    [Fact]
    public void KeepsTheTextAsWritten_Trimmed()
    {
        Assert.Equal("1761-01 Ulm", ProjectNumberText.Parse("  1761-01 Ulm ").Text);
    }
}
