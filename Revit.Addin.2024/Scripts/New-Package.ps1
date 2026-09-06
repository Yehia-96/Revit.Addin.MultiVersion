<#
.SYNOPSIS
    Assembles a self-contained folder a colleague can install from, and optionally zips it.

.DESCRIPTION
    The missing half of Install.ps1: Install.ps1 expects a folder laid out a particular way, and
    this is what lays it out. Modelled on MH.IfcCustomExport's Build-Package.ps1.

    Produces, under <repo>\artifacts\:

        Revit.Addin.<year>-<version>\
            MH.RevitTools\*.dll          the assemblies
            Revit.Addin.<year>.addin   the manifest
            version.txt                what this build is
            Install.ps1                the installer
            Install.cmd               double-clickable wrapper
            _Common.ps1                helpers Install.ps1 needs
            README.md                  what it is and how to install it

    Hand over the folder, or the .zip if -Zip was used. Nothing here touches the office share or
    the local Revit installation -- it only writes into artifacts\.

.PARAMETER Zip
    Also produce Revit.Addin.<year>-<version>.zip beside the folder.

.PARAMETER SkipBuild
    Package whatever is already in bin\<Configuration> instead of building first.

.EXAMPLE
    pwsh -File Scripts\New-Package.ps1 -Zip
.EXAMPLE
    pwsh -File Scripts\New-Package.ps1 -Configuration Debug -SkipBuild
#>
[CmdletBinding()]
param(
    [int]$RevitYear = 0,
    [string]$Configuration = 'Release',
    [string]$OutputRoot,
    [switch]$Zip,
    [switch]$SkipBuild,
    [switch]$WhatIfOnly
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_Common.ps1')

$ctx = Get-AddinContext -ScriptRoot $PSScriptRoot -RevitYear $RevitYear
$repoRoot = Split-Path -Parent $ctx.ProjectDir

if (-not $OutputRoot) { $OutputRoot = Join-Path $repoRoot 'artifacts' }

# Refuse to write outside the repository. A mistyped -OutputRoot should not be able to delete an
# arbitrary folder, and this function does delete its target before rebuilding it.
$repoFull = [IO.Path]::GetFullPath($repoRoot)
$outFull = [IO.Path]::GetFullPath($OutputRoot)
if (-not $outFull.StartsWith($repoFull, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to package outside the repository:`n  $outFull`nis not under`n  $repoFull"
}

Write-Banner "Package $($ctx.ProjectName)" "Revit $($ctx.RevitYear)  |  $Configuration"

# --- build --------------------------------------------------------------------------------------

$projectFile = Join-Path $ctx.ProjectDir "$($ctx.ProjectName).csproj"
if ($SkipBuild) {
    Write-Info 'build skipped (-SkipBuild)'
}
elseif ($WhatIfOnly) {
    Write-Info "would build $Configuration"
}
else {
    $isSdkStyle = (Get-Content -LiteralPath $projectFile -Raw) -match '<Project\s+Sdk='
    if ($isSdkStyle) {
        Write-Step "dotnet build -c $Configuration"
        & dotnet build $projectFile -c $Configuration
    }
    else {
        $msbuild = Get-Command 'msbuild.exe' -ErrorAction SilentlyContinue
        if (-not $msbuild) {
            throw "msbuild.exe is not on PATH. Open a Developer PowerShell for Visual Studio, or build in the IDE and re-run with -SkipBuild."
        }
        Write-Step "msbuild /p:Configuration=$Configuration"
        & $msbuild.Source $projectFile "/p:Configuration=$Configuration" '/v:minimal' '/nologo'
    }
    if ($LASTEXITCODE) { throw "Build failed (exit $LASTEXITCODE)." }
}

# --- locate the build output ----------------------------------------------------------------------

$buildDir = Join-Path $ctx.ProjectDir "bin\$Configuration"
if (-not (Test-Path -LiteralPath $buildDir)) { throw "Build output not found: $buildDir" }

$assembly = Join-Path $buildDir "$($ctx.ProjectName).dll"
if (-not (Test-Path -LiteralPath $assembly)) { throw "$($ctx.ProjectName).dll not found in $buildDir." }

$version = (Get-Item -LiteralPath $assembly).VersionInfo.FileVersion
if ($version) { $version = $version.Trim() } else { $version = Read-ProjectVersion -Context $ctx }

$declared = Read-ProjectVersion -Context $ctx
if ($version -ne $declared) {
    Write-Warn "version.txt says $declared but the assembly reports $version."
    Write-Warn 'Packaging the assembly version. Rebuild if that is not what you meant to hand over.'
}

$packageName = "$($ctx.ProjectName)-$version"
$packageDir = Join-Path $OutputRoot $packageName

Write-Info "version : $version"
Write-Info "output  : $packageDir"

if ($WhatIfOnly) {
    $dllCount = @(Get-ChildItem -Path (Join-Path $buildDir '*.dll') -File -ErrorAction SilentlyContinue).Count
    Write-Info "would package $dllCount assemblies + manifest + installer"
    if ($Zip) { Write-Info "would zip to $packageDir.zip" }
    Write-Info 'dry run, nothing written'
    exit 0
}

# --- assemble -------------------------------------------------------------------------------------

if (Test-Path -LiteralPath $packageDir) { Remove-Item -LiteralPath $packageDir -Recurse -Force }
New-Item -ItemType Directory -Path $packageDir -Force | Out-Null

$payloadDir = Join-Path $packageDir $ctx.PayloadFolder
$dlls = Copy-Files -Path (Join-Path $buildDir '*.dll') -Destination $payloadDir
if ($dlls -eq 0) { throw "No DLLs found in $buildDir." }
Write-Step "$dlls assemblies -> $($ctx.PayloadFolder)\"

Copy-Files -Path $ctx.AddinFile -Destination $packageDir | Out-Null
Copy-Files -Path $ctx.VersionFile -Destination $packageDir | Out-Null
Write-Step "$($ctx.AddinFileName) + version.txt"

foreach ($f in 'Install.ps1', '_Common.ps1', 'README.md') {
    $src = Join-Path $PSScriptRoot $f
    if (Test-Path -LiteralPath $src) { Copy-Files -Path $src -Destination $packageDir | Out-Null }
}

# Colleagues used to double-click installer.bat. Keep that working: a .ps1 is not double-clickable,
# and telling a non-developer to set an execution policy is a good way to never get it installed.
$cmd = @"
@echo off
REM Installs $($ctx.ProjectName) for Revit $($ctx.RevitYear). Right-click -> Run as administrator
REM for an all-users install, or just double-click and answer the prompt for a per-user one.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install.ps1" %*
echo.
pause
"@
Set-Content -LiteralPath (Join-Path $packageDir 'Install.cmd') -Value $cmd -Encoding ASCII
Write-Step 'Install.ps1 + Install.cmd + README.md'

if ($Zip) {
    $zipPath = "$packageDir.zip"
    if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
    Compress-Archive -Path (Join-Path $packageDir '*') -DestinationPath $zipPath
    $mb = [math]::Round((Get-Item -LiteralPath $zipPath).Length / 1MB, 1)
    Write-Step "zipped -> $zipPath ($mb MB)"
}

Write-Host ''
Write-Ok "Packaged $($ctx.ProjectName) v$version."
Write-Info "Hand over: $packageDir"
Write-Info 'They run Install.cmd. Nothing was published to the share.'
