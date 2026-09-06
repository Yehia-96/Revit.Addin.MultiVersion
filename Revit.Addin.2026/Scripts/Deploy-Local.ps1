<#
.SYNOPSIS
    Copies a build into the local Revit add-ins folders. Runs after every build.

.DESCRIPTION
    Replaces postbuild.bat.

    One deliberate behaviour change: this script NEVER touches the office share. The old
    postbuild.bat published to the Teams/SharePoint update folder on every Release build, which
    meant an ordinary local Release build silently shipped to colleagues. Publishing is now an
    explicit act -- see Publish-ToShare.ps1 -- so building and releasing cannot be confused.

    Copies to both the per-user (%APPDATA%) and all-users (%ProgramData%) add-in roots, matching
    what postbuild.bat did, so Revit finds the add-in through whichever manifest it reads.

.PARAMETER SourceDir
    Build output folder, normally $(TargetDir) from MSBuild. A trailing backslash is tolerated.

.PARAMETER RevitYear
    Defaults to the year in the project folder name.

.PARAMETER FailIfRevitRunning
    By default a running Revit is reported and the deploy is skipped, leaving the build green.
    Use this to make it a hard error instead.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File Scripts\Deploy-Local.ps1 -SourceDir "bin\Debug"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$SourceDir,
    [int]$RevitYear = 0,
    [switch]$FailIfRevitRunning,
    [switch]$WhatIfOnly
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_Common.ps1')

$ctx = Get-AddinContext -ScriptRoot $PSScriptRoot -RevitYear $RevitYear

# MSBuild's $(TargetDir) ends in a backslash, so a post-build event written as
#   -SourceDir "$(TargetDir)"
# hands the shell ...\bin\Debug\" - where \" reads as an escaped quote, and the argument arrives
# with a trailing double-quote still attached. Strip that before the backslash, or Test-Path fails
# with "Illegal characters in path" and the build goes red after a successful compile.
$SourceDir = $SourceDir.Trim('"').TrimEnd('\', '/')
if (-not (Test-Path -LiteralPath $SourceDir)) { throw "Build output not found: $SourceDir" }

$assembly = Join-Path $SourceDir "$($ctx.ProjectName).dll"
if (-not (Test-Path -LiteralPath $assembly)) {
    throw "$($ctx.ProjectName).dll not found in $SourceDir. Build the project first."
}

Write-Banner "Deploy $($ctx.ProjectName) locally" "Revit $($ctx.RevitYear)  |  source: $SourceDir"

# A running Revit means locked assemblies. Warn and stop rather than half-copying, but keep the
# build green -- rebuilding with Revit open is a normal thing to do.
if (-not (Assert-RevitNotRunning -WarnOnly:(-not $FailIfRevitRunning))) { exit 0 }

$roots = @($ctx.AppDataAddinRoot, $ctx.ProgramDataAddinRoot)
$totalDlls = 0

foreach ($root in $roots) {
    $payloadDir = Get-PayloadDir -AddinRoot $root -Context $ctx

    if (-not $WhatIfOnly -and -not (Test-Writable $root)) {
        Write-Warn "not writable, skipped: $root"
        continue
    }

    # Cheap when there is nothing to do (one Test-Path), and it keeps a development machine in the
    # same state as a colleague's after the PluginTrail -> MH.RevitTools rename.
    Remove-LegacyPayload -AddinRoot $root -Context $ctx -WhatIfOnly:$WhatIfOnly | Out-Null

    $n = Copy-Files -Path (Join-Path $SourceDir '*.dll') -Destination $payloadDir -WhatIfOnly:$WhatIfOnly
    if ($n -eq 0) { throw "No DLLs found in $SourceDir." }
    $totalDlls = $n

    $m = Copy-Files -Path $ctx.AddinFile -Destination $root -WhatIfOnly:$WhatIfOnly
    if ($m -eq 0) { throw "Manifest not found: $($ctx.AddinFile)" }

    Write-Step "$n DLLs + $($ctx.AddinFileName) -> $root"
}

if ($WhatIfOnly) { Write-Info 'dry run, nothing written'; exit 0 }

Write-Ok "Deployed $totalDlls assemblies for Revit $($ctx.RevitYear). Share untouched."
