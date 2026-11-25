using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

// Keep identical in 2024, 2026 and 2027 but for the year (C# 7.3, .NET Framework 4.8). Without Revit, so it's under tests:
// MH.RevitTools.Database.Tests\AddinUpdate.
namespace Revit.Addin._2026.Services
{
    /// <summary>What Check for Updates found.</summary>
    public enum PluginUpdateState
    {
        /// <summary>The Teams folder with the updates isn't on this PC.</summary>
        NoTeamsFolder,

        /// <summary>Something is missing or not there yet; the text says what.</summary>
        NotUsable,

        UpToDate,

        /// <summary>The installed version is newer than the published one.</summary>
        InstalledIsNewer,

        /// <summary>The published version is newer: it can be installed.</summary>
        Newer,

        /// <summary>An update is at work already: its script waits for Revit to close, or another Revit copies it.</summary>
        Waiting,
    }

    /// <summary>The answer of <see cref="PluginUpdate.Check"/>. Immutable.</summary>
    public sealed class PluginUpdateCheck
    {
        internal PluginUpdateCheck(PluginUpdateState state, string installed, string published, string text)
        {
            State = state;
            Installed = installed;
            Published = published;
            Text = text;
        }

        public PluginUpdateState State { get; }

        /// <summary>The installed version, e.g. 5.2.2.0; null when that isn't known.</summary>
        public string Installed { get; }

        /// <summary>The version in the Teams folder; null when that isn't known.</summary>
        public string Published { get; }

        /// <summary>In plain words, where there's something to say; otherwise null.</summary>
        public string Text { get; }
    }

    /// <summary>Where an update comes from and goes to, for one Revit year. Immutable.</summary>
    public sealed class PluginUpdatePaths
    {
        // As in Publish-ToShare.ps1 and the installers (_Common.ps1).
        private const string TeamsFolder = @"Müller+Hereth GmbH\31 BIM - Dokumente\General\01 Software\01 Revit-Tools\Updates\Versions";
        private const string PayloadFolder = "MH.RevitTools";

        public PluginUpdatePaths(string revitYear, string shareFolder, string installedRoot, string userRoot, string stageFolder)
        {
            // Windows answers with nothing when it doesn't know a folder. The rest would be looked for, and cleared,
            // wherever Revit happens to be.
            if (!new[] { shareFolder, installedRoot, userRoot, stageFolder }.All(Path.IsPathRooted))
                throw new ArgumentException("Windows doesn't say where this computer's folders are.");

            RevitYear = revitYear;
            Name = "Revit.Addin." + revitYear;

            ShareFolder = shareFolder;
            SharePayload = Path.Combine(shareFolder, PayloadFolder);
            ShareDll = Path.Combine(SharePayload, Name + ".dll");
            ShareManifest = Path.Combine(shareFolder, Name + ".addin");
            ShareVersionFile = Path.Combine(shareFolder, "version.txt");

            InstalledRoot = installedRoot;
            InstalledPayload = Path.Combine(installedRoot, PayloadFolder);
            InstalledDll = Path.Combine(InstalledPayload, Name + ".dll");
            InstalledManifest = Path.Combine(installedRoot, Name + ".addin");
            UserManifest = Path.Combine(userRoot, Name + ".addin");

            StageFolder = stageFolder;
            StagedPayload = Path.Combine(stageFolder, "payload");
            StagedManifest = Path.Combine(stageFolder, Name + ".addin");
            Backup = Path.Combine(stageFolder, "backup");
            Log = Path.Combine(stageFolder, "update.log");
            LockFile = Path.Combine(stageFolder, "update.lock");
            Script = Path.Combine(stageFolder, "update.bat");
        }

        public static PluginUpdatePaths For(string revitYear) => new PluginUpdatePaths(
            revitYear,
            Path.Combine(Folder(Environment.SpecialFolder.UserProfile), TeamsFolder, revitYear, "dependencies"),
            Path.Combine(Folder(Environment.SpecialFolder.CommonApplicationData), "Autodesk", "Revit", "Addins", revitYear),
            Path.Combine(Folder(Environment.SpecialFolder.ApplicationData), "Autodesk", "Revit", "Addins", revitYear),
            Path.Combine(Folder(Environment.SpecialFolder.LocalApplicationData), "MH", "RevitTools", "Update", revitYear));

        public string RevitYear { get; }

        /// <summary>E.g. Revit.Addin.2027: what the DLL and the manifest are called.</summary>
        public string Name { get; }

        /// <summary>The synchronized Teams folder of this year's updates.</summary>
        public string ShareFolder { get; }

