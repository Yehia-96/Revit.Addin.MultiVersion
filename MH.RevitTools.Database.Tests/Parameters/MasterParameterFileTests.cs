using System;
using System.IO;
using System.Text;
using MH.RevitTools.Database.Parameters;

namespace MH.RevitTools.Database.Tests.Parameters;

public sealed class MasterParameterFileTests : IDisposable
{
    private static readonly Guid Dicke = new("3f1c2a6e-8b4d-4c1e-9a7f-61d1c4e0a061");
    private static readonly Guid Breite = new("0c5e9b7a-2d3f-4a8b-b1c6-140d2e3f4a5b");

    private readonly string _folder =
        Path.Combine(Path.GetTempPath(), "MH.RevitTools.Database.Tests", Guid.NewGuid().ToString("N"));

    public MasterParameterFileTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void Read_GivesEachParameterByGuid_WithItsNameTypeAndGroup()
    {
        MasterParameterFile file = MasterFileText.Read(MasterFileText.With((Dicke, "Dicke", "LENGTH"), (Breite, "Breite", "LENGTH")));

        Assert.True(file.IsRead);
        Assert.Null(file.Problem);
        Assert.Equal(2, file.Parameters.Count);
        Assert.True(file.Holds(Dicke));
        MasterFileParameter dicke = file.Parameters[Dicke];
        Assert.Equal("Dicke", dicke.Name);
        Assert.Equal("LENGTH", dicke.DataType);
        Assert.Equal("BAU-GEO Bauteilinformation - Geometrie", dicke.Group);
        Assert.Equal("MH_Parameter.txt", file.FileName);
        Assert.Equal(MasterFileText.WrittenAtUtc, file.WrittenAtUtc);
    }

    [Fact]
    public void Read_TakesTheColumnsFromTheHeader_InAnyOrder()
    {
        string text = "*META\tVERSION\tMINVERSION\nMETA\t2\t1\n*PARAM\tNAME\tGROUP\tDATATYPE\tGUID\n"
            + "PARAM\tDicke\t1\tLENGTH\t" + Dicke + "\n";

        MasterParameterFile file = MasterFileText.Read(text);

        Assert.True(file.IsRead);
        Assert.Equal("Dicke", file.Parameters[Dicke].Name);
    }

    [Theory]
    [InlineData("", "no META line")]
    [InlineData("Dicke;LENGTH\n", "no META line")]
    [InlineData("META\t2\t1\nPARAM\tx\n", "before the *PARAM line")]
    [InlineData("META\t2\t1\n*PARAM\tNAME\tDATATYPE\n", "without the GUID")]
    [InlineData("META\t2\t1\n" + MasterFileText.Header + "\nPARAM\tnot-a-guid\tDicke\tLENGTH\t\t1\t1\t\t1\t0\n", "without a valid GUID")]
    [InlineData("META\t2\t1\n" + MasterFileText.Header + "\nPARAM\t" + "3f1c2a6e-8b4d-4c1e-9a7f-61d1c4e0a061\n", "too few columns")]
    public void Read_SaysWhatsWrong_WithABrokenFile(string text, string expected)
    {
        MasterParameterFile file = MasterFileText.Read(text);

        Assert.False(file.IsRead);
        Assert.Contains(expected, file.Problem);
        Assert.Contains(@"C:\share\MH_Parameter.txt", file.Problem);
        Assert.Empty(file.Parameters);
    }

    [Fact]
    public void Read_SaysWhichGuidComesTwice()
    {
        MasterParameterFile file = MasterFileText.Read(MasterFileText.With((Dicke, "Dicke", "LENGTH"), (Dicke, "Dicke 2", "TEXT")));

        Assert.False(file.IsRead);
        Assert.Contains("twice", file.Problem);
        Assert.Contains(Dicke.ToString("D"), file.Problem);
    }

    [Fact]
    public void CopyAndRead_CopiesStudiosUtf16File_AndReadsTheCopy()
    {
        string source = Path.Combine(_folder, "share", "MH_Parameter.txt");
        string copy = Path.Combine(_folder, "local", "MH_Parameter.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(source));
        File.WriteAllText(source, MasterFileText.With((Dicke, "Dicke Größe", "LENGTH")), Encoding.Unicode);

        MasterParameterFile file = MasterParameterFile.CopyAndRead(source, copy);

        Assert.True(file.IsRead, file.Problem);
        Assert.Equal("Dicke Größe", file.Parameters[Dicke].Name);
        Assert.Equal(source, file.Path);
        Assert.Equal(File.GetLastWriteTimeUtc(source), file.WrittenAtUtc);
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(copy));
    }

    [Fact]
    public void CopyAndRead_ReadsAFileStudioHoldsOpen()
    {
        string source = Path.Combine(_folder, "MH_Parameter.txt");
        File.WriteAllText(source, MasterFileText.With((Dicke, "Dicke", "LENGTH")), Encoding.Unicode);

        using (new FileStream(source, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete))
        {
            MasterParameterFile file = MasterParameterFile.CopyAndRead(source, Path.Combine(_folder, "copy.txt"));

            Assert.True(file.IsRead, file.Problem);
        }
    }

    [Fact]
    public void CopyAndRead_MissingFile_NamesItAndSaysWhoWritesIt()
    {
        string source = Path.Combine(_folder, "nowhere", "MH_Parameter.txt");

        MasterParameterFile file = MasterParameterFile.CopyAndRead(source, Path.Combine(_folder, "copy.txt"));

        Assert.False(file.IsRead);
        Assert.Contains(source, file.Problem);
        Assert.Contains("Studio writes it", file.Problem);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public void CopyAndRead_WithoutAPath_PointsAtTheConnectionFile(string path)
    {
        MasterParameterFile file = MasterParameterFile.CopyAndRead(path, Path.Combine(_folder, "copy.txt"));

        Assert.False(file.IsRead);
        Assert.Contains("\"masterParameterFile\"", file.Problem);
        Assert.Equal("MH_Parameter.txt", file.FileName);
    }
}
