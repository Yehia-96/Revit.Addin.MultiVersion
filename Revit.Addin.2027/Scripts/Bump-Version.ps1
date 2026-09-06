<#
.SYNOPSIS
    Raises the version in version.txt and AssemblyInfo.cs, and appends a changelog row.

.DESCRIPTION
    Replaces version_increment.ps1. What changed:

      * Paths come from $PSScriptRoot. The old script hardcoded
        C:\Yehia\Database_parameters\...\Revit.Addin.2024, which only worked on one machine and
        silently edited the wrong project when copied into another.
      * It printed $ServerDllDir and $ServerVersionFile, neither of which was ever assigned, and
        computed a $ServerDir it never used. All gone.
      * The Excel COM objects are released in a finally block. Previously an error between Open()
        and Quit() left an invisible EXCEL.EXE holding version-docs.xlsx open, so the next run
        failed on a locked file.
      * -Part and -Message make it scriptable, so Release-Wizard.ps1 can drive it without a human
        typing into a detached window mid-build.

    IMPORTANT: this no longer runs from the .csproj PreBuild event. It used to be launched with
    `start "" powershell -NoExit`, which detaches -- MSBuild did not wait, so the build compiled
    the OLD AssemblyInfo.cs while the prompt was still on screen. Bump first, then build.

.PARAMETER Part
    major, minor or patch. Prompts if omitted.

.PARAMETER Message
    Changelog text for version-docs.xlsx. Prompts if omitted.

.EXAMPLE
    pwsh -File Scripts\Bump-Version.ps1
.EXAMPLE
    pwsh -File Scripts\Bump-Version.ps1 -Part patch -Message "Fixed the Norm column width"
#>
[CmdletBinding()]
param(
    [int]$RevitYear = 0,
    [ValidateSet('major', 'minor', 'patch')][string]$Part,
    [string]$Message,
    [switch]$SkipChangelog,
    [switch]$WhatIfOnly
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_Common.ps1')

$ctx = Get-AddinContext -ScriptRoot $PSScriptRoot -RevitYear $RevitYear
$current = Read-ProjectVersion -Context $ctx

Write-Banner "Bump $($ctx.ProjectName)" "current version $current"

if (-not $Part) {
    Write-Host '    1. major   2. minor   3. patch'
    $answer = (Read-Host '  Which part?').Trim()
    switch ($answer) {
        '1' { $Part = 'major' }
        '2' { $Part = 'minor' }
        '3' { $Part = 'patch' }
        'major' { $Part = 'major' }
        'minor' { $Part = 'minor' }
        'patch' { $Part = 'patch' }
        default { throw "Expected 1, 2 or 3 (or major/minor/patch); got '$answer'." }
    }
}

$parts = $current.Split('.')
$major = [int]$parts[0]; $minor = [int]$parts[1]; $patch = [int]$parts[2]

switch ($Part) {
    'major' { $major++; $minor = 0; $patch = 0 }
    'minor' { $minor++; $patch = 0 }
    'patch' { $patch++ }
}
$new = "$major.$minor.$patch.0"

Write-Step "$current  ->  $new"

if (-not $SkipChangelog -and -not $Message) {
    $Message = (Read-Host "  What changed in $new").Trim()
    if (-not $Message) { throw 'A changelog message is required. Pass -SkipChangelog to omit it deliberately.' }
}

if ($WhatIfOnly) {
    Write-Info "would write $new to version.txt and AssemblyInfo.cs"
    if (-not $SkipChangelog) { Write-Info "would append changelog row: $new | $Message" }
    exit 0
}

# --- version.txt --------------------------------------------------------------------------------

Set-Content -LiteralPath $ctx.VersionFile -Value $new -NoNewline -Encoding UTF8
Write-Ok "version.txt -> $new"

# --- AssemblyInfo.cs ----------------------------------------------------------------------------
# Read and write as one string so the file's existing line endings and BOM survive untouched.
# A line-by-line rewrite here would flip CRLF to LF and produce a whole-file diff.

if (-not (Test-Path -LiteralPath $ctx.AssemblyInfoFile)) {
    throw "AssemblyInfo.cs not found at $($ctx.AssemblyInfoFile)"
}
$asm = Get-Content -LiteralPath $ctx.AssemblyInfoFile -Raw
$before = $asm
$asm = $asm -replace 'AssemblyVersion\("[^"]*"\)', "AssemblyVersion(`"$new`")"
$asm = $asm -replace 'AssemblyFileVersion\("[^"]*"\)', "AssemblyFileVersion(`"$new`")"
if ($asm -eq $before) { throw 'No AssemblyVersion attribute found to update in AssemblyInfo.cs.' }
[IO.File]::WriteAllText($ctx.AssemblyInfoFile, $asm)
Write-Ok "AssemblyInfo.cs -> $new"

# --- changelog workbook -------------------------------------------------------------------------

if ($SkipChangelog) {
    Write-Info 'changelog skipped'
}
elseif (-not (Test-Path -LiteralPath $ctx.VersionDocsFile)) {
    Write-Warn "version-docs.xlsx not found at $($ctx.VersionDocsFile); changelog not updated."
}
else {
    $excel = $null; $workbook = $null; $sheet = $null
    try {
        $excel = New-Object -ComObject Excel.Application
        $excel.Visible = $false
        $excel.DisplayAlerts = $false
        $workbook = $excel.Workbooks.Open($ctx.VersionDocsFile)
        $sheet = $workbook.Sheets.Item(1)
        $row = $sheet.UsedRange.Rows.Count + 1
        $sheet.Cells.Item($row, 1).Value2 = $new
        $sheet.Cells.Item($row, 2).Value2 = $Message
        $workbook.Save()
        Write-Ok "version-docs.xlsx row $row -> $new"
    }
    catch {
        Write-Warn "Could not update version-docs.xlsx: $($_.Exception.Message)"
        Write-Warn "Add the row by hand: $new | $Message"
    }
    finally {
        # Release in reverse order of acquisition. Without this an invisible EXCEL.EXE keeps the
        # workbook locked and the next bump fails on a file it cannot open.
        if ($workbook) { $workbook.Close($false) | Out-Null }
        if ($excel) { $excel.Quit() }
        foreach ($o in @($sheet, $workbook, $excel)) {
            if ($o) { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($o) }
        }
        [GC]::Collect(); [GC]::WaitForPendingFinalizers()
    }
}

Write-Host ''
Write-Ok "Now at $new. Build Release, then run Publish-ToShare.ps1."
