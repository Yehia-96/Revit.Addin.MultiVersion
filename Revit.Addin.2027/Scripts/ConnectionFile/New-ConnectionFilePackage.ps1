<#
.SYNOPSIS
    Assembles the folder the office PCs get their connection file from. Developer side.

.DESCRIPTION
    Takes the office's connection file, which the database session's login script writes, and puts it into a folder
    with everything needed to install it on a PC:

        database.json                   the connection file: it holds the read-only login's password
        Install-ConnectionFile.ps1      the installer
        _ConnectionFile.ps1             helpers it needs
        Install connection file.cmd     double-clickable wrapper
        INSTALLATION.txt                how to

    A fixed folder, cleared and rewritten each time, beside the add-in's packages and outside their year folders, which
    New-Package.ps1 clears. Only you and administrators may open it. It never makes a ZIP and never goes to the share:
    the password must not end up in Teams, a mail or a repository. Hand it over on a stick.

    Nothing here touches the share, the local Revit installation or this PC's own connection file.

.PARAMETER SourceFile
    The office's connection file.

.PARAMETER OutputRoot
    Where the folder is made.

.PARAMETER WhatIfOnly
    Shows what would be done, and writes nothing.

.EXAMPLE
    pwsh -File Scripts\ConnectionFile\New-ConnectionFilePackage.ps1 -WhatIfOnly
