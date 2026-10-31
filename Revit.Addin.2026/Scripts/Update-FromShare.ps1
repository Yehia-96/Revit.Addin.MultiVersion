<#
.SYNOPSIS
    Installs or updates the add-in from the Teams/SharePoint share. User side.

.DESCRIPTION
    Replaces run_plugin_update.bat, which pointed at X:\Abdelazziz\Plugin_Versions -- a mapped
    drive that no longer exists -- and was already marked "not used but kept for reference".

    This is what the ribbon's "Check for Updates" hands off to, and what a colleague can run
    directly. Three things it does that the old xcopy did not:

      1. Refuses to run while Revit is open. Revit locks loaded assemblies, so updating underneath
         it leaves a mismatched set of DLLs that fails at load time.
      2. Unblocks everything it copies. Files synced from SharePoint carry a Mark-of-the-Web and
         .NET will not load a blocked assembly inside Revit.
      3. Compares versions first, so a no-op update costs nothing and says so.

.PARAMETER Force
    Reinstall even when the installed version already matches the share.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File Scripts\Update-FromShare.ps1
#>
[CmdletBinding()]
param(
    [int]$RevitYear = 0,
    [string]$ShareRoot,
    [switch]$Force,
    [switch]$WhatIfOnly
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_Common.ps1')

$ctx = Get-AddinContext -ScriptRoot $PSScriptRoot -RevitYear $RevitYear

if ($ShareRoot) { $shareDepsRoot = Join-Path $ShareRoot 'dependencies' }
else { $shareDepsRoot = $ctx.ShareDepsRoot }
$shareDllDir = Join-Path $shareDepsRoot $ctx.PayloadFolder

# Installs into both roots, like Install.ps1. The version check reads the ProgramData copy: both
# manifests point at it, so it is the assembly Revit actually loads.
$installDir = Get-PayloadDir -AddinRoot $ctx.ProgramDataAddinRoot -Context $ctx
$manifestOnShare = Join-Path $shareDepsRoot $ctx.AddinFileName

Write-Banner "Update $($ctx.ProjectName)" "Revit $($ctx.RevitYear)"
Write-Info "share  : $shareDepsRoot"
Write-Info "install: $($ctx.ProgramDataAddinRoot)"
Write-Info "         $($ctx.AppDataAddinRoot)"

if (-not (Test-Path -LiteralPath $shareDepsRoot)) {
    throw "Cannot reach the update share:`n  $shareDepsRoot`nOpen Teams and sync the 31 BIM folder, then try again."
}

# --- version comparison -------------------------------------------------------------------------

$shareVersionFile = Join-Path $shareDepsRoot 'version.txt'
if (-not (Test-Path -LiteralPath $shareVersionFile)) {
    throw "version.txt missing on the share at $shareVersionFile."
}
$shareVersion = (Get-Content -LiteralPath $shareVersionFile -Raw).Trim()

$installedVersion = 'none'
$installedAssembly = Join-Path $installDir "$($ctx.ProjectName).dll"
if (Test-Path -LiteralPath $installedAssembly) {
    $fv = (Get-Item -LiteralPath $installedAssembly).VersionInfo.FileVersion
    if ($fv) { $installedVersion = $fv.Trim() }
}

Write-Step "installed $installedVersion  ->  available $shareVersion"

# When both roots hold the manifest, Revit reads the per-user (AppData) one and skips the
# all-users one as "duplicated". So a stale AppData manifest would win even with a current
# ProgramData install - do not call that up to date. A missing AppData manifest is fine: Revit
# then reads the ProgramData one.
$appDataManifest = Join-Path $ctx.AppDataAddinRoot $ctx.AddinFileName
$appDataStale = $false
if ((Test-Path -LiteralPath $appDataManifest) -and (Test-Path -LiteralPath $manifestOnShare)) {
    $appDataStale = (Get-FileHash -LiteralPath $appDataManifest).Hash -ne (Get-FileHash -LiteralPath $manifestOnShare).Hash
}
if ($appDataStale) { Write-Warn 'the per-user manifest differs from the share; it will be refreshed.' }

if ($installedVersion -eq $shareVersion -and -not $appDataStale -and -not $Force) {
    Write-Ok 'Already up to date.'
    exit 0
}

# --- guards -----------------------------------------------------------------------------------

Assert-RevitNotRunning | Out-Null

if (-not (Test-Path -LiteralPath $manifestOnShare)) {
    throw "Manifest not found on the share: $manifestOnShare"
}

# --- copy ---------------------------------------------------------------------------------------

# Both roots: ProgramData (required) then AppData. Legacy PluginTrail\ cleanup, payload, manifest
# and unblocking all happen per root, exactly as Install.ps1 does.
$written = Install-ToAddinRoots -Context $ctx -PayloadSource $shareDllDir -ManifestPath $manifestOnShare -WhatIfOnly:$WhatIfOnly

if ($WhatIfOnly) { Write-Info 'dry run, nothing written'; exit 0 }

Write-Ok "Updated to $shareVersion in $written add-in folder(s). Start Revit $($ctx.RevitYear) to use it."
