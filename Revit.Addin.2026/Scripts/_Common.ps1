<#
.SYNOPSIS
    Shared helpers for the Revit.Addin deployment scripts. Dot-sourced, never run directly.

.DESCRIPTION
    Every script in this folder derives its paths from $PSScriptRoot and the Revit year, so the
    same file works unchanged in Revit.Addin.2024, .2026 and .2027 and in both repositories.
    Nothing here hardcodes a drive, a user or a project location.

    Windows PowerShell 5.1 compatible: no ternaries, no null-coalescing, no PS7-only cmdlets.
#>

# No Set-StrictMode here on purpose. This file is dot-sourced, so anything it sets applies to the
# caller's scope -- including an interactive session someone is exploring in. A strict-mode error
# aborting a publish half-way through the share is a worse failure than the typo class it catches.

# --- output -------------------------------------------------------------------------------------

function Write-Step { param([string]$Text) Write-Host "  $Text" }
function Write-Info { param([string]$Text) Write-Host "  $Text" -ForegroundColor DarkGray }
function Write-Ok   { param([string]$Text) Write-Host "  $Text" -ForegroundColor Green }
function Write-Warn { param([string]$Text) Write-Host "  $Text" -ForegroundColor Yellow }

function Write-Banner {
    param([string]$Title, [string]$Subtitle)
    Write-Host ''
    Write-Host "  $Title" -ForegroundColor Cyan
    if ($Subtitle) { Write-Host "  $Subtitle" -ForegroundColor DarkGray }
    Write-Host ('  ' + ('-' * 68)) -ForegroundColor DarkGray
}

# --- project layout -----------------------------------------------------------------------------

<#
Resolves everything from the folder this script lives in. Two layouts are supported, because the
same scripts run from the source tree and from a package handed to a colleague:

    <repo>\Revit.Addin.<year>\Scripts\_Common.ps1     source tree; project is the parent
    <package>\_Common.ps1                             package root; project is this folder

The .addin manifest tells them apart and supplies the identity. Taking the name from the manifest
rather than the folder matters: a package folder is called Revit.Addin.2024-5.1.0.0, so the folder
name would give the wrong assembly name and the wrong-looking year.

Pass -RevitYear to override.
#>
function Get-AddinContext {
    param(
        [string]$ScriptRoot,
        [int]$RevitYear = 0
    )

    # A manifest beside this script means we are in a package; otherwise look one level up.
    $projectDir = $ScriptRoot
    $candidates = @(Get-ChildItem -LiteralPath $projectDir -Filter '*.addin' -File -ErrorAction SilentlyContinue)
    if ($candidates.Count -eq 0) {
        $projectDir = Split-Path -Parent $ScriptRoot
        $candidates = @(Get-ChildItem -LiteralPath $projectDir -Filter '*.addin' -File -ErrorAction SilentlyContinue)
    }
    if ($candidates.Count -eq 0) {
        throw "No .addin manifest found in '$ScriptRoot' or its parent. Run this from a project's Scripts folder or from a package folder."
    }

    # Revit.Addin.2024 also ships Revit.Addin.2022.addin and .2023.addin, left from when one
    # project built for three years. Picking the first match alphabetically would silently target
    # Revit 2022, so match the folder name and only fall back when nothing matches.
    $folderName = Split-Path -Leaf $projectDir
    $addin = $candidates | Where-Object { [IO.Path]::GetFileNameWithoutExtension($_.Name) -eq $folderName } |
             Select-Object -First 1
    if (-not $addin) {
        # Package folders are named <project>-<version>, e.g. Revit.Addin.2024-5.1.0.0.
        $addin = $candidates | Where-Object { $folderName.StartsWith([IO.Path]::GetFileNameWithoutExtension($_.Name), [StringComparison]::OrdinalIgnoreCase) } |
                 Sort-Object { $_.BaseName.Length } -Descending | Select-Object -First 1
    }
    if (-not $addin -and $candidates.Count -eq 1) { $addin = $candidates[0] }
    if (-not $addin) {
        $names = ($candidates | ForEach-Object { $_.Name }) -join ', '
        throw "Cannot tell which manifest belongs to '$folderName'. Found: $names. Rename the folder to match one, or pass -RevitYear."
    }

    $addinFile = $addin.FullName
    $projectName = [IO.Path]::GetFileNameWithoutExtension($addin.Name)

    if ($RevitYear -le 0) {
        if ($projectName -match '(20\d{2})') {
            $RevitYear = [int]$Matches[1]
        }
        else {
            throw "Cannot determine the Revit year from manifest '$($addin.Name)'. Pass -RevitYear explicitly."
        }
    }

    $shareVersionRoot = Join-Path $env:USERPROFILE ("Müller+Hereth GmbH\31 BIM - Dokumente\General\01 Software\01 Revit-Tools\Updates\Versions\{0}" -f $RevitYear)

    return [pscustomobject]@{
        RevitYear        = $RevitYear
        ProjectName      = $projectName
        ProjectDir       = $projectDir
        ScriptsDir       = $ScriptRoot
        AddinFile        = $addinFile
        AddinFileName    = Split-Path -Leaf $addinFile
        VersionFile      = Join-Path $projectDir 'version.txt'
        VersionDocsFile  = Join-Path $projectDir 'version-docs.xlsx'
        AssemblyInfoFile = Join-Path $projectDir 'Properties\AssemblyInfo.cs'
        PayloadFolder    = 'PluginTrail'

        # Local install roots. Both are written so the add-in loads whether Revit reads the
        # per-user or the all-users manifest.
        AppDataAddinRoot     = Join-Path $env:APPDATA ("Autodesk\Revit\Addins\{0}" -f $RevitYear)
        ProgramDataAddinRoot = Join-Path $env:ProgramData ("Autodesk\Revit\Addins\{0}" -f $RevitYear)

        # Office share, laid out exactly as the old postbuild.bat left it so existing installs
        # and the ribbon's "Check for Updates" keep working.
        ShareVersionRoot = $shareVersionRoot
        ShareDepsRoot    = Join-Path $shareVersionRoot 'dependencies'
    }
}

