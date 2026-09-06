# Revit.Addin deployment scripts

One set of PowerShell scripts, identical byte-for-byte in `Revit.Addin.2024`, `.2026` and `.2027`
and in both repositories. Each derives the Revit year from its own project folder name, so nothing
here needs editing when a year is added — copy the folder and it works.

Replaces the previous `.bat` scripts plus `version_increment.ps1`.

## The scripts

| Script | What it does | Replaces |
|---|---|---|
| `Deploy-Local.ps1` | Copies a build into the local Revit add-in folders. Runs from the post-build event. | `postbuild.bat` |
| `Publish-ToShare.ps1` | Publishes a Release to the Teams update share. Developer side. | `LocalToServer.bat` |
| `Update-FromShare.ps1` | Pulls the current release from the share onto a machine. User side. | `run_plugin_update.bat` |
| `Install.ps1` | First-time install from a handed-over folder. | `installer.bat` |
| `Bump-Version.ps1` | Raises the version and appends a changelog row. | `version_increment.ps1` |
| `Release-Wizard.ps1` | Bump → build → verify → publish, in order, with confirmations. | *(new)* |
| `_Common.ps1` | Shared helpers. Dot-sourced, never run directly. | — |

## The one behaviour change

**A Release build no longer publishes to the office share.**

`postbuild.bat` used to copy to the Teams share on every Release build, so an ordinary local
Release build shipped to everyone. Publishing is now something you ask for:

```powershell
pwsh -File Scripts\Release-Wizard.ps1
```

or, if the version and build are already right:

```powershell
pwsh -File Scripts\Publish-ToShare.ps1
```

Nothing reaches the share without an explicit confirmation, and `-WhatIfOnly` shows exactly what
would be copied where.

## Versioning is no longer part of the build

The old `.csproj` PreBuild event launched `version_increment.ps1` with `start "" powershell -NoExit`.
`start` detaches, so MSBuild did not wait — the build compiled the *previous* `AssemblyInfo.cs`
while the version prompt was still on screen, and the shipped assembly could disagree with
`version.txt`. Bump first, then build. `Release-Wizard.ps1` does both in the right order and fails
if the built assembly and `version.txt` disagree.

## Common tasks

```powershell
# Full release, with prompts
pwsh -File Scripts\Release-Wizard.ps1

# Just raise the version
pwsh -File Scripts\Bump-Version.ps1 -Part patch -Message "Fixed the Norm column width"

# See what a publish would do, without writing
pwsh -File Scripts\Publish-ToShare.ps1 -WhatIfOnly

# Update this machine from the share
powershell -ExecutionPolicy Bypass -File Scripts\Update-FromShare.ps1

# Install for one user only, no admin rights needed
powershell -ExecutionPolicy Bypass -File Scripts\Install.ps1 -PerUser
```

Every script takes `-RevitYear` to override the year, and `-WhatIfOnly` to write nothing.
Run `Get-Help .\Scripts\Publish-ToShare.ps1 -Full` for the rest.

## The payload folder was renamed

The assemblies used to install into `Addins\<year>\PluginTrail\`. They now install into
`Addins\<year>\MH.RevitTools\`, which says what it holds and sits beside `MH.IfcCustomExport`.

`Install.ps1` and `Update-FromShare.ps1` delete a leftover `PluginTrail\` before writing the new
folder, so nobody ends up with two copies of the assemblies and a manifest pointing at one of them.

**Existing installations need one manual re-install.** The version colleagues are running has the
old folder name compiled into it, and the update script it generates at runtime copies into
`PluginTrail\` while the new manifest points at `MH.RevitTools\` — so the add-in would not load.
The in-Revit *Check for Updates* button cannot carry itself across this rename. Send the package
from `New-Package.ps1` once and have people run `Install.cmd`; after that, updates work normally
again.

## Notes

- The files are saved **UTF-8 with BOM** on purpose. Windows PowerShell 5.1 reads a BOM-less `.ps1`
  as ANSI, which corrupts the `ü` in the `Müller+Hereth` share path and sends the copy somewhere
  that does not exist.
- Scripts refuse to touch a Revit install while Revit is running — it holds locks on loaded
  assemblies, and copying over it leaves a mismatched set of DLLs.
- Everything copied is unblocked (Mark-of-the-Web). Files synced from SharePoint are blocked by
  default and .NET will not load a blocked assembly inside Revit.
