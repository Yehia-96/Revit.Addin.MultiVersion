using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Revit.Addin._2027.Services;

namespace MH.RevitTools.Database.Tests.AddinUpdate;

/// <summary>
/// The script that installs an update once Revit has closed, run for real on folders of its own: a DLL is a text
/// file here, and one that's in use is held open as a running Revit holds it.
/// </summary>
public sealed class PluginUpdateScriptTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MH.RevitTools.Database.Tests", "Müller+Hereth & Söhne 100% " + Guid.NewGuid().ToString("N"));
    private readonly PluginUpdatePaths _paths;
    private readonly int _files;

    public PluginUpdateScriptTests()
    {
        _paths = PluginUpdateTests.Paths(_root);
        Directory.CreateDirectory(_paths.SharePayload);
        Directory.CreateDirectory(Path.Combine(_paths.SharePayload, "runtimes"));
        Directory.CreateDirectory(_paths.InstalledPayload);
        Directory.CreateDirectory(Path.GetDirectoryName(_paths.UserManifest));

        foreach (string name in new[] { "A.dll", "LeftOver.dll", "MH.RevitTools.Database.dll", "Newtonsoft.Json.dll", "Z.dll" })
            Install(Installed(name), "old");
        Install(_paths.InstalledDll, "5.2.2.0");
        Install(_paths.InstalledManifest, "old manifest");
        Install(_paths.UserManifest, "old manifest");

        foreach (string name in new[] { "A.dll", "MH.RevitTools.Database.dll", "NewInThisVersion.dll", "Newtonsoft.Json.dll", "Z.dll", "Revit.Addin.2027.deps.json", @"runtimes\native.dll" })
            Put(_paths.SharePayload, name, "new");
        Put(_paths.SharePayload, "Revit.Addin.2027.dll", "5.3.0.0");
        File.WriteAllText(_paths.ShareVersionFile, "5.3.0.0");
        File.WriteAllText(_paths.ShareManifest, "new manifest");

        var update = new PluginUpdate(_paths, File.ReadAllText);
        Assert.Null(update.Prepare(update.Check()));
        _files = Directory.GetFiles(_paths.StagedPayload, "*", SearchOption.AllDirectories).Length;
        Assert.Equal(8, _files);
    }

    public void Dispose() => PluginUpdateTests.Remove(_root);

    private static void Put(string folder, string name, string content) => File.WriteAllText(Path.Combine(folder, name), content);

    // Some weeks ago, as it was. Within the same moment, robocopy would take a file of the same size for the same one.
    private static void Install(string file, string content)
    {
        File.WriteAllText(file, content);
        File.SetLastWriteTimeUtc(file, new DateTime(2026, 11, 2, 8, 0, 0, DateTimeKind.Utc));
    }

    private static FileStream Use(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.Read);

    private string Installed(string name) => Path.Combine(_paths.InstalledPayload, name);

    private string State() => string.Join(", ", Directory.GetFiles(_paths.InstalledPayload, "*", SearchOption.AllDirectories)
        .OrderBy(file => file, StringComparer.Ordinal).Select(file => Path.GetRelativePath(_paths.InstalledPayload, file) + "=" + File.ReadAllText(file)))
        + "; " + File.ReadAllText(Path.Combine(_paths.InstalledRoot, "Revit.Addin.2027.addin")) + "; " + File.ReadAllText(_paths.UserManifest);

    private const string Before = "A.dll=old, LeftOver.dll=old, MH.RevitTools.Database.dll=old, Newtonsoft.Json.dll=old, Revit.Addin.2027.dll=5.2.2.0, Z.dll=old; old manifest; old manifest";

    private const string After = "A.dll=new, LeftOver.dll=old, MH.RevitTools.Database.dll=new, NewInThisVersion.dll=new, Newtonsoft.Json.dll=new, Revit.Addin.2027.deps.json=new, "
        + @"Revit.Addin.2027.dll=5.3.0.0, Z.dll=new, runtimes\native.dll=new; new manifest; new manifest";

    // Not running on any PC, so that a Revit that's running on this one doesn't count.
    private const string NoProgram = "no-such-program.exe";

    // As it's written, but for how long it waits, and for what.
    private void Write(int tries = 2, string program = NoProgram, Func<string, string> change = null)
    {
        string text = PluginUpdateScript.Text(_paths, "5.3.0.0", _files, tries, program);
        File.WriteAllText(_paths.Script, change == null ? text : change(text), new UTF8Encoding(false));
    }

    /// <summary>The script at work, in a window that isn't shown.</summary>
    private sealed class Window : IDisposable
    {
        private readonly Process _process;
        private readonly StringBuilder _said = new();

        public Window(PluginUpdatePaths paths, string path = null)
        {
            var start = new ProcessStartInfo("cmd.exe", "/c \"\"" + paths.Script + "\"\"")
            {
                UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, WorkingDirectory = paths.StageFolder, CreateNoWindow = true,
            };
            if (path != null)
                start.Environment["PATH"] = path + ";" + start.Environment["PATH"];
            _process = Process.Start(start);
            DataReceivedEventHandler hear = (_, line) => { lock (_said) _said.AppendLine(line.Data); };
            _process.OutputDataReceived += hear;
            _process.ErrorDataReceived += hear;
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
        }

        public string Said
        {
            get { lock (_said) return _said.ToString(); }
        }

        public void PressAKey()
        {
            _process.StandardInput.Write(" ");
            _process.StandardInput.Close();
        }

        public int End()
        {
            Assert.True(_process.WaitForExit(60_000), "The script didn't end. It said: " + Said);
            _process.WaitForExit();
            return _process.ExitCode;
        }

        public void Dispose()
        {
            if (!_process.HasExited)
                _process.Kill(entireProcessTree: true);
            _process.Dispose();
        }
    }

    private (int Code, string Said) Run(int tries = 2, string program = NoProgram, Func<string, string> change = null, string path = null)
    {
        Write(tries, program, change);
        using var window = new Window(_paths, path);
        window.PressAKey();
        int code = window.End();
        return (code, window.Said);
    }

    private static async Task Until(Func<bool> condition, string what)
    {
        for (int tries = 0; tries < 300; tries++)
        {
            if (condition())
                return;
            await Task.Delay(100);
        }
        Assert.Fail("It took too long: " + what);
    }

    [Fact]
    public void WithRevitClosed_EverythingArrives_AndItSaysSo()
    {
        Assert.Equal(Before, State());

        (int code, string said) = Run();

        Assert.Equal(PluginUpdateScript.Installed, code);
        Assert.Equal(After, State());
        Assert.Contains("Updated to 5.3.0.0.", said);
        Assert.DoesNotContain("NOT UPDATED", said);
        Assert.False(Directory.Exists(_paths.StagedPayload));
        Assert.False(Directory.Exists(_paths.Backup));
    }

    // The old script stopped halfway here, left a mixture and said "Update Complete.".
    [Theory]
    [InlineData("Revit.Addin.2027.dll")]
    [InlineData("A.dll")]
    [InlineData("Z.dll")]
    public void WhileADllIsInUse_NothingIsTouched_AndItSaysSo(string inUse)
    {
        using (Use(Installed(inUse)))
        {
            (int code, string said) = Run();

            Assert.Equal(PluginUpdateScript.InUse, code);
            Assert.Contains("NOT UPDATED", said);
            Assert.Contains(inUse, said);
            Assert.DoesNotContain("Updated to", said);
        }
        Assert.Equal(Before, State());
    }

    [Fact]
    public async Task ItWaits_UntilTheDllsAreFree()
    {
        FileStream inUse = Use(Installed("Revit.Addin.2027.dll"));
        Task release = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(3));
            inUse.Dispose();
        });

        (int code, string said) = Run(tries: 20);
        await release;

        Assert.Equal(PluginUpdateScript.Installed, code);
        Assert.Contains("Close every Revit 2027 window", said);
        Assert.Equal(After, State());
    }

    // Another window that stays open for hours: it's only given up when no Revit is running. The tests' own program
    // stands in for Revit, which runs as long as they do.
    [Fact]
    public async Task ItWaits_AsLongAsRevitRuns()
    {
        FileStream inUse = Use(Installed("Revit.Addin.2027.dll"));
        Task release = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(8));
            inUse.Dispose();
        });

        (int code, _) = Run(tries: 1, program: Path.GetFileName(Environment.ProcessPath));
        await release;

        Assert.Equal(PluginUpdateScript.Installed, code);
        Assert.Equal(After, State());
    }

    // Two Revit windows, and the update was accepted in both.
    [Fact]
    public async Task TwoAtOnce_InstallItOnce()
    {
        FileStream inUse = Use(Installed("Revit.Addin.2027.dll"));
        Write(tries: 20);
        using var first = new Window(_paths);
        await Until(() => first.Said.Contains("Close every Revit 2027 window"), "the first one waits");

        using var second = new Window(_paths);
        Assert.Equal(PluginUpdateScript.AtWork, second.End());
        Assert.Contains("already", second.Said);
        Assert.Equal(PluginUpdateState.Waiting, new PluginUpdate(_paths, File.ReadAllText).Check().State);
        Assert.Equal(Before, State());

        inUse.Dispose();
        first.PressAKey();
        Assert.Equal(PluginUpdateScript.Installed, first.End());
        Assert.Equal(After, State());
    }

    // In the other window the files are just being copied.
    [Fact]
    public void WhileAnotherRevitCopiesAnUpdate_NothingIsTouched()
    {
        Write();
        using (new FileStream(_paths.LockFile, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None))
        using (var window = new Window(_paths))
            Assert.Equal(PluginUpdateScript.AtWork, window.End());

        Assert.Equal(Before, State());
        Assert.Equal(PluginUpdateScript.Installed, Run().Code);
    }

    private bool IsAtWork()
    {
        try
        {
            using (new FileStream(_paths.LockFile, FileMode.Open, FileAccess.Write, FileShare.None))
                return false;
        }
        catch (IOException)
        {
            return true;
        }
    }

    // Its result is still shown while the next update is copied: when the key is pressed, its file is another one, or gone.
    // It isn't at work any more, so the next one isn't held up.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AWindowThatsStillOpen_IsFinished(bool replaced)
    {
        Write();
        using var window = new Window(_paths);
        await Until(() => window.Said.Contains("Updated to 5.3.0.0.") && !IsAtWork(), "the update");
        if (replaced)
            File.WriteAllText(_paths.Script, string.Concat(Enumerable.Repeat("echo not mine\r\n", 1000)));
        else
            File.Delete(_paths.Script);

        window.PressAKey();

        Assert.Equal(PluginUpdateScript.Installed, window.End());
        Assert.DoesNotContain("not mine", window.Said);
    }

    // The window was closed, or the PC was switched off, while it installed. The add-in's own DLLs come last: until
    // everything else is there, it's the version from before, and Check for Updates offers the new one again.
    [Fact]
    public void InterruptedHalfway_ItsOfferedAgain()
    {
        Run(change: text =>
        {
            const string ownDlls = "robocopy \"%STAGE%\" \"%TARGET%\" %OURS%";
            Assert.Contains(ownDlls, text);
            return text.Replace(ownDlls, "exit 9 & rem ");
        });
        Assert.Contains("A.dll=new", State());
        Assert.Contains("MH.RevitTools.Database.dll=old", State());
        Assert.Contains("Revit.Addin.2027.dll=5.2.2.0", State());

        var update = new PluginUpdate(_paths, File.ReadAllText);
        PluginUpdateCheck check = update.Check();
        Assert.Equal(PluginUpdateState.Newer, check.State);
        Assert.Null(update.Prepare(check));
        Assert.Equal(PluginUpdateScript.Installed, Run().Code);
        Assert.Equal(After, State());
    }

    // Other programs of these names come first on some PCs (Git's find, for instance).
    [Fact]
    public void WindowsOwnPrograms_AreUsed()
    {
        string others = Directory.CreateDirectory(Path.Combine(_root, "others")).FullName;
        foreach (string name in new[] { "robocopy", "tasklist", "find", "ping", "chcp" })
        {
            Put(others, name + ".bat", "@echo not Windows' own\r\n@exit /b 16\r\n");
            Put(_paths.StageFolder, name + ".bat", "@echo not Windows' own\r\n@exit /b 16\r\n");
        }

        (int code, string said) = Run(path: others);

        Assert.Equal(PluginUpdateScript.Installed, code);
        Assert.DoesNotContain("not Windows' own", said);
        Assert.Equal(After, State());
    }

    // An antivirus program can take one away between the copying and the installing (2026-11-24: G DATA did, with a test's copy).
    [Theory]
    [InlineData("MH.RevitTools.Database.dll")]
    [InlineData(@"runtimes\native.dll")]
    public void WithAFileMissingFromTheCopy_NothingIsTouched(string missing)
    {
        File.Delete(Path.Combine(_paths.StagedPayload, missing));

        (int code, string said) = Run();

        Assert.Equal(PluginUpdateScript.Incomplete, code);
        Assert.Contains("NOT UPDATED", said);
        Assert.Contains("antivirus", said);
        Assert.Equal(Before, State());
    }

    // As for a file that this Windows account may not change.
    [Fact]
    public void AReadOnlyDll_IsLeftAlone_AndItSaysSo()
    {
        File.SetAttributes(Installed("Revit.Addin.2027.dll"), FileAttributes.ReadOnly);
        try
        {
            (int code, string said) = Run();

            Assert.Equal(PluginUpdateScript.InUse, code);
            Assert.Contains("NOT UPDATED: \"Revit.Addin.2027.dll\"", said);
            Assert.Equal(Before, State());
        }
        finally
        {
            File.SetAttributes(Installed("Revit.Addin.2027.dll"), FileAttributes.Normal);
        }
    }

    // A name with a sign that means something to cmd.
    [Fact]
    public void ADllsName_IsNoCommand()
    {
        Install(Installed("B&echo not a command&.dll"), "old");
        Put(_paths.StagedPayload, "B&echo not a command&.dll", "new");
        using (Use(Installed("B&echo not a command&.dll")))
        {
            (int code, string said) = Run();

            Assert.Equal(PluginUpdateScript.InUse, code);
            Assert.Contains("\"B&echo not a command&.dll\"", said);
            Assert.DoesNotContain(said.Split('\n'), line => line.StartsWith("not a command"));
        }
    }

    // A file that isn't a DLL isn't waited for; when it can't be replaced, everything goes back.
    [Fact]
    public void WhenSomethingCantBeReplaced_TheVersionFromBeforeStays()
    {
        Install(Installed("Revit.Addin.2027.deps.json"), "old");
        string before = State();
        using (Use(Installed("Revit.Addin.2027.deps.json")))
        {
            (int code, string said) = Run();

            Assert.Equal(PluginUpdateScript.Failed, code);
            Assert.Contains("NOT UPDATED", said);
            Assert.Contains("is still installed", said);
            Assert.Contains("deps.json", said);
        }
        // New files stay beside the old version, which doesn't know them.
        Assert.Equal(before, State().Replace("NewInThisVersion.dll=new, ", "").Replace(@", runtimes\native.dll=new", ""));
    }

    // Without what's there now, nothing could be put back.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WhenAManifestCantBeSaved_NothingIsTouched(bool own)
    {
        using (new FileStream(own ? _paths.UserManifest : _paths.InstalledManifest, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            (int code, string said) = Run();

            Assert.Equal(PluginUpdateScript.Failed, code);
            Assert.Contains("NOT UPDATED", said);
            Assert.Contains("Nothing has been changed", said);
        }
        Assert.Equal(Before, State());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WhenAManifestCantBeReplaced_TheVersionFromBeforeStays(bool own)
    {
        using (Use(own ? _paths.UserManifest : _paths.InstalledManifest))
        {
            (int code, string said) = Run();

            Assert.Equal(PluginUpdateScript.Failed, code);
            Assert.Contains("is still installed", said);
        }
        Assert.Contains("Revit.Addin.2027.dll=5.2.2.0", State());
        Assert.EndsWith("; old manifest; old manifest", State());
    }

    // Someone who was installed without it: Revit reads the one for all users then, and gets no second one.
    [Fact]
    public void WithoutAManifestOfTheirOwn_NoneIsMade()
    {
        File.Delete(_paths.UserManifest);

        Assert.Equal(PluginUpdateScript.Installed, Run().Code);

        Assert.False(File.Exists(_paths.UserManifest));
    }

    [Fact]
    public void RunASecondTime_ThereIsNothingToInstall()
    {
        Assert.Equal(PluginUpdateScript.Installed, Run().Code);
        (int code, string said) = Run();

        Assert.Equal(PluginUpdateScript.NothingToInstall, code);
        Assert.Contains("nothing to install", said);
        Assert.Equal(After, State());
    }

    [Fact]
    public void FilesThatAreTheSame_AreFineToo()
    {
        foreach (string file in Directory.GetFiles(_paths.StagedPayload))
            File.Copy(file, Installed(Path.GetFileName(file)), true);

        Assert.Equal(PluginUpdateScript.Installed, Run().Code);
        Assert.Equal(After, State());
    }

    // For the real one: 15 tries of two seconds without a Revit, and no end while one runs.
    [Fact]
    public void TheWrittenScript_LooksForRevit()
    {
        string text = PluginUpdateScript.Text(_paths, "5.3.0.0", _files);

        Assert.Contains("IMAGENAME eq Revit.exe", text);
        Assert.Contains("GEQ 15", text);
        Assert.DoesNotContain("xcopy", text);
        Assert.DoesNotContain("powershell", text, StringComparison.OrdinalIgnoreCase);
    }
}
