using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MH.RevitTools.Database.Tests.ExcelImport;

/// <summary>The Excel import is one feature in three projects: its shared files differ in the year only.</summary>
public sealed class ExcelImportYearsTests
{
    private static readonly string[] Years = { "2024", "2026", "2027" };

    private static string Folder(string year) => Path.Combine(AppContext.BaseDirectory, "ExcelImport", year);

    private static string Read(string year, string file) =>
        File.ReadAllText(Path.Combine(Folder(year), file)).Replace("\r\n", "\n").Replace("_" + year, "_YEAR");

    public static IEnumerable<object[]> Files() =>
        from year in new[] { "2024", "2026" }
        from file in Directory.GetFiles(Folder("2027"), "*.cs.txt").Select(Path.GetFileName)
        select new object[] { year, file };

    [Theory]
    [MemberData(nameof(Files))]
    public void EveryYearHasTheSame(string year, string file) => Assert.Equal(Read("2027", file), Read(year, file));

    // So that the test above can't pass on nothing.
    [Theory]
    [InlineData("ProjectParameterBinder.cs.txt")]
    [InlineData("SharedParameterSeeder.cs.txt")]
    [InlineData("SeedMaterialParameters.cs.txt")]
    [InlineData("ParameterRun.cs.txt")]
    [InlineData("SharedParameterNames.cs.txt")]
    [InlineData("WorkbookFile.cs.txt")]
    [InlineData("WorkbookLoader.cs.txt")]
    [InlineData("PlanungsdatenbankParser.cs.txt")]
    public void TheFileIsAmongThem(string file) => Assert.Contains(file, Directory.GetFiles(Folder("2027")).Select(Path.GetFileName));

    // 2024's project lists its files.
    [Theory]
    [InlineData(@"Helpers\ProjectParameterBinder.cs")]
    [InlineData(@"Services\ParameterRun.cs")]
    [InlineData(@"Services\SharedParameterNames.cs")]
    [InlineData(@"Services\Planungsdatenbank\WorkbookFile.cs")]
    public void The2024Project_HasTheFile(string file) =>
        Assert.Contains("<Compile Include=\"" + file + "\" />", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "AddinUpdate", "2024", "Revit.Addin.2024.csproj.txt")));

    // Revit's ReInsert takes a parameter, with its values, off the categories it isn't given. One place calls it, and gives it all.
    [Theory]
    [InlineData("SeedMaterialParameters.cs.txt")]
    [InlineData(@"Own\SeedProjectElementsParameters.cs.txt")]
    public void OnlyTheBinder_BindsParameters(string file)
    {
        foreach (string year in Years)
        {
            string text = Read(year, file.Replace('\\', Path.DirectorySeparatorChar));
            Assert.DoesNotContain("Insert(def", text);
            Assert.Contains("ProjectParameterBinder.Add(", text);
        }
    }
}
