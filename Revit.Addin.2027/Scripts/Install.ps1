<#
.SYNOPSIS
    Installs the add-in from a folder handed to a colleague. First-time install.

.DESCRIPTION
    Replaces installer.bat, which had three problems worth naming:

      * `if not exist "%TARGET_ADDIN"` was missing its closing %, so the "is Revit installed?"
        guard never fired and the script happily installed for a Revit that was not there.
      * The banner advertised a PLUGIN_VERSION that was hardcoded and years stale. The version
        is now read from the payload itself, so it cannot lie.
      * Unblocking DLLs was an optional prompt. A blocked assembly will not load inside Revit,
        so it is now unconditional.

    Use this when someone has a copy of the folder. For the normal path -- pulling the current
    release off the Teams share -- use Update-FromShare.ps1 instead.

    Installs into both add-in roots, all-users %ProgramData% and per-user %APPDATA%, the same
    pair Deploy-Local.ps1 writes. ProgramData is required (the manifest points at that copy), so
    run elevated; AppData is skipped with a warning if it cannot be written.

    There used to be a -PerUser switch that installed to AppData only. It never worked on its
    own - the manifest it copied still pointed at the ProgramData DLL - so it is gone.

.PARAMETER SourceDir
    Folder holding the payload. Defaults to the folder this script sits beside, so the whole
    thing can be zipped and handed over as-is.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File Install.ps1
#>
[CmdletBinding()]
param(
    [int]$RevitYear = 0,
    [string]$SourceDir,
    [switch]$WhatIfOnly
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_Common.ps1')

$ctx = Get-AddinContext -ScriptRoot $PSScriptRoot -RevitYear $RevitYear

if (-not $SourceDir) { $SourceDir = $ctx.ProjectDir }
$SourceDir = $SourceDir.TrimEnd('\', '/')

# The payload may sit either loose in the folder or under MH.RevitTools\, depending on how it was
# handed over. Accept both rather than failing on a detail the recipient cannot be expected to know.
$payloadSource = Join-Path $SourceDir $ctx.PayloadFolder
if (-not (Test-Path -LiteralPath $payloadSource)) { $payloadSource = $SourceDir }

$assembly = Join-Path $payloadSource "$($ctx.ProjectName).dll"
if (-not (Test-Path -LiteralPath $assembly)) {
    throw "$($ctx.ProjectName).dll not found under $SourceDir. Is this the right folder?"
}

# Read the version out of the payload instead of hardcoding it.
$version = (Get-Item -LiteralPath $assembly).VersionInfo.FileVersion
if (-not $version) { $version = 'unknown' } else { $version = $version.Trim() }

$addinRoot = $ctx.ProgramDataAddinRoot

Write-Banner "Install $($ctx.ProjectName) v$version" "Revit $($ctx.RevitYear)"
Write-Info "from: $payloadSource"
Write-Info "to  : $($ctx.ProgramDataAddinRoot)"
Write-Info "      $($ctx.AppDataAddinRoot)"

# The guard installer.bat meant to have. The Addins\<year> folder is created by Revit's installer,
# so its absence is a good signal that this Revit version is not on the machine.
$revitAddinsParent = Split-Path -Parent $addinRoot
if (-not (Test-Path -LiteralPath $revitAddinsParent)) {
    throw "No Autodesk Revit add-ins folder at $revitAddinsParent.`nIs Revit installed on this machine?"
}
if (-not (Test-Path -LiteralPath $addinRoot)) {
    Write-Warn "Revit $($ctx.RevitYear) does not appear to be installed (no $addinRoot)."
    $answer = Read-Host "Install anyway? [y/N]"
    if ($answer -notmatch '^(y|yes)$') { Write-Info 'cancelled'; exit 0 }
}

Assert-RevitNotRunning | Out-Null

# The manifest lives beside the payload folder, not inside it.
$manifest = Join-Path $SourceDir $ctx.AddinFileName
if (-not (Test-Path -LiteralPath $manifest)) { $manifest = $ctx.AddinFile }

# Both roots: ProgramData (required) then AppData. Legacy cleanup and unblocking happen per root.
$written = Install-ToAddinRoots -Context $ctx -PayloadSource $payloadSource -ManifestPath $manifest -WhatIfOnly:$WhatIfOnly

if ($WhatIfOnly) { Write-Info 'dry run, nothing written'; exit 0 }

Write-Ok "Installed v$version into $written add-in folder(s). Start Revit $($ctx.RevitYear) and look for the Parameter Tools tab."
