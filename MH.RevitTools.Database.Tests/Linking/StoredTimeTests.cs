using System;
using MH.RevitTools.Database.Linking;

namespace MH.RevitTools.Database.Tests.Linking;

public sealed class StoredTimeTests
{
    [Fact]
    public void KeepsUtcAsIsoText_AndReadsItBack()
    {
        var time = new DateTime(2026, 11, 19, 14, 32, 5, DateTimeKind.Utc);

        Assert.Equal("2026-11-19T14:32:05Z", StoredTime.ToText(time));
        DateTime? read = StoredTime.FromText("2026-11-19T14:32:05Z");
        Assert.Equal(time, read);
        Assert.Equal(DateTimeKind.Utc, read.Value.Kind);
    }

    [Fact]
    public void StoresALocalTimeAsUtc()
    {
        DateTime local = new(2026, 11, 19, 15, 32, 5, DateTimeKind.Local);

        Assert.Equal(local.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"), StoredTime.ToText(local));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("19.11.2026 14:32")]
    [InlineData("2026-11-19")]
    public void ReadsNothingFromEmptyOrForeignText(string text)
    {
        Assert.Null(StoredTime.FromText(text));
    }

    [Fact]
    public void StoresNothingForAnUnknownTime()
    {
        Assert.Equal("", StoredTime.ToText(null));
    }
}
