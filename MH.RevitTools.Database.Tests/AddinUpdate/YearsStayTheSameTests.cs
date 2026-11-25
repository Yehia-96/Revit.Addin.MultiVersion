using System;
using System.IO;

namespace MH.RevitTools.Database.Tests.AddinUpdate;

/// <summary>Check for Updates is one feature in three projects: the files differ in the year only.</summary>
public sealed class YearsStayTheSameTests
{
    private static string Read(string year, string file) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "AddinUpdate", year, file + ".txt"))
            .Replace("\r\n", "\n").Replace("_" + year, "_YEAR");

    [Theory]
    [InlineData("CheckPluginUpdate.cs", "2024")]
    [InlineData("CheckPluginUpdate.cs", "2026")]
    [InlineData("PluginUpdate.cs", "2024")]
    [InlineData("PluginUpdate.cs", "2026")]
    public void EveryYearHasTheSame(string file, string year) => Assert.Equal(Read("2027", file), Read(year, file));

    // Started when Revit closes, or an update never installs.
    [Theory]
    [InlineData("2024")]
    [InlineData("2026")]
    [InlineData("2027")]
    public void EveryYearStartsIt(string year) => Assert.Contains("CheckPluginUpdate.StartScheduledUpdate();", Read(year, "App.cs"));

    // 2024's project lists its files.
    [Fact]
    public void The2024Project_HasTheNewFile() => Assert.Contains(@"<Compile Include=""Services\PluginUpdate.cs"" />", Read("2024", "Revit.Addin.2024.csproj"));
}