        public string SharePayload { get; }

        public string ShareDll { get; }

        public string ShareManifest { get; }

        public string ShareVersionFile { get; }

        /// <summary>Revit's add-in folder for all users, whose DLL every manifest points at.</summary>
        public string InstalledRoot { get; }

        public string InstalledPayload { get; }

        public string InstalledDll { get; }

        public string InstalledManifest { get; }

        /// <summary>The person's own manifest. It wins over the one for all users, which Revit 2027 doesn't even read.</summary>
        public string UserManifest { get; }

        /// <summary>The copied files until they're installed, with the script. One folder per year.</summary>
        public string StageFolder { get; }

        public string StagedPayload { get; }

        public string StagedManifest { get; }

        public string Backup { get; }

        public string Log { get; }

        /// <summary>Held open by whoever copies or installs an update, so that it's one at a time.</summary>
        public string LockFile { get; }

        public string Script { get; }

        private static string Folder(Environment.SpecialFolder folder) => Environment.GetFolderPath(folder);
    }

    /// <summary>
    /// Check for Updates: compares the installed add-in with the one in the Teams folder, copies a newer one to this PC
    /// and checks the copy. Installing it is the script's work, once Revit has closed (<see cref="PluginUpdateScript"/>).
    /// </summary>
    public sealed class PluginUpdate
    {
        // What nobody needs at run time, as in Copy-Payload of the scripts.
        private static readonly string[] LeftOut = { ".pdb", ".xml" };

        // Windows' number for a file that somebody has open.
        private const int SharingViolation = 32;

        private const int LongestShown = 40;

        private readonly PluginUpdatePaths _paths;
        private readonly Func<string, string> _versionOf;

        /// <param name="versionOf">Reads a DLL's version; null for Windows' own.</param>
        public PluginUpdate(PluginUpdatePaths paths, Func<string, string> versionOf = null)
        {
            _paths = paths ?? throw new ArgumentNullException(nameof(paths));
            _versionOf = versionOf ?? (file => FileVersionInfo.GetVersionInfo(file).FileVersion);
        }

        public PluginUpdateCheck Check()
        {
            try
            {
                return Compare();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return NotUsable("The Teams folder can't be read now. Is OneDrive running? (" + ex.Message + ")");
            }
        }

        private PluginUpdateCheck Compare()
        {
            if (IsAtWork())
                return new PluginUpdateCheck(PluginUpdateState.Waiting, null, null, WaitingText);
            if (!Directory.Exists(_paths.ShareFolder))
                return new PluginUpdateCheck(PluginUpdateState.NoTeamsFolder, null, null, null);
            if (!File.Exists(_paths.ShareVersionFile))
                return NotUsable("The Teams folder has no version.txt for Revit " + _paths.RevitYear + ". It may still be synchronizing: try again in a few minutes.");
            if (!File.Exists(_paths.InstalledDll))
                return NotUsable("The installed add-in isn't where it should be:" + Environment.NewLine + _paths.InstalledDll);

            string written = File.ReadAllText(_paths.ShareVersionFile).Trim();
            Version published = Parse(written);
            if (published == null)
                return NotUsable("version.txt in the Teams folder doesn't hold a version: \""
                    + (written.Length > LongestShown ? written.Substring(0, LongestShown) + "…" : written) + "\".");
            Version installed = Parse(_versionOf(_paths.InstalledDll));
            if (installed == null)
                return NotUsable("The installed add-in doesn't say which version it is.");

            // The numbers, not what was written: it goes into the script, where a line of its own would be a command.
            string publishedText = published.ToString();
            string installedText = installed.ToString();

            if (published.Equals(installed))
                return new PluginUpdateCheck(PluginUpdateState.UpToDate, installedText, publishedText, null);
            if (published < installed)
                return new PluginUpdateCheck(PluginUpdateState.InstalledIsNewer, installedText, publishedText,
                    "You have version " + installedText + ", which is newer than the published one (" + publishedText + ").");

            // version.txt can arrive before the DLLs do.
            string there = File.Exists(_paths.ShareDll) ? (_versionOf(_paths.ShareDll) ?? "").Trim() : "";
            if (!published.Equals(Parse(there)))
                return NotUsable("The Teams folder isn't completely synchronized yet: version.txt says " + publishedText + ", and the add-in there "
                    + (there.Length == 0 ? "is missing" : "is " + there) + ". Try again in a few minutes.");
            if (!File.Exists(_paths.ShareManifest))
                return NotUsable("The Teams folder isn't completely synchronized yet: " + Path.GetFileName(_paths.ShareManifest) + " is missing. Try again in a few minutes.");
            return new PluginUpdateCheck(PluginUpdateState.Newer, installedText, publishedText, null);
        }

