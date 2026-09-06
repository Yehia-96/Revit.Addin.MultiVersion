<#
.SYNOPSIS
    Walks a maintainer through a complete release: bump, build, verify, publish.

.DESCRIPTION
    A coordinator, not an implementation. Every real step is delegated to the script that already
    owns it -- Bump-Version.ps1, msbuild, Publish-ToShare.ps1 -- so there is exactly one definition
    of how a bump, a build or a publish works.

    This is what replaces the old .csproj PreBuild hook. That hook launched version_increment.ps1
    with `start "" powershell -NoExit`, which detaches: MSBuild carried straight on and compiled
    the previous AssemblyInfo.cs while the version prompt was still waiting for input. Doing the
    steps here, in order, in one process, removes the race entirely.

    Nothing reaches the office share without an explicit confirmation, and every checkpoint can be
    cancelled. Colleagues are unaffected until the final step runs.

.EXAMPLE
    pwsh -File Scripts\Release-Wizard.ps1
.EXAMPLE
    pwsh -File Scripts\Release-Wizard.ps1 -Part patch -Message "Norm column width" -Configuration Release
#>
[CmdletBinding()]
param(
    [int]$RevitYear = 0,
    [ValidateSet('major', 'minor', 'patch')][string]$Part,
    [string]$Message,
    [string]$Configuration = 'Release',
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_Common.ps1')

$ctx = Get-AddinContext -ScriptRoot $PSScriptRoot -RevitYear $RevitYear
$total = 5

function Write-Stage {
    param([int]$Number, [string]$Title)
    Write-Host ''
    Write-Host ("  [{0}/{1}]  {2}" -f $Number, $total, $Title) -ForegroundColor Cyan
    Write-Host ('  ' + ('-' * 68)) -ForegroundColor DarkGray
}

function Confirm-Or-Exit {
    param([string]$Question)
    $answer = Read-Host "  $Question [y/N]"
    if ($answer -notmatch '^(y|yes)$') {
        Write-Host '  Cancelled. Nothing further was changed.' -ForegroundColor Yellow
        exit 0
    }
}

Write-Banner "Release $($ctx.ProjectName)" "Revit $($ctx.RevitYear)  |  $($ctx.ProjectDir)"

# --- 1. preflight -------------------------------------------------------------------------------

Write-Stage 1 'Preflight'
$startVersion = Read-ProjectVersion -Context $ctx
Write-Step "current version : $startVersion"
Write-Step "share           : $($ctx.ShareVersionRoot)"

if (Get-Process -Name 'Revit' -ErrorAction SilentlyContinue) {
    Write-Warn 'Revit is running. The build will succeed but the local deploy step will be skipped.'
}
if (-not (Test-Path -LiteralPath $ctx.ShareVersionRoot)) {
    Write-Warn "Share not reachable at $($ctx.ShareVersionRoot) - publishing will fail until Teams syncs."
}

# --- 2. bump ------------------------------------------------------------------------------------

Write-Stage 2 'Version'
$bumpArgs = @{ RevitYear = $ctx.RevitYear }
if ($Part) { $bumpArgs['Part'] = $Part }
if ($Message) { $bumpArgs['Message'] = $Message }
& (Join-Path $PSScriptRoot 'Bump-Version.ps1') @bumpArgs
if ($LASTEXITCODE) { throw "Version bump failed (exit $LASTEXITCODE)." }

$newVersion = Read-ProjectVersion -Context $ctx
if ($newVersion -eq $startVersion) { throw "Version did not change; still $startVersion." }

# --- 3. build -----------------------------------------------------------------------------------

Write-Stage 3 "Build $Configuration"
if ($SkipBuild) {
    Write-Info 'skipped (-SkipBuild)'
}
else {
    $projectFile = Join-Path $ctx.ProjectDir "$($ctx.ProjectName).csproj"
    if (-not (Test-Path -LiteralPath $projectFile)) { throw "Project file not found: $projectFile" }

    # msbuild for the .NET Framework project (2024); dotnet build for the SDK-style ones.
    $isSdkStyle = (Get-Content -LiteralPath $projectFile -Raw) -match '<Project\s+Sdk='
    if ($isSdkStyle) {
        Write-Step "dotnet build -c $Configuration"
        & dotnet build $projectFile -c $Configuration
    }
    else {
        $msbuild = Get-Command 'msbuild.exe' -ErrorAction SilentlyContinue
        if (-not $msbuild) {
            throw "msbuild.exe is not on PATH. Open a Developer PowerShell for Visual Studio, or build $($ctx.ProjectName) in the IDE and re-run with -SkipBuild."
        }
        Write-Step "msbuild /p:Configuration=$Configuration"
        & $msbuild.Source $projectFile "/p:Configuration=$Configuration" '/v:minimal' '/nologo'
    }
    if ($LASTEXITCODE) { throw "Build failed (exit $LASTEXITCODE)." }
    Write-Ok "built $Configuration"
}

# --- 4. verify ----------------------------------------------------------------------------------

Write-Stage 4 'Verify'
$outputDir = Join-Path $ctx.ProjectDir "bin\$Configuration"
$assembly = Join-Path $outputDir "$($ctx.ProjectName).dll"
if (-not (Test-Path -LiteralPath $assembly)) { throw "Build output missing: $assembly" }

$fileVersion = (Get-Item -LiteralPath $assembly).VersionInfo.FileVersion
if ($fileVersion) { $fileVersion = $fileVersion.Trim() }
Write-Step "version.txt : $newVersion"
Write-Step "assembly    : $fileVersion"

# This is the check the detached PreBuild made impossible. If these disagree, the build predates
# the bump and publishing it would advertise a version nobody actually receives.
if ($fileVersion -and $fileVersion -ne $newVersion) {
    throw "Assembly reports $fileVersion but version.txt says $newVersion. The build is stale - rebuild before publishing."
}
Write-Ok 'version.txt and the built assembly agree'

# --- 5. publish ---------------------------------------------------------------------------------

Write-Stage 5 'Publish to the office share'
Write-Step "This makes v$newVersion available to everyone on Revit $($ctx.RevitYear)."
& (Join-Path $PSScriptRoot 'Publish-ToShare.ps1') -RevitYear $ctx.RevitYear -Configuration $Configuration -WhatIfOnly

Confirm-Or-Exit "Publish v$newVersion to $($ctx.ShareVersionRoot)?"

& (Join-Path $PSScriptRoot 'Publish-ToShare.ps1') -RevitYear $ctx.RevitYear -Configuration $Configuration -Confirm:$false
if ($LASTEXITCODE) { throw "Publish failed (exit $LASTEXITCODE)." }

Write-Host ''
Write-Ok "Released $($ctx.ProjectName) v$newVersion for Revit $($ctx.RevitYear)."
