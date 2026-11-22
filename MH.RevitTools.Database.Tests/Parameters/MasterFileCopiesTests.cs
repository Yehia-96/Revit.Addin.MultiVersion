using System;
using System.IO;
using System.Text;
using MH.RevitTools.Database.Parameters;

namespace MH.RevitTools.Database.Tests.Parameters;

public sealed class MasterFileCopiesTests : IDisposable
{
    private readonly string _folder =
        Path.Combine(Path.GetTempPath(), "MH.RevitTools.Database.Tests", Guid.NewGuid().ToString("N"));

    public MasterFileCopiesTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void PathFor_GivesEachRevitItsOwnCopy()
    {
        Assert.Equal(Path.Combine(_folder, "MH_Parameter.12345.txt"), MasterFileCopies.PathFor(_folder, 12345));
        Assert.NotEqual(MasterFileCopies.PathFor(_folder, 1), MasterFileCopies.PathFor(_folder, 2));
    }

    [Fact]
    public void RemoveOld_DeletesOnlyOldCopies_AndNothingElse()
    {
        DateTime now = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);
        string old = Write("MH_Parameter.1.txt", now.AddDays(-2));
        string fresh = Write("MH_Parameter.2.txt", now.AddHours(-1));
        string other = Write("database.json", now.AddDays(-30));

        int removed = MasterFileCopies.RemoveOld(_folder, now, TimeSpan.FromDays(1));

        Assert.Equal(1, removed);
        Assert.False(File.Exists(old));
        Assert.True(File.Exists(fresh));
        Assert.True(File.Exists(other));
    }

    [Fact]
    public void RemoveOld_WithoutTheFolder_DoesNothing()
    {
        Assert.Equal(0, MasterFileCopies.RemoveOld(Path.Combine(_folder, "missing"), DateTime.UtcNow, TimeSpan.FromDays(1)));
    }

    [Fact]
    public void CopyAndRead_WhenTheConnectionFileNamesTheCopyItself_ReadsItWithoutEmptyingIt()
    {
        Guid dicke = Guid.NewGuid();
        string path = Path.Combine(_folder, "MH_Parameter.7.txt");
        File.WriteAllText(path, MasterFileText.With((dicke, "Dicke", "LENGTH")), Encoding.Unicode);

        MasterParameterFile file = MasterParameterFile.CopyAndRead(path, path);

        Assert.True(file.IsRead, file.Problem);
        Assert.True(file.Holds(dicke));
    }

    private string Write(string name, DateTime writtenUtc)
    {
        string path = Path.Combine(_folder, name);
        File.WriteAllText(path, "x");
        File.SetLastWriteTimeUtc(path, writtenUtc);
        return path;
    }
}