        /// <summary>
        /// Copies the published files to this PC, checks them, and writes the script that installs them. Null when that
        /// worked; otherwise why not, and nothing is left to install.
        /// </summary>
        public string Prepare(PluginUpdateCheck check)
        {
            if (check == null)
                throw new ArgumentNullException(nameof(check));
            try
            {
                Directory.CreateDirectory(_paths.StageFolder);
                using (FileStream held = TryHold())
                    return held == null ? WaitingText : Copy(check.Published);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return "The files couldn't be copied from the Teams folder to this computer. Is OneDrive running? (" + ex.Message + ")";
            }
        }

        private string Copy(string version)
        {
            Clear();
            int files = CopyPayload();
            CopyFile(_paths.ShareManifest, _paths.StagedManifest);

            string copied = _versionOf(Path.Combine(_paths.StagedPayload, _paths.Name + ".dll"));
            if (!Equals(Parse(copied), Parse(version)))
            {
                Clear();
                return "The files in the Teams folder changed while they were copied. Try again in a few minutes.";
            }
            // Without a mark at the start, which cmd would take for a command.
            File.WriteAllText(_paths.Script, PluginUpdateScript.Text(_paths, version, files), new UTF8Encoding(false));
            return null;
        }

        private string WaitingText =>
            "A small window is open that installs it. Close every Revit " + _paths.RevitYear + " window, and it goes on by itself.";

        private bool IsAtWork()
        {
            if (!File.Exists(_paths.LockFile))
                return false;
            using (FileStream held = TryHold())
                return held == null;
        }

        // Null while somebody else holds it. Windows lets go of it however they end.
        private FileStream TryHold()
        {
            try
            {
                return new FileStream(_paths.LockFile, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
            }
            catch (IOException ex) when ((ex.HResult & 0xFFFF) == SharingViolation)
            {
                return null;
            }
        }

        /// <summary>
        /// True when this Windows account may not replace the installed files, as when an administrator installed them.
        /// Windows checks that before it checks whether a file is in use, so it can be asked while Revit runs. Only for
        /// saying so: the script has the last word, and runs with the person's own rights. No file is changed, and the one
        /// that's added to see whether that works is gone again at once.
        /// </summary>
        public bool NeedsAdministrator()
        {
            try
            {
                IEnumerable<string> files = Directory.GetFiles(_paths.InstalledPayload, "*", SearchOption.AllDirectories)
                    .Concat(new[] { _paths.InstalledManifest }.Where(File.Exists));
                return files.Any(IsRefused) || !CanAddTo(_paths.InstalledPayload);
            }
            catch (UnauthorizedAccessException)
            {
                return true;
            }
            catch (IOException)
            {
                return false;
            }
        }

        // Only a file's rights count: one that's in use now is Revit's.
        private static bool IsRefused(string file)
        {
            try
            {
                using (new FileStream(file, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
                    return false;
            }
            catch (UnauthorizedAccessException)
            {
                return true;
            }
            catch (IOException)
            {
                return false;
            }
        }

        private static bool CanAddTo(string folder)
        {
            string probe = Path.Combine(folder, ".update-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose))
                    return true;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
            catch (IOException)
            {
                return true;
            }
        }

        // Not the folder itself, as a program can be in it, nor what the script saved.
        private void Clear()
        {
            File.Delete(_paths.Script);
            File.Delete(_paths.StagedManifest);
            if (Directory.Exists(_paths.StagedPayload))
                Directory.Delete(_paths.StagedPayload, true);
        }

        private int CopyPayload()
        {
            string from = _paths.SharePayload.TrimEnd('\\') + "\\";
            List<string> files = Directory.GetFiles(from, "*", SearchOption.AllDirectories)
                .Where(file => !LeftOut.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)).ToList();
            foreach (string file in files)
                CopyFile(file, Path.Combine(_paths.StagedPayload, file.Substring(from.Length)));
            return files.Count;
        }

        // The content only: a file from SharePoint can carry the Mark of the Web, with which .NET Framework refuses to
        // load a DLL, and Windows' own copying would take it along. Its time stays behind too: each copy has the time of
        // the copying, so that the script's robocopy never takes an installed file for the same one.
        private static void CopyFile(string from, string to)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(to));
                using (var source = new FileStream(from, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var target = new FileStream(to, FileMode.Create, FileAccess.Write, FileShare.None))
                    source.CopyTo(target);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                throw new IOException(Path.GetFileName(from) + ": " + ex.Message, ex);
            }
        }

        // With all four parts, so that 5.3.0 is 5.3.0.0; null when it isn't a version.
        private static Version Parse(string text)
        {
            Version version;
            return Version.TryParse((text ?? "").Trim(), out version)
                ? new Version(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision))
                : null;
        }

