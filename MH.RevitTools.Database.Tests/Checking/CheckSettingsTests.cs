using System;
using System.IO;
using System.Linq;
using MH.RevitTools.Database.Checking;
using Newtonsoft.Json.Linq;

namespace MH.RevitTools.Database.Tests.Checking;

public sealed class CheckSettingsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "MH.RevitTools.Database.Tests", Guid.NewGuid().ToString("N"));

    private string SettingsPath => Path.Combine(_folder, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public void DefaultPath_IsInTheUsersRoamingFolder()
    {
        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MH", "RevitTools", "settings.json"),
            CheckSettings.DefaultPath);
    }

    [Fact]
    public void WithoutAFile_ItChecksEveryMinute()
    {
        CheckSettings settings = CheckSettings.Load(SettingsPath);

        Assert.Equal(60, settings.Interval.Seconds);
        Assert.Equal("every minute", settings.Interval.Phrase);
        Assert.True(settings.Interval.ChecksByItself);
    }

    [Fact]
    public void AChoice_IsSaved_AndReadBack_KeepingWhateverElseTheFileHolds()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(SettingsPath, "{ \"autoApply\": false }");

        Assert.True(new CheckSettings(300).TrySave(SettingsPath, out string problem), problem);

        Assert.Equal(300, CheckSettings.Load(SettingsPath).Interval.Seconds);
        JObject saved = JObject.Parse(File.ReadAllText(SettingsPath));
        Assert.False(saved.Value<bool>("autoApply"));
    }

    [Fact]
    public void OnlyWhenIAsk_IsAChoiceToo()
    {
        Directory.CreateDirectory(_folder);
        new CheckSettings(0).TrySave(SettingsPath, out _);

        CheckInterval interval = CheckSettings.Load(SettingsPath).Interval;

        Assert.False(interval.ChecksByItself);
        Assert.Equal("Only when I ask", interval.Label);
    }

    [Theory]
    [InlineData("{ \"checkEverySeconds\": 7 }")]
    [InlineData("{ \"checkEverySeconds\": \"often\" }")]
    [InlineData("{ broken")]
    [InlineData("[]")]
    public void AnythingElse_MeansEveryMinute(string json)
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(SettingsPath, json);

        Assert.Equal(60, CheckSettings.Load(SettingsPath).Interval.Seconds);
        Assert.Equal(60, new CheckSettings(7).Interval.Seconds);
    }

    [Fact]
    public void ThePane_OffersTheMockupsFourChoices()
    {
        Assert.Equal(new[] { "Every 30 seconds", "Every minute", "Every 5 minutes", "Only when I ask" },
            CheckInterval.All.Select(choice => choice.Label));
    }
}
