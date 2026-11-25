using System;
using System.IO;
using MH.RevitTools.Database.Tests.AddinUpdate;
using Revit.Addin._2027.Services.Planungsdatenbank;

namespace MH.RevitTools.Database.Tests.ExcelImport;

/// <summary>The workbook's copy that's read, and whether the workbook was saved again since. Without Excel: any file will do.</summary>
public sealed class WorkbookFileTests : IDisposable
{
    private static readonly DateTime Saved = new(2026, 11, 25, 9, 48, 0, DateTimeKind.Utc);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "MH.RevitTools.Database.Tests", Guid.NewGuid().ToString("N"));
    private readonly string _workbook;

    public WorkbookFileTests()
    {
        Directory.CreateDirectory(_folder);
        _workbook = Path.Combine(_folder, "Planungsdatenbank.xlsx");
        Save("1761", Saved);
    }

    public void Dispose() => PluginUpdateTests.Remove(_folder);

    private void Save(string content, DateTime whenUtc)
    {
        File.WriteAllText(_workbook, content);
        File.SetLastWriteTimeUtc(_workbook, whenUtc);
    }

    private WorkbookFile Read() => WorkbookFile.Read(_workbook, _ => { });

    [Fact]
    public void Read_GivesACopy_WithTheSameExtension_AndDeletesItAfterwards()
    {
        string copy = null, content = null;

        WorkbookFile file = WorkbookFile.Read(_workbook, path =>
        {
            copy = path;
            content = File.ReadAllText(path);
        });

        Assert.Equal("1761", content);
        Assert.NotEqual(_workbook, copy);
        Assert.Equal(".xlsx", Path.GetExtension(copy));
        Assert.False(File.Exists(copy));
        Assert.Equal(_workbook, file.FullPath);
    }

    // Excel keeps a workbook it has open for itself to write; others may read it, if they let Excel write.
    [Fact]
    public void Read_Works_WhileExcelHasItOpen()
    {
        using var excel = new FileStream(_workbook, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        string content = null;

        Assert.Throws<IOException>(() => new FileStream(_workbook, FileMode.Open, FileAccess.Read, FileShare.Read));
        WorkbookFile.Read(_workbook, path => content = File.ReadAllText(path));

        Assert.Equal("1761", content);
    }

    [Fact]
    public void Read_PassesOnWhatTheReaderThrows_AndDeletesTheCopy()
    {
        string copy = null;

        var ex = Assert.Throws<InvalidDataException>(() => WorkbookFile.Read(_workbook, path =>
        {
            copy = path;
            throw new InvalidDataException("no such sheet");
        }));

        Assert.Equal("no such sheet", ex.Message);
        Assert.False(File.Exists(copy));
    }

    [Fact]
    public void Read_Throws_WhenThereIsNoSuchFile()
    {
        string gone = Path.Combine(_folder, "Gone.xlsx");

        var ex = Assert.Throws<FileNotFoundException>(() => WorkbookFile.Read(gone, _ => Assert.Fail("read")));

        Assert.Equal(gone, ex.FileName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public void Read_Throws_WithoutAPath(string path) => Assert.Throws<ArgumentException>(() => WorkbookFile.Read(path, _ => { }));

    [Fact]
    public void HasChanged_IsFalse_WhileNobodySavesIt()
    {
        WorkbookFile file = Read();

        Assert.False(file.HasChanged());
        Assert.False(file.HasChanged());
    }

    // The report of 25.11.2026: a value changed in the Excel, and the tool run again.
    [Fact]
    public void HasChanged_IsTrue_OnceItsSavedAgain()
    {
        WorkbookFile file = Read();

        Save("1800", Saved.AddMinutes(2));

        Assert.True(file.HasChanged());
        Assert.False(Read().HasChanged());
    }

    [Fact]
    public void HasChanged_IsTrue_WhenOnlyTheSizeDiffers()
    {
        WorkbookFile file = Read();

        Save("1761-01", Saved);

        Assert.True(file.HasChanged());
    }

    // An older copy put back over it, from a backup say.
    [Fact]
    public void HasChanged_IsTrue_WhenItsOlder()
    {
        WorkbookFile file = Read();

        Save("1760", Saved.AddDays(-1));

        Assert.True(file.HasChanged());
    }

    // Excel writes a new file and puts it in the old one's place.
    [Fact]
    public void HasChanged_IsTrue_WhenAnotherFileIsInItsPlace()
    {
        WorkbookFile file = Read();
        string other = Path.Combine(_folder, "A1B2C3.tmp");
        File.WriteAllText(other, "1800");

        File.Move(other, _workbook, overwrite: true);

        Assert.True(file.HasChanged());
    }

    [Fact]
    public void HasChanged_IsTrue_WhenItWasSavedWhileItWasRead()
    {
        WorkbookFile file = WorkbookFile.Read(_workbook, _ => Save("1800", Saved.AddMinutes(2)));

        Assert.True(file.HasChanged());
    }

    [Fact]
    public void HasChanged_IsFalse_WhenItsGone()
    {
        WorkbookFile file = Read();

        File.Delete(_workbook);

        Assert.False(file.HasChanged());
    }

    [Fact]
    public void Description_SaysItsName_AndWhenItWasSaved() =>
        Assert.Equal("Planungsdatenbank.xlsx, saved " + Saved.ToLocalTime().ToString("g"), Read().Description);
}