        private static PluginUpdateCheck NotUsable(string text) => new PluginUpdateCheck(PluginUpdateState.NotUsable, null, null, text);
    }

    /// <summary>
    /// The script that installs the copied update. It runs when Revit closes: it waits until no Revit uses the DLLs, saves
    /// the installed files, replaces them, checks that all of them arrived, and says so. If anything goes wrong, the files
    /// from before go back, and it says that. Its window can be closed at any time, so the add-in's own DLLs come last: as
    /// long as not everything else is new, it's the version from before, and Check for Updates offers the new one again.
    /// Plain cmd, as antivirus programs don't like scripts that a program starts.
    /// </summary>
    public static class PluginUpdateScript
    {
        /// <summary>The script's exit codes.</summary>
        public const int Installed = 0;
        public const int Failed = 1;
        public const int InUse = 2;
        public const int NothingToInstall = 3;
        public const int AtWork = 4;
        public const int Incomplete = 5;

        /// <param name="files">How many were copied. An antivirus program can take one away until they're installed.</param>
        /// <param name="tries">How often it looks, every two seconds, while files can't be replaced although no Revit runs.</param>
        /// <param name="program">What it waits for.</param>
        public static string Text(PluginUpdatePaths paths, string version, int files, int tries = 15, string program = "Revit.exe")
        {
            string revit = "Revit " + paths.RevitYear;
            var lines = new[]
            {
                "@echo off",
                "setlocal EnableExtensions DisableDelayedExpansion",
                // Windows' own programs, whichever others this PC has; and the window is in no folder of ours.
                "set \"PATH=%SystemRoot%\\System32\"",
                "cd /d \"%SystemRoot%\\System32\"",
                "chcp 65001 >nul",
                "title Parameter Tools for " + revit + ": update to " + version,
                Set("STAGE", paths.StagedPayload),
                Set("MANIFEST", paths.StagedManifest),
                Set("TARGET", paths.InstalledPayload),
                Set("ALLUSERS", paths.InstalledManifest),
                Set("OWN", paths.UserManifest),
                Set("BACKUP", paths.Backup),
                Set("LOG", paths.Log),
                Set("LOCK", paths.LockFile),
                Set("DLL", paths.Name + ".dll"),
                Set("OURS", "Revit.Addin.*.dll MH.*.dll"),
                Set("ALL", "/E /R:3 /W:2 /NFL /NDL /NJH /NJS /NP"),
                "set \"CODE=\"",

                // One at a time: the second one can't open the file the first one writes to, and doesn't get in. In
                // parentheses, so that it's read to the end before the first one lets go: another update may then replace
                // this file while the window still shows how this one went. That's why it ends without /b, too, which
                // would look for the file again.
                "(",
                "2>nul (9>\"%LOCK%\" call :main)",
                "setlocal EnableDelayedExpansion",
                "if not defined CODE (",
                "echo Another window is already working on the update.",
                "set \"CODE=" + AtWork + "\"",
                ") else (",
                "echo.",
                "pause",
                ")",
                "exit !CODE!",
                ")",

                ":main",
                "echo Parameter Tools for " + revit + ": update to " + version,
                "echo.",
                "if not exist \"%STAGE%\\%DLL%\" goto nothing",
                "set \"TOLD=\"",
                "set \"TRIES=0\"",

                // --- wait until no DLL is in use ---
                ":wait",
                "set \"BUSY=\"",
                "for %%F in (\"%STAGE%\\*.dll\") do call :probe \"%%~nxF\"",
                "if not defined BUSY goto install",
                "tasklist /FI \"IMAGENAME eq " + program + "\" 2>nul | find /I \"" + program + "\" >nul",
                "if errorlevel 1 set /a TRIES+=1",
                "if %TRIES% GEQ " + tries + " goto inuse",
                "if not defined TOLD echo Waiting until Revit has closed. Close every " + revit + " window.",
                "if not defined TOLD echo To leave everything as it is, close this window.",
                "set \"TOLD=1\"",
                "ping -n 3 127.0.0.1 >nul",
                "goto wait",

                // --- save, replace, check ---
                ":install",
                "set \"COUNT=0\"",
                "for /R \"%STAGE%\" %%F in (*) do set /a COUNT+=1",
                "if %COUNT% LSS " + files + " goto incomplete",
                "echo Installing... Don't close this window.",
                "if exist \"%BACKUP%\" rd /s /q \"%BACKUP%\"",
                "robocopy \"%TARGET%\" \"%BACKUP%\\payload\" %ALL% >\"%LOG%\"",
                "if errorlevel 8 goto notsaved",
                "if exist \"%ALLUSERS%\" (copy /B /Y \"%ALLUSERS%\" \"%BACKUP%\\allusers.addin\" >\"%LOG%\" 2>&1 || goto notsaved)",
                "if exist \"%OWN%\" (copy /B /Y \"%OWN%\" \"%BACKUP%\\own.addin\" >\"%LOG%\" 2>&1 || goto notsaved)",
                "robocopy \"%STAGE%\" \"%TARGET%\" %ALL% /XF %OURS% >\"%LOG%\"",
                "if errorlevel 8 goto failed",
                "robocopy \"%STAGE%\" \"%TARGET%\" %OURS% %ALL% >\"%LOG%\"",
                "if errorlevel 8 goto failed",
                "robocopy \"%STAGE%\" \"%TARGET%\" %ALL% /L >nul",
                "set /a \"MISSING=%ERRORLEVEL% & 1\"",
                "if not \"%MISSING%\"==\"0\" goto failed",
                "copy /B /Y \"%MANIFEST%\" \"%ALLUSERS%\" >\"%LOG%\" 2>&1",
                "if errorlevel 1 goto failed",
                "if not exist \"%OWN%\" goto done",
                "copy /B /Y \"%MANIFEST%\" \"%OWN%\" >\"%LOG%\" 2>&1",
                "if errorlevel 1 goto failed",

                ":done",
                "rd /s /q \"%BACKUP%\" 2>nul",
                "rd /s /q \"%STAGE%\" 2>nul",
                "echo Updated to " + version + ". You can start " + revit + " again.",
                "set \"CODE=" + Installed + "\"",
                "exit /b 0",

                // The other way round: the add-in's own DLLs first.
                ":failed",
                "type \"%LOG%\"",
                "robocopy \"%BACKUP%\\payload\" \"%TARGET%\" %OURS% %ALL% >nul",
                "robocopy \"%BACKUP%\\payload\" \"%TARGET%\" %ALL% >nul",
                "if errorlevel 8 (set \"BACK=\") else (set \"BACK=1\")",
                "if exist \"%BACKUP%\\allusers.addin\" copy /B /Y \"%BACKUP%\\allusers.addin\" \"%ALLUSERS%\" >nul",
                "if exist \"%BACKUP%\\own.addin\" copy /B /Y \"%BACKUP%\\own.addin\" \"%OWN%\" >nul",
                "echo.",
                "if defined BACK echo NOT UPDATED. The version from before is still installed.",
                "if not defined BACK echo NOT UPDATED, and the version from before couldn't be put back completely. Install the add-in again.",
                "set \"CODE=" + Failed + "\"",
                "exit /b 0",

                ":notsaved",
                "type \"%LOG%\"",
                "echo.",
                "echo NOT UPDATED: the installed files couldn't be saved first. Nothing has been changed.",
                "set \"CODE=" + Failed + "\"",
                "exit /b 0",

                ":inuse",
                "echo NOT UPDATED: \"%BUSY%\" can't be replaced. A program has it open, or this Windows account may not change it.",
                "echo Nothing has been changed. Use Check for Updates again later, or let an administrator install it with Install.cmd.",
                "set \"CODE=" + InUse + "\"",
                "exit /b 0",

                ":incomplete",
                "echo NOT UPDATED: a copied file is gone. An antivirus program may have removed it.",
                "echo Nothing has been changed. Use Check for Updates again, or install the new version with Install.cmd from the ZIP.",
                "set \"CODE=" + Incomplete + "\"",
                "exit /b 0",

                ":nothing",
                "echo There's nothing to install. Use Check for Updates in Revit.",
                "set \"CODE=" + NothingToInstall + "\"",
                "exit /b 0",

                // Opens a file for writing without writing: what's in use, or may not be changed, can't be opened.
                ":probe",
                "if exist \"%TARGET%\\%~1\" 2>nul (>>\"%TARGET%\\%~1\" (call )) || set \"BUSY=%~1\"",
                "exit /b 0",
            };
            return string.Join("\r\n", lines) + "\r\n";
        }

        // A percent sign in a path would otherwise start a variable.
        private static string Set(string name, string value) => "set \"" + name + "=" + value.Replace("%", "%%") + "\"";
    }
}