#>
[CmdletBinding()]
param(
    [string]$SourceFile = 'C:\Yehia\ProjectsApps\Revit.Addin\database.json',
    [string]$OutputRoot = 'C:\Yehia\ProjectsApps\Revit.Addin\ConnectionFile',
    [switch]$WhatIfOnly
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_ConnectionFile.ps1')

$MarkerName = '.mh-connection-file.json'
$MarkerText = '{ "product": "MH.RevitTools connection file" }'
$Scripts = 'Install-ConnectionFile.ps1', '_ConnectionFile.ps1'
$Expected = @($MarkerName, $ConnectionFileName, 'Install connection file.cmd', 'INSTALLATION.txt') + $Scripts

# The password stays out of everything that's shared or versioned, whatever -OutputRoot says.
function Assert-PrivatePlace {
    param([string]$Path)

    $segments = @($Path.Split([IO.Path]::DirectorySeparatorChar) | Where-Object { $_ })
    if ($Path.StartsWith('\\') -or $segments.Count -lt 3) { throw "'$Path' isn't a place for it: a local folder, not too close to the drive root." }
    if ($Path -match '~\d') { throw "'$Path' is a short name. Give the folder's full name." }
    $drive = New-Object IO.DriveInfo([IO.Path]::GetPathRoot($Path))
    if ($drive.DriveType -ne [IO.DriveType]::Fixed) { throw "'$Path' isn't on a fixed drive of this PC ($($drive.DriveType)). The password must not go there." }
    foreach ($shared in @($env:OneDrive, $env:OneDriveCommercial, $env:OneDriveConsumer) | Where-Object { $_ }) {
        if (($Path + '\').StartsWith($shared.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "'$Path' is synchronized with OneDrive. The password must not go there." }
    }
    if ($Path -match 'Hereth|OneDrive|SharePoint') { throw "'$Path' looks like the office share or a synchronized folder. The password must not go there." }
    for ($folder = $Path; $folder; $folder = Split-Path -Parent $folder) {
        if (Test-Path -LiteralPath (Join-Path $folder '.git')) { throw "'$Path' is inside the repository $folder. The password must not go there." }
        Assert-NotLinked $folder
    }
}

try {
    Write-Banner 'Package the MH database connection file' 'for the office PCs'
    $SourceFile = [IO.Path]::GetFullPath($SourceFile)
    $OutputRoot = [IO.Path]::GetFullPath($OutputRoot).TrimEnd('\')
    Assert-PrivatePlace $OutputRoot

    $bytes = Read-ConnectionBytes $SourceFile
    $settings = Get-OfficeConnection $SourceFile $bytes
    Write-Info "from: $SourceFile"
    Write-Info "to  : $OutputRoot"
    Show-Connection $settings

    # Only a folder this script made is ever cleared, and only of what it puts there.
    $existing = @()
    if (Test-Path -LiteralPath $OutputRoot) {
        $existing = @(Get-ChildItem -LiteralPath $OutputRoot -Force)
        $marker = Join-Path $OutputRoot $MarkerName
        if ($existing.Count -and -not ((Test-Path -LiteralPath $marker -PathType Leaf) -and ([IO.File]::ReadAllText($marker).Trim() -eq $MarkerText))) {
            throw "'$OutputRoot' isn't empty and wasn't made by this script. It holds: $((($existing | Select-Object -First 5 | ForEach-Object { $_.Name })) -join ', ')."
        }
        $strange = @($existing | Where-Object { $_.PSIsContainer -or $_.Name -notin $Expected })
        if ($strange.Count) { throw "'$OutputRoot' holds something this script never puts there: $(($strange | ForEach-Object { $_.Name }) -join ', ')." }
        Assert-NotLinked @($existing | ForEach-Object { $_.FullName })
    }

    if ($WhatIfOnly) {
        Write-Info "would write $ConnectionFileName, $($Scripts -join ', '), Install connection file.cmd and INSTALLATION.txt"
        Write-Info 'would let only you and administrators open the folder'
        Write-Info 'preview, nothing written'
        exit 0
    }

    $me = Get-CurrentSid
    if (Test-Path -LiteralPath $OutputRoot) {
        foreach ($item in $existing) { [IO.File]::Delete($item.FullName) }
        [IO.Directory]::Delete($OutputRoot, $false)
    }
    # Closed to others from the start, before the password goes in.
    New-FolderWithSecurity $OutputRoot (New-FolderSecurity -FullControl $me)

    $MarkerText | Set-Content -LiteralPath (Join-Path $OutputRoot $MarkerName) -Encoding ASCII
    foreach ($name in $Scripts) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $OutputRoot }
    Write-NewFile (Join-Path $OutputRoot $ConnectionFileName) $bytes

    @"
@echo off
REM Puts the MH database connection file on this PC. Right-click -> Run as administrator.
REM Windows PowerShell with its own modules only, whatever it's started from.
set "PSModulePath="
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-ConnectionFile.ps1" %*
set "INSTALL_EXIT=%ERRORLEVEL%"
echo.
pause
exit /b %INSTALL_EXIT%
"@ | Set-Content -LiteralPath (Join-Path $OutputRoot 'Install connection file.cmd') -Encoding ASCII

    @"
MH database connection file, for MH.RevitTools in Revit

Once per PC, for every Revit year and everyone who uses the PC:

1. Run it from the stick: right-click "Install connection file.cmd" and choose "Run as administrator".
   Any administrator account will do. Revit may stay open.
2. In Revit: Parameter Tools, Database panel, the arrow below it, Test connection.
3. If you copied this folder to the PC, delete it there: database.json holds a password.

The add-in itself is installed as before, with its own Install.cmd or Check for Updates.
Keep this folder off the share, out of Teams and out of mails.
"@ | Set-Content -LiteralPath (Join-Path $OutputRoot 'INSTALLATION.txt') -Encoding ASCII

    # --- check -----------------------------------------------------------------------------------

    foreach ($name in $Scripts) {
        $copied = [IO.File]::ReadAllBytes((Join-Path $OutputRoot $name))
        if ((Get-BytesHash $copied) -ne (Get-BytesHash ([IO.File]::ReadAllBytes((Join-Path $PSScriptRoot $name))))) { throw "$name wasn't copied completely." }
    }
    $file = Join-Path $OutputRoot $ConnectionFileName
    if ((Get-BytesHash (Read-ConnectionBytes $file)) -ne (Get-BytesHash $bytes)) { throw "$ConnectionFileName wasn't copied completely." }
    $names = @(Get-ChildItem -LiteralPath $OutputRoot -Force | ForEach-Object { $_.Name })
    if (Compare-Object $Expected $names) { throw "'$OutputRoot' holds something else than it should: $($names -join ', ')." }
    # Nobody else may open it at all, let alone change it.
    foreach ($path in $OutputRoot, $file) {
        $strangers = @((Get-PathSecurity $path).GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier]) |
            ForEach-Object { $_.IdentityReference.Value } | Where-Object { $_ -notin @($SidSystem, $SidAdministrators, $me) })
        if ($strangers.Count) { throw "'$path' can be opened by others too: $($strangers -join ', ')." }
    }

    Write-Ok "Ready: $OutputRoot"
    Write-Info 'Copy it to a stick. On each PC: "Install connection file.cmd", Run as administrator.'
    Write-Warn 'It holds the password: not in Teams, not in a mail, not in a ZIP on the share.'
    exit 0
}
catch {
    Write-Bad $_.Exception.Message
    exit 1
}