function Get-PayloadDir {
    param([string]$AddinRoot, $Context)
    return Join-Path $AddinRoot $Context.PayloadFolder
}

# --- guards -------------------------------------------------------------------------------------

<#
Revit holds an exclusive lock on loaded add-in assemblies. Copying over a running instance leaves
a half-updated, mismatched set of DLLs that fails at load time, so every script checks first.
#>
function Assert-RevitNotRunning {
    param(
        [switch]$WarnOnly,
        [string]$Action = 'continue'
    )
    $revit = Get-Process -Name 'Revit' -ErrorAction SilentlyContinue
    if (-not $revit) { return $true }

    $pids = ($revit | ForEach-Object { $_.Id }) -join ', '
    $message = "Revit is running (pid $pids). Close it and re-run, or the copy will be half-applied."
    if ($WarnOnly) {
        Write-Warn "Skipped: $message"
        return $false
    }
    throw $message
}

function Test-Writable {
    param([string]$Path)
    try {
        New-Item -ItemType Directory -Path $Path -Force -ErrorAction Stop | Out-Null
        $probe = Join-Path $Path ('.write-probe-' + [Guid]::NewGuid().ToString('N'))
        New-Item -ItemType File -Path $probe -ErrorAction Stop | Out-Null
        Remove-Item -LiteralPath $probe -Force
        return $true
    }
    catch { return $false }
}

# --- version ------------------------------------------------------------------------------------

function Read-ProjectVersion {
    param($Context)
    if (-not (Test-Path -LiteralPath $Context.VersionFile)) {
        throw "version.txt not found at $($Context.VersionFile)"
    }
    $raw = (Get-Content -LiteralPath $Context.VersionFile -Raw).Trim()
    if ($raw -notmatch '^\d+\.\d+\.\d+\.\d+$') {
        throw "version.txt does not contain a four-part version: '$raw'"
    }
    return $raw
}

# --- copying ------------------------------------------------------------------------------------

<#
Replaces the bare `xcopy /Y /Q` calls the .bat scripts used. Those reported success even when
nothing was copied; this returns a count and the caller checks it.
#>
function Copy-Files {
    param(
        [string[]]$Path,
        [string]$Destination,
        [switch]$WhatIfOnly
    )
    $files = @()
    foreach ($p in $Path) {
        $files += @(Get-ChildItem -Path $p -File -ErrorAction SilentlyContinue)
    }
    if ($files.Count -eq 0) { return 0 }

    if ($WhatIfOnly) {
        # Summarise. A Release payload is ~200 assemblies, and listing every one would scroll the
        # Release-Wizard's confirmation prompt off the screen.
        if ($files.Count -le 4) {
            foreach ($f in $files) { Write-Info "would copy $($f.Name) -> $Destination" }
        }
        else {
            $sample = ($files | Select-Object -First 3 | ForEach-Object { $_.Name }) -join ', '
            Write-Info "would copy $($files.Count) files ($sample, ...) -> $Destination"
        }
        return $files.Count
    }

    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    foreach ($f in $files) {
        Copy-Item -LiteralPath $f.FullName -Destination $Destination -Force
    }
    return $files.Count
}

<#
Files that arrive from SharePoint/OneDrive carry a Mark-of-the-Web alternate data stream, and .NET
refuses to load a blocked assembly inside Revit. installer.bat offered this as an optional prompt;
here it is unconditional, because a blocked DLL is never what anyone wants.
#>
function Unblock-Payload {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return }
    Get-ChildItem -LiteralPath $Path -Recurse -File -Include *.dll, *.exe -ErrorAction SilentlyContinue |
        Unblock-File -ErrorAction SilentlyContinue
}
