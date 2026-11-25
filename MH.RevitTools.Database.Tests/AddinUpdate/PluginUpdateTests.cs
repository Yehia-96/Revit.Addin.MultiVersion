using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Revit.Addin._2027.Services;

namespace MH.RevitTools.Database.Tests.AddinUpdate;

/// <summary>What Check for Updates finds, and what it copies, on folders of its own. A DLL's version is its text here.</summary>
public sealed class PluginUpdateTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MH.RevitTools.Database.Tests", "Müller+Hereth " + Guid.NewGuid().ToString("N"));
    private readonly PluginUpdatePaths _paths;

    public PluginUpdateTests()
    {
        _paths = Paths(_root);
        Directory.CreateDirectory(_paths.SharePayload);
        Directory.CreateDirectory(_paths.InstalledPayload);
        Publish("5.3.0.0");
        File.WriteAllText(_paths.InstalledDll, "5.2.2.0");
    }

    public void Dispose() => Remove(_root);

    // An antivirus program can have a fresh file open for a moment.
    internal static void Remove(string folder)
    {
        for (int tries = 1; ; tries++)
        {
            try
            {
                Directory.Delete(folder, recursive: true);
                return;
            }
            catch (Exception ex) when (tries < 20 && ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(250);
            }
        }
    }

    internal static PluginUpdatePaths Paths(string root) => new(
        "2027", Path.Combine(root, "Teams", "dependencies"), Path.Combine(root, "ProgramData", "2027"), Path.Combine(root, "AppData", "2027"),
        Path.Combine(root, "Local", "Update", "2027"));

    private void Publish(string version, string dllVersion = null)
    {
        File.WriteAllText(_paths.ShareVersionFile, version + "\r\n");
        File.WriteAllText(_paths.ShareDll, dllVersion ?? version);
        File.WriteAllText(_paths.ShareManifest, "manifest of " + version);
    }

    private PluginUpdate Update() => new(_paths, File.ReadAllText);

    [Fact]
    public void Paths_AreThoseOfTheScriptsAndTheInstallers()
    {
        PluginUpdatePaths real = PluginUpdatePaths.For("2026");

        Assert.EndsWith(@"\Müller+Hereth GmbH\31 BIM - Dokumente\General\01 Software\01 Revit-Tools\Updates\Versions\2026\dependencies", real.ShareFolder);
        Assert.Equal(Path.Combine(real.ShareFolder, "MH.RevitTools", "Revit.Addin.2026.dll"), real.ShareDll);
        Assert.Equal(@"C:\ProgramData\Autodesk\Revit\Addins\2026\MH.RevitTools\Revit.Addin.2026.dll", real.InstalledDll, ignoreCase: true);
        Assert.EndsWith(@"\AppData\Roaming\Autodesk\Revit\Addins\2026\Revit.Addin.2026.addin", real.UserManifest, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(@"\MH\RevitTools\Update\2026", real.StageFolder);
        Assert.NotEqual(real.Script, PluginUpdatePaths.For("2027").Script);
    }

    // Without one, a folder would be looked for, and cleared, wherever Revit happens to be.
    [Theory]
    [InlineData("", @"C:\b", @"C:\c", @"C:\d")]
    [InlineData(@"C:\a", "b", @"C:\c", @"C:\d")]
    [InlineData(@"C:\a", @"C:\b", "c", @"C:\d")]
    [InlineData(@"C:\a", @"C:\b", @"C:\c", @"MH\RevitTools")]
    public void Paths_AreWholeOnes(string share, string installed, string user, string stage) =>
        Assert.Throws<ArgumentException>(() => new PluginUpdatePaths("2027", share, installed, user, stage));

    [Fact]
    public void ANewerVersion_IsOffered()
    {
        PluginUpdateCheck check = Update().Check();

        Assert.Equal(PluginUpdateState.Newer, check.State);
        Assert.Equal(("5.2.2.0", "5.3.0.0"), (check.Installed, check.Published));
    }

    [Theory]
    [InlineData("5.2.2.0")]
    [InlineData("5.2.2")]
    [InlineData(" 5.2.2.0 ")]
    public void TheSameVersion_IsUpToDate(string published)
    {
        Publish(published, "5.2.2.0");

        Assert.Equal(PluginUpdateState.UpToDate, Update().Check().State);
    }

    // It used to be offered as an update: only the text was compared.
    [Theory]
    [InlineData("5.2.1.0")]
    [InlineData("4.9.9.9")]
    public void AnOlderVersion_IsNotOffered(string published)
    {
        Publish(published);

        PluginUpdateCheck check = Update().Check();

        Assert.Equal(PluginUpdateState.InstalledIsNewer, check.State);
        Assert.Contains("5.2.2.0", check.Text);
        Assert.Contains(published, check.Text);
    }

    // 5.10 comes after 5.9, which a comparison of texts gets wrong.
    [Fact]
    public void Versions_AreComparedAsNumbers()
    {
        File.WriteAllText(_paths.InstalledDll, "5.9.0.0");
        Publish("5.10.0.0");

        Assert.Equal(PluginUpdateState.Newer, Update().Check().State);
    }

    // It goes into the script, where a line of its own would be a command.
    [Fact]
    public void AVersion_IsItsNumbersOnly()
    {
        Publish("5.3.0\r\n.1", "5.3.0.1");

        PluginUpdateCheck check = Update().Check();

        Assert.Equal((PluginUpdateState.Newer, "5.3.0.1"), (check.State, check.Published));
        Assert.Null(Update().Prepare(check));
        Assert.DoesNotContain(File.ReadAllLines(_paths.Script), line => line.StartsWith('.'));
    }

    // As Windows reads it from a real DLL; the other tests take its text.
    [Theory]
    [InlineData("0.9.0.0", PluginUpdateState.InstalledIsNewer)]
    [InlineData("1.0.0.0", PluginUpdateState.UpToDate)]
    [InlineData("1.1.0.0", PluginUpdateState.NotUsable)]
    public void AVersion_IsReadFromTheDll(string published, PluginUpdateState expected)
    {
        string library = typeof(MH.RevitTools.Database.Changes.ChangeReport).Assembly.Location;
        Assert.Equal("1.0.0.0", FileVersionInfo.GetVersionInfo(library).FileVersion);
        Publish(published);
        File.Copy(library, _paths.InstalledDll, overwrite: true);
        File.Copy(library, _paths.ShareDll, overwrite: true);

        Assert.Equal(expected, new PluginUpdate(_paths).Check().State);
    }

    [Fact]
    public void WithoutTheTeamsFolder_ItSaysSo()
    {
        Directory.Delete(_paths.ShareFolder, recursive: true);

        Assert.Equal(PluginUpdateState.NoTeamsFolder, Update().Check().State);
    }

    [Fact]
    public void WithoutAVersionFile_NothingIsOffered()
    {
        File.Delete(_paths.ShareVersionFile);

        PluginUpdateCheck check = Update().Check();

        Assert.Equal(PluginUpdateState.NotUsable, check.State);
        Assert.Contains("version.txt", check.Text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("new")]
    public void AVersionFileWithoutAVersion_IsNamed(string content)
    {
        File.WriteAllText(_paths.ShareVersionFile, content);

        PluginUpdateCheck check = Update().Check();

        Assert.Equal(PluginUpdateState.NotUsable, check.State);
        Assert.Contains("\"" + content + "\"", check.Text);
    }

    [Fact]
    public void AVersionFileWithoutAVersion_IsntShownWhole()
    {
        File.WriteAllText(_paths.ShareVersionFile, new string('x', 5000));

        Assert.InRange(Update().Check().Text.Length, 1, 200);
    }

    // Only in the cloud, and OneDrive isn't running, for instance.
    [Fact]
    public void ATeamsFolderThatCantBeRead_IsNotOffered()
    {
        using (new FileStream(_paths.ShareVersionFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            PluginUpdateCheck check = Update().Check();

            Assert.Equal(PluginUpdateState.NotUsable, check.State);
            Assert.Contains("OneDrive", check.Text);
        }
    }

    // As cmd holds it as long as the script is at work: for one that waits for this very Revit, for instance.
    private FileStream AtWork()
    {
        Directory.CreateDirectory(_paths.StageFolder);
        return new FileStream(_paths.LockFile, FileMode.Create, FileAccess.Write, FileShare.Read);
    }

    [Fact]
    public void WhileAnUpdateIsWaiting_NoneIsOffered()
    {
        using (AtWork())
        {
            PluginUpdateCheck check = Update().Check();

            Assert.Equal(PluginUpdateState.Waiting, check.State);
            Assert.Contains("Close every Revit 2027 window", check.Text);
        }
        Assert.Equal(PluginUpdateState.Newer, Update().Check().State);
    }

    // The question was open while it started.
    [Fact]
    public void WhileAnUpdateIsWaiting_ItsFilesStay()
    {
        PluginUpdateCheck check = Update().Check();
        Assert.Null(Update().Prepare(check));
        using (AtWork())
            Assert.Contains("Close every Revit 2027 window", Update().Prepare(check));

        Assert.True(File.Exists(_paths.Script));
        Assert.True(File.Exists(Path.Combine(_paths.StagedPayload, "Revit.Addin.2027.dll")));
    }

    // version.txt can arrive before the DLLs do.
    [Theory]
    [InlineData("5.2.2.0")]
    [InlineData(null)]
    public void ATeamsFolderThatIsntSynchronizedYet_IsNotOffered(string dllVersion)
    {
        if (dllVersion == null)
            File.Delete(_paths.ShareDll);
        else
            File.WriteAllText(_paths.ShareDll, dllVersion);

        PluginUpdateCheck check = Update().Check();

        Assert.Equal(PluginUpdateState.NotUsable, check.State);
        Assert.Contains("synchronized", check.Text);
    }

    [Fact]
    public void WithoutTheManifestThere_NothingIsOffered()
    {
        File.Delete(_paths.ShareManifest);

        Assert.Equal(PluginUpdateState.NotUsable, Update().Check().State);
    }

    [Fact]
    public void WithoutAnInstalledAddin_ItSaysWhereItLooked()
    {
        File.Delete(_paths.InstalledDll);

        PluginUpdateCheck check = Update().Check();

        Assert.Equal(PluginUpdateState.NotUsable, check.State);
        Assert.Contains(_paths.InstalledDll, check.Text);
    }

    // The whole payload: deps.json, native files, languages. Only what nobody needs stays behind.
    [Fact]
    public void Prepare_CopiesEverything_AndWritesTheScript()
    {
        Directory.CreateDirectory(Path.Combine(_paths.SharePayload, "runtimes", "win-x64"));
        File.WriteAllText(Path.Combine(_paths.SharePayload, "runtimes", "win-x64", "native.dll"), "native");
        File.WriteAllText(Path.Combine(_paths.SharePayload, "Revit.Addin.2027.deps.json"), "{}");
        File.WriteAllText(Path.Combine(_paths.SharePayload, "Revit.Addin.2027.pdb"), "symbols");
        File.WriteAllText(Path.Combine(_paths.SharePayload, "ClosedXML.xml"), "documentation");
        File.SetLastWriteTimeUtc(_paths.ShareDll, new DateTime(2026, 11, 20, 8, 0, 0, DateTimeKind.Utc));
        DateTime before = DateTime.UtcNow.AddSeconds(-2);

        string problem = Update().Prepare(Update().Check());

        Assert.Null(problem);
        Assert.Equal(new[] { "Revit.Addin.2027.deps.json", "Revit.Addin.2027.dll", @"runtimes\win-x64\native.dll" },
            Directory.GetFiles(_paths.StagedPayload, "*", SearchOption.AllDirectories).Select(file => Path.GetRelativePath(_paths.StagedPayload, file)).OrderBy(name => name, StringComparer.Ordinal));
        // A time of its own, so that robocopy can't take an installed file of the same size and time for the same one.
        Assert.True(File.GetLastWriteTimeUtc(Path.Combine(_paths.StagedPayload, "Revit.Addin.2027.dll")) > before);
        Assert.Equal("manifest of 5.3.0.0", File.ReadAllText(_paths.StagedManifest));
        Assert.True(File.Exists(_paths.Script));
        Assert.Contains("5.3.0.0", File.ReadAllText(_paths.Script));
        // What the script counts: the three files.
        Assert.Contains("LSS 3 goto incomplete", File.ReadAllText(_paths.Script));
    }

    [Fact]
    public void Prepare_LeavesNothingOfAnEarlierOne()
    {
        Directory.CreateDirectory(_paths.StagedPayload);
        File.WriteAllText(Path.Combine(_paths.StagedPayload, "old.dll"), "old");

        Assert.Null(Update().Prepare(Update().Check()));

        Assert.False(File.Exists(Path.Combine(_paths.StagedPayload, "old.dll")));
    }

    // A window of the last one that's still open, for instance.
    [Fact]
    public void Prepare_WorksWhileAProgramIsInTheFolder()
    {
        Assert.Null(Update().Prepare(Update().Check()));
        using Process window = Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 30 127.0.0.1") { WorkingDirectory = _paths.StageFolder, CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true });
        try
        {
            Assert.Null(Update().Prepare(Update().Check()));
        }
        finally
        {
            window.Kill(entireProcessTree: true);
            window.WaitForExit();
        }
    }

    // The Mark of the Web, with which .NET Framework refuses to load a DLL.
    [Fact]
    public void Prepare_LeavesTheMarkOfTheWebBehind()
    {
        File.WriteAllText(_paths.ShareDll + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3");

        Assert.Null(Update().Prepare(Update().Check()));

        Assert.False(File.Exists(Path.Combine(_paths.StagedPayload, "Revit.Addin.2027.dll") + ":Zone.Identifier"));
    }

    [Fact]
    public void Prepare_SaysWhichFileCantBeCopied()
    {
        string held = Path.Combine(_paths.SharePayload, "held.dll");
        File.WriteAllText(held, "x");
        PluginUpdateCheck check = Update().Check();
        using (new FileStream(held, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            string problem = Update().Prepare(check);

            Assert.Contains("held.dll", problem);
            Assert.Contains("OneDrive", problem);
        }
        Assert.False(File.Exists(_paths.Script));
    }

    // Published anew while it was being copied.
    [Fact]
    public void Prepare_ChecksWhatItCopied()
    {
        PluginUpdateCheck check = Update().Check();
        File.WriteAllText(_paths.ShareDll, "5.4.0.0");

        Assert.Contains("changed", Update().Prepare(check));
        Assert.False(File.Exists(_paths.Script));
    }

    [Fact]
    public void FilesThisAccountMayReplace_NeedNoAdministrator()
    {
        Assert.Null(Update().Prepare(Update().Check()));
        using (new FileStream(_paths.InstalledDll, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.False(Update().NeedsAdministrator());
        Assert.Equal("5.2.2.0", File.ReadAllText(_paths.InstalledDll));
        Assert.Single(Directory.GetFiles(_paths.InstalledPayload));
    }

    // Read-only stands in for a file an administrator owns: Windows answers the same.
    [Fact]
    public void FilesThisAccountMayNotReplace_NeedAnAdministrator()
    {
        Assert.Null(Update().Prepare(Update().Check()));
        File.SetAttributes(_paths.InstalledDll, FileAttributes.ReadOnly);
        try
        {
            Assert.True(Update().NeedsAdministrator());
        }
        finally
        {
            File.SetAttributes(_paths.InstalledDll, FileAttributes.Normal);
        }
    }
}
