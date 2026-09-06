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
    [string]$AddinRoot,
    [switch]$Force,
    [switch]$WhatIfOnly
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_Common.ps1')

$ctx = Get-AddinContext -ScriptRoot $PSScriptRoot -RevitYear $RevitYear

if ($ShareRoot) { $shareDepsRoot = Join-Path $ShareRoot 'dependencies' }
else { $shareDepsRoot = $ctx.ShareDepsRoot }
$shareDllDir = Join-Path $shareDepsRoot $ctx.PayloadFolder

if (-not $AddinRoot) { $AddinRoot = $ctx.ProgramDataAddinRoot }
$installDir = Get-PayloadDir -AddinRoot $AddinRoot -Context $ctx

Write-Banner "Update $($ctx.ProjectName)" "Revit $($ctx.RevitYear)"
Write-Info "share  : $shareDepsRoot"
Write-Info "install: $AddinRoot"

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

if ($installedVersion -eq $shareVersion -and -not $Force) {
    Write-Ok 'Already up to date.'
    exit 0
}

# --- guards -----------------------------------------------------------------------------------

Assert-RevitNotRunning | Out-Null

if ($WhatIfOnly) {
    Remove-LegacyPayload -AddinRoot $AddinRoot -Context $ctx -WhatIfOnly | Out-Null
    Copy-Files -Path (Join-Path $shareDllDir '*.dll') -Destination $installDir -WhatIfOnly | Out-Null
    Copy-Files -Path (Join-Path $shareDepsRoot $ctx.AddinFileName) -Destination $AddinRoot -WhatIfOnly | Out-Null
    Write-Info 'dry run, nothing written'
    exit 0
}

if (-not (Test-Writable $AddinRoot)) {
    throw "Cannot write to $AddinRoot.`nRe-run this from an elevated PowerShell, or pass -AddinRoot `"$($ctx.AppDataAddinRoot)`" to install for yourself only."
}

# --- copy ---------------------------------------------------------------------------------------

# Same migration as Install.ps1: drop the previous release's PluginTrail\ before writing the new
# folder, so the machine is not left carrying both.
Remove-LegacyPayload -AddinRoot $AddinRoot -Context $ctx | Out-Null

$dlls = Copy-Files -Path (Join-Path $shareDllDir '*.dll') -Destination $installDir
if ($dlls -eq 0) { throw "No assemblies found on the share at $shareDllDir." }
Write-Step "$dlls assemblies -> $installDir"

$manifestOnShare = Join-Path $shareDepsRoot $ctx.AddinFileName
if ((Copy-Files -Path $manifestOnShare -Destination $AddinRoot) -eq 0) {
    throw "Manifest not found on the share: $manifestOnShare"
}
Write-Step "$($ctx.AddinFileName) -> $AddinRoot"

Write-Step 'unblocking files (Mark-of-the-Web)...'
Unblock-Payload -Path $installDir
Unblock-Payload -Path $AddinRoot

Write-Ok "Updated to $shareVersion. Start Revit $($ctx.RevitYear) to use it."
