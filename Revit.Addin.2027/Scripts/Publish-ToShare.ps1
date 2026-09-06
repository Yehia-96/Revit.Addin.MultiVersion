<#
.SYNOPSIS
    Publishes a release to the Teams/SharePoint update share. Developer side.

.DESCRIPTION
    Replaces LocalToServer.bat, and takes over the release half of postbuild.bat.

    The old script was hardcoded to D:\Database_parameters\... -- a drive that does not exist on
    this machine -- and copied from bin\Debug, so it shipped Debug builds when it ran at all.
    This one resolves everything from $PSScriptRoot and refuses anything but a Release build
    unless you insist.

    Share layout is unchanged, so existing installations and the ribbon's "Check for Updates"
    keep working:

        Versions\<year>\version-docs.xlsx           changelog workbook
        Versions\<year>\dependencies\               .addin manifest + version.txt
        Versions\<year>\dependencies\MH.RevitTools\   assemblies

    Publishing is never automatic. Nothing is written without -Confirm:$false or an explicit yes.

.PARAMETER Configuration
    Which build output to publish. Release by default; Debug requires -AllowDebug.

.PARAMETER WhatIfOnly
    Show exactly what would be copied where, and write nothing.

.EXAMPLE
    pwsh -File Scripts\Publish-ToShare.ps1 -WhatIfOnly
.EXAMPLE
    pwsh -File Scripts\Publish-ToShare.ps1 -Confirm:$false
#>
[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [int]$RevitYear = 0,
    [string]$Configuration = 'Release',
    [string]$SourceDir,
    [string]$ShareRoot,
    [switch]$AllowDebug,
    [switch]$WhatIfOnly
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_Common.ps1')

$ctx = Get-AddinContext -ScriptRoot $PSScriptRoot -RevitYear $RevitYear

if ($Configuration -ne 'Release' -and -not $AllowDebug) {
    throw "Refusing to publish a $Configuration build to the office share. Pass -AllowDebug if you really mean it."
}

if (-not $SourceDir) { $SourceDir = Join-Path $ctx.ProjectDir "bin\$Configuration" }
$SourceDir = $SourceDir.TrimEnd('\', '/')
if (-not (Test-Path -LiteralPath $SourceDir)) {
    throw "Build output not found: $SourceDir. Build $Configuration first."
}

$assembly = Join-Path $SourceDir "$($ctx.ProjectName).dll"
if (-not (Test-Path -LiteralPath $assembly)) {
    throw "$($ctx.ProjectName).dll not found in $SourceDir."
}

$version = Read-ProjectVersion -Context $ctx

# The assembly and version.txt must agree, or colleagues are told a version they did not receive.
$fileVersion = (Get-Item -LiteralPath $assembly).VersionInfo.FileVersion
if ($fileVersion -and $fileVersion.Trim() -ne $version) {
    Write-Warn "version.txt says $version but $($ctx.ProjectName).dll reports $fileVersion."
    Write-Warn 'Rebuild after bumping the version, or the update check will advertise the wrong build.'
}

if ($ShareRoot) {
    $shareVersionRoot = $ShareRoot
    $shareDepsRoot = Join-Path $ShareRoot 'dependencies'
}
else {
    $shareVersionRoot = $ctx.ShareVersionRoot
    $shareDepsRoot = $ctx.ShareDepsRoot
}
$shareDllDir = Join-Path $shareDepsRoot $ctx.PayloadFolder

Write-Banner "Publish $($ctx.ProjectName) v$version" "Revit $($ctx.RevitYear)  |  $Configuration"
Write-Info "from : $SourceDir"
Write-Info "to   : $shareVersionRoot"

if ($WhatIfOnly) {
    Copy-Files -Path (Join-Path $SourceDir '*.dll') -Destination $shareDllDir -WhatIfOnly | Out-Null
    Copy-Files -Path $ctx.AddinFile -Destination $shareDepsRoot -WhatIfOnly | Out-Null
    Copy-Files -Path $ctx.VersionFile -Destination $shareDepsRoot -WhatIfOnly | Out-Null
    Copy-Files -Path $ctx.VersionDocsFile -Destination $shareVersionRoot -WhatIfOnly | Out-Null
    Write-Info 'dry run, nothing written'
    exit 0
}

if (-not (Test-Path -LiteralPath $shareVersionRoot)) {
    throw "Share not found: $shareVersionRoot`nIs the 31 BIM folder synced in Teams?"
}

if (-not $PSCmdlet.ShouldProcess($shareVersionRoot, "Publish v$version for Revit $($ctx.RevitYear) to the office share")) {
    Write-Info 'cancelled, nothing written'
    exit 0
}

$dlls = Copy-Files -Path (Join-Path $SourceDir '*.dll') -Destination $shareDllDir
if ($dlls -eq 0) { throw "No DLLs found in $SourceDir." }
Write-Step "$dlls assemblies -> $shareDllDir"

if ((Copy-Files -Path $ctx.AddinFile -Destination $shareDepsRoot) -eq 0) {
    throw "Manifest not found: $($ctx.AddinFile)"
}
Write-Step "$($ctx.AddinFileName) -> $shareDepsRoot"

if ((Copy-Files -Path $ctx.VersionFile -Destination $shareDepsRoot) -eq 0) {
    throw "version.txt not found: $($ctx.VersionFile)"
}
Write-Step "version.txt ($version) -> $shareDepsRoot"

# The changelog workbook sits one level up from dependencies, where the old postbuild.bat put it.
if (Test-Path -LiteralPath $ctx.VersionDocsFile) {
    Copy-Files -Path $ctx.VersionDocsFile -Destination $shareVersionRoot | Out-Null
    Write-Step "version-docs.xlsx -> $shareVersionRoot"
}
else {
    Write-Warn 'version-docs.xlsx not found; changelog not updated on the share.'
}

Write-Ok "Published v$version for Revit $($ctx.RevitYear)."
Write-Info 'Colleagues get it from the ribbon: Check for Updates.'
