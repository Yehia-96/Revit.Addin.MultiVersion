<#
.SYNOPSIS
    Puts the MH database's connection file on this PC and locks it. Run as administrator.

.DESCRIPTION
    The database link in Revit (MH.RevitTools) reads C:\ProgramData\MH\RevitTools\database.json: which server and
    database, and the read-only login's password. One file per PC, for every Revit year. This script puts it there
    from the copy it's handed over with, and locks the folder, so that only administrators can change it. Everyone on
    the PC can read it, as Revit runs as the person who uses it.

    It is separate from the add-in's own Install.ps1 on purpose:

      * A PC that already has the add-in needs only this, once. Check for Updates never touches the file.
      * This needs administrator rights. The add-in for Revit 2027 is registered per person, so its own installer has
        to run as that person.
      * The add-in's package and ZIP never hold the password.

    The lock matters because any user may create files under C:\ProgramData. Without it, someone could put a file
    there first, or own the folder, and point Revit at another server.

    It never shows the password: not on screen, not in a preview, not in an error.

.PARAMETER SourceFile
    The connection file to install. Defaults to database.json beside this script.

.PARAMETER Force
    Replaces a file for another server or database, as on a developer's PC. Without it, that stops.

.PARAMETER TrialFolder
    Another folder instead, e.g. C:\Temp\try\MH\RevitTools, to try it out without administrator rights. Who owns it is
    then left alone, and you may change it. Not for a real PC.

.PARAMETER WhatIfOnly
    Shows what would be done, and writes nothing.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File Install-ConnectionFile.ps1
.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File Install-ConnectionFile.ps1 -WhatIfOnly
#>
[CmdletBinding()]
param(
    [string]$SourceFile,
    [string]$TrialFolder,
    [switch]$Force,
    [switch]$WhatIfOnly
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_ConnectionFile.ps1')

# A colleague reads this, so a problem is one plain line rather than PowerShell's own error.
try {
    Write-Banner 'Install the MH database connection file' 'for MH.RevitTools, every Revit year on this PC'

    if (-not $SourceFile) { $SourceFile = Join-Path $PSScriptRoot $ConnectionFileName }
    $SourceFile = [IO.Path]::GetFullPath($SourceFile)
    $programData = [Environment]::GetFolderPath('CommonApplicationData')
    $folder = Join-Path $programData 'MH\RevitTools'
    $isTrial = [bool]$TrialFolder
    if ($isTrial) {
        $folder = [IO.Path]::GetFullPath($TrialFolder).TrimEnd('\')
        $above = Split-Path -Parent $folder
        if (-not $above -or -not (Split-Path -Parent $above) -or ($folder + '\').StartsWith($programData + '\', [StringComparison]::OrdinalIgnoreCase)) {
            throw "$folder isn't a place to try it: a folder of its own, two levels deep, and not where the real one goes."
        }
    }
    elseif (-not $WhatIfOnly -and -not (Test-Administrator)) {
        throw 'This needs administrator rights. Right-click "Install connection file.cmd" and choose "Run as administrator".'
    }

    Install-ConnectionFile -Source $SourceFile -Folder $folder -Replace:$Force -Preview:$WhatIfOnly -Trial:$isTrial

    if (-not $WhatIfOnly -and -not $isTrial) {
        Write-Info 'Check it in Revit: Database panel, the arrow below it, Test connection. Revit may stay open.'
        Write-Warn "If you copied $SourceFile to this PC, delete it now: it holds the password."
    }
    exit 0
}
catch {
    Write-Bad $_.Exception.Message
    exit 1
}
