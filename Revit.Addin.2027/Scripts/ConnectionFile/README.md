# The connection file on the office PCs

**Not used** (Yehia, 2026-11-24). The connection file goes along with the add-in, without the password, which each person enters
once in Revit: `DATABASE_LINK.md`, step 9. These scripts are for a file that holds the password. The add-in prefers the file they
install, so nothing else would have to change.

The database link in Revit reads `C:\ProgramData\MH\RevitTools\database.json`, one file per PC for every Revit year: which
server and database, and the read-only login with its password. These scripts put it there. Its format, and why it's
separate from everything else, is in `DATABASE_LINK.md` ("The connection file", step 9).

| Script | What it does |
|---|---|
| `New-ConnectionFilePackage.ps1` | Developer side. Makes the folder to hand over, `C:\Yehia\ProjectsApps\Revit.Addin\ConnectionFile`, from the office's `database.json`. |
| `Install-ConnectionFile.ps1` | On each PC, as administrator: puts the file there and locks the folder. |
| `_ConnectionFile.ps1` | Helpers for both. Dot-sourced, never run directly. |
| `Test-ConnectionFile.ps1` | Tests, on throwaway folders. Run it with `powershell` and with `pwsh`. |

## In the office, once

1. The database session's login script has written `C:\Yehia\ProjectsApps\Revit.Addin\database.json`.
2. See what would happen, then make the folder:

   ```powershell
   pwsh -File Scripts\ConnectionFile\New-ConnectionFilePackage.ps1 -WhatIfOnly
   pwsh -File Scripts\ConnectionFile\New-ConnectionFilePackage.ps1
   ```

3. Copy the folder to a stick. On each PC, from the stick: right-click `Install connection file.cmd`, Run as
   administrator. Any administrator account will do, and Revit may stay open.
4. In Revit: Parameter Tools, Database panel, the arrow below it, Test connection.

A new password later is the same again: the file for the same database is simply replaced.

## Why it isn't part of Install.ps1

- A PC that already has the add-in needs only the file, once. Check for Updates brings later versions of the add-in and
  never touches it.
- It needs administrator rights. The add-in for Revit 2027 is registered per person (its manifest is in `%APPDATA%`), so
  the add-in's own installer has to run as that person, not as another administrator.
- The add-in's package and ZIP never hold the password.
- `Install.ps1` and `New-Package.ps1` stay identical in 2024, 2026, 2027 and ProjectsApp.

## What it takes care of

- It never shows the password: not on screen, not in a preview, not in an error.
- It stops, and changes nothing, when the file is a template, isn't complete, or is a developer's; when the PC's file is for
  another database (`-Force` replaces it); when the folder is a link; or when `C:\ProgramData\MH` holds something else.
- It locks `C:\ProgramData\MH` and `MH\RevitTools`: administrators change, everyone reads, nothing inherited, and the
  administrators own them. Any user may create folders under `C:\ProgramData`, so an existing `MH` is never taken over: a new
  one is built beside it, locked from the start and with the file in it, and then takes its place. An unlocked one is kept as
  `MH.before-<time>`. Then everything is read back from Windows.
- If the swap fails, the `MH` that was there stays.
- The hand-over folder is never made in a repository, a synchronized or network folder, or on a stick, and only you and
  administrators can open it.
- `C:\ProgramData\Autodesk\Revit\Addins\2027\MH.RevitTools` is left as it is: Check for Updates writes there without
  administrator rights.

The files are saved UTF-8 with BOM, for Windows PowerShell 5.1.
