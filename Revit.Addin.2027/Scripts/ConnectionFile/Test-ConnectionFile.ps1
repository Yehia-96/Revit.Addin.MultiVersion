<#
.SYNOPSIS
    Tests the connection file's scripts on throwaway folders. Needs no administrator rights and no modules.

.DESCRIPTION
    Never touches this PC's own connection file, the share or a database. The password in it is made up for the tests.
    What only an administrator can do (who owns the folder) isn't covered here; the walk on a second PC is.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File Test-ConnectionFile.ps1
.EXAMPLE
    pwsh -NoProfile -File Test-ConnectionFile.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_ConnectionFile.ps1')

$MadeUp = 'made-up-for-tests-7Qx'
$Engine = (Get-Process -Id $PID).Path
$Work = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) ('Temp\MH.ConnectionFileTests.' + [Guid]::NewGuid().ToString('N'))
$script:Passed = 0
$script:Failed = 0
$script:AllOutput = New-Object Text.StringBuilder

function Check {
    param([string]$What, [bool]$Ok)
    if ($Ok) { $script:Passed++ }
    else { $script:Failed++; Write-Host "  FAILED  $What" -ForegroundColor Red }
}

# Runs a script as it's really run, in a process of its own, and keeps everything it says.
function Invoke-Script {
    param([string]$Name, [string[]]$Arguments, [string]$Folder = $PSScriptRoot)
    $lines = & $Engine -NoProfile -ExecutionPolicy Bypass -File (Join-Path $Folder $Name) @Arguments 2>&1 | Out-String
    $code = $LASTEXITCODE
    [void]$script:AllOutput.AppendLine($lines)
    return [pscustomobject]@{ Output = ($lines -replace '\s+', ' '); Code = $code }
}

function New-File {
    param([string]$Name, [string]$Content, [switch]$WithBom)
    $path = Join-Path $Work $Name
    New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force | Out-Null
    [IO.File]::WriteAllText($path, $Content, (New-Object Text.UTF8Encoding([bool]$WithBom)))
    return $path
}

function Office {
    param([string]$Password = $MadeUp, [string]$Server = 'CADSQL.test.local,1433', [string]$Extra = '')
    return "{ `"server`": `"$Server`", `"database`": `"MH_Database`", `"login`": `"revit_reader`", `"password`": `"$Password`", `"encrypt`": true, " +
        "`"trustServerCertificate`": true, `"masterParameterFile`": `"\\\\server\\share\\MH_Parameter.txt`"$Extra }"
}

function Rules {
    param([string]$Path)
    return @((Get-Acl -LiteralPath $Path).GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier]))
}

try {
    New-Item -ItemType Directory -Path $Work | Out-Null

    # --- reading -----------------------------------------------------------------------------------

    $read = Read-ConnectionFile (New-File 'read\office.json' (Office))
    Check 'an office file is read' ($null -eq $read.Problem -and $read.Server -eq 'CADSQL.test.local,1433' -and $read.Database -eq 'MH_Database' -and $read.Login -eq 'revit_reader')
    Check 'its flags and path are read' ($read.Encrypt -and $read.TrustServerCertificate -and -not $read.WindowsSignIn -and $read.MasterParameterFile -eq '\\server\share\MH_Parameter.txt')
    Check 'it only says that there is a password' ($read.HasPassword -and -not (($read | ConvertTo-Json) -match [regex]::Escape($MadeUp)))
    Check 'a byte order mark does not matter' ($null -eq (Read-ConnectionFile (New-File 'read\bom.json' (Office) -WithBom)).Problem)
    Check 'the case of the keys does not matter' ($null -eq (Read-ConnectionFile (New-File 'read\case.json' (Office).Replace('"server"', '"Server"').Replace('"password"', '"PASSWORD"'))).Problem)
    Check 'without flags it is encrypted and checked' ((Read-ConnectionFile (New-File 'read\noflags.json' '{ "server": "s", "database": "d", "login": "l", "password": "x" }')) | ForEach-Object { $_.Encrypt -and -not $_.TrustServerCertificate })
    $dev = Read-ConnectionFile (New-File 'read\dev.json' '{ "server": "(localdb)\\MSSQLLocalDB", "database": "MH_Database", "windowsSignIn": true }')
    Check 'a developer file is read' ($null -eq $dev.Problem -and $dev.WindowsSignIn)

    $problems = @(
        @{ Name = 'missing'; Path = (Join-Path $Work 'read\none.json'); Says = 'no connection file' },
        @{ Name = 'empty'; Path = (New-File 'read\empty.json' ''); Says = 'valid JSON' },
        @{ Name = 'not JSON'; Path = (New-File 'read\broken.json' "{ `"password`": `"$MadeUp`" `"server`" }"); Says = 'valid JSON' },
        @{ Name = 'cut off'; Path = (New-File 'read\cut.json' (Office).TrimEnd('}')); Says = 'valid JSON' },
        @{ Name = 'two blocks'; Path = (New-File 'read\two.json' ((Office) + (Office))); Says = 'valid JSON' },
        @{ Name = 'something after it'; Path = (New-File 'read\after.json' ((Office) + ' x')); Says = 'valid JSON' },
        @{ Name = 'a list'; Path = (New-File 'read\list.json' '[ 1 ]'); Says = 'single' },
        @{ Name = 'no server'; Path = (New-File 'read\noserver.json' '{ "database": "d", "login": "l", "password": "x" }'); Says = 'server' },
        @{ Name = 'no database'; Path = (New-File 'read\nodb.json' '{ "server": "s", "login": "l", "password": "x" }'); Says = 'database' },
        @{ Name = 'no login'; Path = (New-File 'read\nologin.json' '{ "server": "s", "database": "d", "password": "x" }'); Says = 'login' },
        @{ Name = 'no password'; Path = (New-File 'read\nopw.json' '{ "server": "s", "database": "d", "login": "l" }'); Says = 'password' },
        @{ Name = 'a number as password'; Path = (New-File 'read\number.json' '{ "server": "s", "database": "d", "login": "l", "password": 5 }'); Says = 'text in quotes' },
        @{ Name = 'text as a flag'; Path = (New-File 'read\flag.json' (Office -Extra ', "windowsSignIn": "no"')); Says = 'true or false' },
        @{ Name = 'a key twice'; Path = (New-File 'read\twice.json' (Office -Extra ', "Server": "other"')); Says = 'more than once' },
        @{ Name = 'too big'; Path = (New-File 'read\big.json' (Office -Extra (', "x": "' + ('a' * 70000) + '"'))); Says = 'too big' }
    )
    foreach ($case in $problems) {
        $problem = (Read-ConnectionFile $case.Path).Problem
        Check "reading: $($case.Name)" ($problem -match $case.Says -and $problem -notmatch [regex]::Escape($MadeUp))
    }
    Check 'braces and quotes in a password do not matter' ($null -eq (Read-ConnectionFile (New-File 'read\odd.json' (Office -Password 'a}b{c\"d\\e]'))).Problem)
    Check 'templates are recognized' (@((Read-ConnectionFile (New-File 'read\template.json' (Office -Password '<written by the installer>'))).Placeholders) -contains 'password')
    Check 'underscored templates too' (@((Read-ConnectionFile (New-File 'read\template2.json' (Office -Server '__SERVER__'))).Placeholders) -contains 'server')

    # --- installing --------------------------------------------------------------------------------

    $source = New-File 'source\database.json' (Office)
    $folder = Join-Path $Work 'pc\MH\RevitTools'
    $target = Join-Path $folder 'database.json'
    New-Item -ItemType Directory -Path (Join-Path $Work 'pc') | Out-Null

    $run = Invoke-Script 'Install-ConnectionFile.ps1' @('-SourceFile', $source, '-TrialFolder', $folder, '-WhatIfOnly')
    Check 'a preview writes nothing' ($run.Code -eq 0 -and $run.Output -match 'preview' -and -not (Test-Path -LiteralPath (Join-Path $Work 'pc\MH')))

    $run = Invoke-Script 'Install-ConnectionFile.ps1' @('-SourceFile', $source, '-TrialFolder', $folder)
    Check 'it installs' ($run.Code -eq 0 -and $run.Output -match 'Installed and locked' -and (Test-Path -LiteralPath $target))
    Check 'byte for byte' ((Get-FileHash -LiteralPath $target).Hash -eq (Get-FileHash -LiteralPath $source).Hash)
    Check 'nothing else is left behind' (@(Get-ChildItem -LiteralPath $folder -Force).Count -eq 1)
    $me = Get-CurrentSid
    foreach ($path in (Split-Path -Parent $folder), $folder, $target) {
        $rules = Rules $path
        Check "only the expected accounts on $path" (-not @($rules | Where-Object { $_.IdentityReference.Value -notin $SidSystem, $SidAdministrators, $SidUsers, $me }).Count)
        Check "others only read $path" (-not @(Get-OpenAccess $path -AlsoTrusted $me -IgnoreOwner).Count)
        Check "users may read $path" ([bool]@($rules | Where-Object { $_.IdentityReference.Value -eq $SidUsers -and ([int64]$_.FileSystemRights -band 1) }).Count)
    }
    Check 'the folders inherit nothing' ((Get-Acl -LiteralPath $folder).AreAccessRulesProtected -and (Get-Acl -LiteralPath (Split-Path -Parent $folder)).AreAccessRulesProtected)

    $run = Invoke-Script 'Install-ConnectionFile.ps1' @('-SourceFile', $source, '-TrialFolder', $folder)
    Check 'again changes nothing' ($run.Code -eq 0 -and (Get-FileHash -LiteralPath $target).Hash -eq (Get-FileHash -LiteralPath $source).Hash)

    $newPassword = New-File 'source\new.json' (Office -Password "$MadeUp-new")
    $run = Invoke-Script 'Install-ConnectionFile.ps1' @('-SourceFile', $newPassword, '-TrialFolder', $folder)
    Check 'a new password for the same database replaces it' ($run.Code -eq 0 -and (Get-FileHash -LiteralPath $target).Hash -eq (Get-FileHash -LiteralPath $newPassword).Hash)

    $before = (Get-FileHash -LiteralPath $target).Hash
    $other = New-File 'source\other.json' (Office -Server 'OTHER.test.local')
    $run = Invoke-Script 'Install-ConnectionFile.ps1' @('-SourceFile', $other, '-TrialFolder', $folder)
    Check 'another database stops it' ($run.Code -eq 1 -and $run.Output -match 'another database' -and (Get-FileHash -LiteralPath $target).Hash -eq $before)
    $run = Invoke-Script 'Install-ConnectionFile.ps1' @('-SourceFile', $other, '-TrialFolder', $folder, '-Force')
    Check 'Force replaces it' ($run.Code -eq 0 -and (Get-FileHash -LiteralPath $target).Hash -eq (Get-FileHash -LiteralPath $other).Hash)

    [IO.File]::WriteAllText($target, 'not a connection file')
    $run = Invoke-Script 'Install-ConnectionFile.ps1' @('-SourceFile', $source, '-TrialFolder', $folder)
    Check 'a broken file is replaced' ($run.Code -eq 0 -and (Get-FileHash -LiteralPath $target).Hash -eq (Get-FileHash -LiteralPath $source).Hash)

    Check 'a locked folder that is replaced is not kept' (@(Get-ChildItem -LiteralPath (Join-Path $Work 'pc') -Force).Count -eq 1)

    # Someone was there first: the folder and the file are theirs, and everyone may write.
    $taken = Join-Path $Work 'taken\MH\RevitTools'
    New-Item -ItemType Directory -Path $taken -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $taken 'database.json'), (Office))
    $everyone = New-Object Security.Principal.SecurityIdentifier('S-1-1-0')
    foreach ($path in (Split-Path -Parent $taken), $taken, (Join-Path $taken 'database.json')) {
        $acl = Get-Acl -LiteralPath $path
        $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($everyone, 'FullControl', 'Allow')))
        Set-Acl -LiteralPath $path -AclObject $acl
    }
    Check 'the tests can tell an open folder' (@(Get-OpenAccess $taken -AlsoTrusted $me -IgnoreOwner).Count -gt 0)
    $run = Invoke-Script 'Install-ConnectionFile.ps1' @('-SourceFile', $source, '-TrialFolder', $taken, '-WhatIfOnly')
    Check 'a preview says that an open folder moves aside, and leaves it' ($run.Code -eq 0 -and $run.Output -match 'locked yet' -and @(Get-ChildItem -LiteralPath (Join-Path $Work 'taken')).Count -eq 1)
    $run = Invoke-Script 'Install-ConnectionFile.ps1' @('-SourceFile', $source, '-TrialFolder', $taken)
    Check 'a new folder is locked in place of an open one' ($run.Code -eq 0 -and -not @(foreach ($path in (Split-Path -Parent $taken), $taken, (Join-Path $taken 'database.json')) { Get-OpenAccess $path -AlsoTrusted $me -IgnoreOwner }).Count)
    Check 'the open one is kept aside' (@(Get-ChildItem -LiteralPath (Join-Path $Work 'taken') -Directory | Where-Object { $_.Name -match '^MH\.before-\d{8}-\d{6}(-\d+)?$' }).Count -eq 1)
    $taken1 = Join-Path $Work 'name\MH.before'
    New-Item -ItemType Directory -Path $taken1, "$taken1-2" -Force | Out-Null
    Check 'a name in use gets a number' ((Get-FreeName $taken1) -eq "$taken1-3" -and (Get-FreeName (Join-Path $Work 'name\free')) -eq (Join-Path $Work 'name\free'))
    $run = Invoke-Script 'Install-ConnectionFile.ps1' @('-SourceFile', $newPassword, '-TrialFolder', $taken)
    Check 'a locked one is replaced, and not kept' ($run.Code -eq 0 -and @(Get-ChildItem -LiteralPath (Join-Path $Work 'taken') -Directory).Count -eq 2)

    # A locked folder that someone holds open can't be renamed: nothing is lost then.
    $held = New-Object IO.FileStream((Join-Path $taken 'database.json'), [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    try { $run = Invoke-Script 'Install-ConnectionFile.ps1' @('-SourceFile', $source, '-TrialFolder', $taken) } finally { $held.Dispose() }
    Check 'a file in use stops it' ($run.Code -eq 1 -and $run.Output -match 'rename')
    Check 'and leaves the one that was there' ((Get-FileHash -LiteralPath (Join-Path $taken 'database.json')).Hash -eq (Get-FileHash -LiteralPath $newPassword).Hash)
    Check 'and nothing beside it' (@(Get-ChildItem -LiteralPath (Join-Path $Work 'taken') -Directory).Count -eq 2)

    $refused = @(
        @{ Name = 'a template'; Source = (New-File 'source\template.json' (Office -Password '<password>')); Says = 'placeholder' },
        @{ Name = 'a developer file'; Source = (New-File 'source\dev.json' '{ "server": "(localdb)\\MSSQLLocalDB", "database": "MH_Database", "windowsSignIn": true }'); Says = 'developer' },
        @{ Name = 'a broken file'; Source = (New-File 'source\broken.json' "{ `"server`": `"s`", `"password`": `"$MadeUp`" "); Says = 'valid JSON' },
        @{ Name = 'a missing file'; Source = (Join-Path $Work 'source\none.json'); Says = 'no connection file' }
    )
    foreach ($case in $refused) {
        $clean = Join-Path $Work ('refused\' + [Guid]::NewGuid().ToString('N') + '\MH\RevitTools')
        New-Item -ItemType Directory -Path (Split-Path -Parent (Split-Path -Parent $clean)) -Force | Out-Null
        $run = Invoke-Script 'Install-ConnectionFile.ps1' @('-SourceFile', $case.Source, '-TrialFolder', $clean)
        Check "refused: $($case.Name)" ($run.Code -eq 1 -and $run.Output -match $case.Says -and -not (Test-Path -LiteralPath (Split-Path -Parent $clean)))
    }

    $shared = Join-Path $Work 'shared\MH\RevitTools'
    New-Item -ItemType Directory -Path (Join-Path $Work 'shared\MH\SomethingElse') -Force | Out-Null
    $run = Invoke-Script 'Install-ConnectionFile.ps1' @('-SourceFile', $source, '-TrialFolder', $shared)
    Check 'a folder that holds something else is left alone' ($run.Code -eq 1 -and $run.Output -match 'SomethingElse' -and -not (Test-Path -LiteralPath $shared))

    foreach ($case in @{ Name = 'the real place'; Folder = (Join-Path ([Environment]::GetFolderPath('CommonApplicationData')) 'MH\RevitTools') }, @{ Name = 'the drive root'; Folder = 'C:\RevitTools' }) {
        $run = Invoke-Script 'Install-ConnectionFile.ps1' @('-SourceFile', $source, '-TrialFolder', $case.Folder, '-WhatIfOnly')
        Check "no trial in $($case.Name)" ($run.Code -eq 1 -and $run.Output -match 'place to try it')
    }

    foreach ($link in 'MH', 'MH\RevitTools') {
        $root = Join-Path $Work ('linked' + $link.Length)
        $elsewhere = Join-Path $root 'elsewhere'
        New-Item -ItemType Directory -Path $elsewhere -Force | Out-Null
        $linkPath = Join-Path $root $link
        New-Item -ItemType Directory -Path (Split-Path -Parent $linkPath) -Force | Out-Null
        New-Item -ItemType Junction -Path $linkPath -Value $elsewhere | Out-Null
        $run = Invoke-Script 'Install-ConnectionFile.ps1' @('-SourceFile', $source, '-TrialFolder', (Join-Path $root 'MH\RevitTools'))
        Check "a link at $link stops it" ($run.Code -eq 1 -and $run.Output -match 'link' -and -not @(Get-ChildItem -LiteralPath $elsewhere -Recurse -Force | Where-Object { $_.Name -eq 'database.json' }).Count)
        [IO.Directory]::Delete($linkPath)
    }

    # Only without administrator rights: with them, this would really install.
    if (-not (Test-Administrator)) {
        $run = Invoke-Script 'Install-ConnectionFile.ps1' @('-SourceFile', $source)
        Check 'without administrator rights nothing happens' ($run.Code -eq 1 -and $run.Output -match 'administrator')
    }

    # --- the hand-over folder ----------------------------------------------------------------------

    $package = Join-Path $Work 'handover\ConnectionFile'
    $run = Invoke-Script 'New-ConnectionFilePackage.ps1' @('-SourceFile', $source, '-OutputRoot', $package, '-WhatIfOnly')
    Check 'a package preview writes nothing' ($run.Code -eq 0 -and $run.Output -match 'preview' -and -not (Test-Path -LiteralPath $package))

    $run = Invoke-Script 'New-ConnectionFilePackage.ps1' @('-SourceFile', $source, '-OutputRoot', $package)
    Check 'the package is made' ($run.Code -eq 0 -and $run.Output -match 'Ready')
    $expected = '.mh-connection-file.json', '_ConnectionFile.ps1', 'database.json', 'Install connection file.cmd', 'INSTALLATION.txt', 'Install-ConnectionFile.ps1'
    Check 'with exactly its files' (-not (Compare-Object $expected @(Get-ChildItem -LiteralPath $package -Force | ForEach-Object { $_.Name })))
    foreach ($path in $package, (Join-Path $package 'database.json')) {
        Check "nobody else may open $path" (-not @(Rules $path | Where-Object { $_.IdentityReference.Value -notin $SidSystem, $SidAdministrators, $me }).Count)
    }
    $run = Invoke-Script 'New-ConnectionFilePackage.ps1' @('-SourceFile', $newPassword, '-OutputRoot', $package)
    Check 'made again, with the new file' ($run.Code -eq 0 -and (Get-FileHash -LiteralPath (Join-Path $package 'database.json')).Hash -eq (Get-FileHash -LiteralPath $newPassword).Hash)
    New-Item -ItemType Directory -Path (Join-Path $package 'someones folder') | Out-Null
    $run = Invoke-Script 'New-ConnectionFilePackage.ps1' @('-SourceFile', $source, '-OutputRoot', $package)
    Check 'not over a folder someone put in' ($run.Code -eq 1 -and $run.Output -match 'never puts there' -and (Test-Path -LiteralPath (Join-Path $package 'database.json')))
    Remove-Item -LiteralPath (Join-Path $package 'someones folder')
    $marker = Join-Path $package '.mh-connection-file.json'
    $markerText = [IO.File]::ReadAllText($marker)
    [IO.File]::WriteAllText($marker, '{ "product": "something else" }')
    $run = Invoke-Script 'New-ConnectionFilePackage.ps1' @('-SourceFile', $source, '-OutputRoot', $package)
    Check 'not over another product''s folder' ($run.Code -eq 1 -and $run.Output -match 'made by this script')
    [IO.File]::WriteAllText($marker, $markerText)

    # From the package as it's handed over: no -SourceFile.
    $fromPackage = Join-Path $Work 'secondpc\MH\RevitTools'
    New-Item -ItemType Directory -Path (Join-Path $Work 'secondpc') | Out-Null
    $run = Invoke-Script 'Install-ConnectionFile.ps1' @('-TrialFolder', $fromPackage) -Folder $package
    Check 'the package installs' ($run.Code -eq 0 -and (Get-FileHash -LiteralPath (Join-Path $fromPackage 'database.json')).Hash -eq (Get-FileHash -LiteralPath $newPassword).Hash)

    # As it's really started: the .cmd, with the "Press any key" getting none. From PowerShell 7 its modules come along.
    $viaCmd = Join-Path $Work 'third pc\MH\RevitTools'
    New-Item -ItemType Directory -Path (Join-Path $Work 'third pc') | Out-Null
    $lines = '' | & (Join-Path $package 'Install connection file.cmd') -TrialFolder $viaCmd 2>&1 | Out-String
    $code = $LASTEXITCODE
    [void]$script:AllOutput.AppendLine($lines)
    Check 'the .cmd installs' ($code -eq 0 -and $lines -match 'Installed and locked' -and (Test-Path -LiteralPath (Join-Path $viaCmd 'database.json')))
    $lines = '' | & (Join-Path $package 'Install connection file.cmd') -SourceFile (Join-Path $Work 'none.json') -TrialFolder $viaCmd 2>&1 | Out-String
    $code = $LASTEXITCODE
    [void]$script:AllOutput.AppendLine($lines)
    Check 'the .cmd passes a failure on' ($code -eq 1 -and $lines -match 'no connection file')

    $foreign = Join-Path $Work 'foreign\folder'
    New-File 'foreign\folder\mine.txt' 'keep' | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $Work 'repo\.git') -Force | Out-Null
    $places = @(
        @{ Name = 'a folder with something else in it'; Root = $foreign; Says = "wasn't made by this script" },
        @{ Name = 'a repository'; Root = (Join-Path $Work 'repo\out\ConnectionFile'); Says = 'repository' },
        @{ Name = 'near the drive root'; Root = 'C:\ConnectionFile'; Says = 'drive root' },
        @{ Name = 'a network folder'; Root = '\\server.invalid\share\folder\ConnectionFile'; Says = 'local folder' },
        @{ Name = 'the office share'; Root = (Join-Path $Work ('Mu' + [char]0x0308 + 'ller+Hereth GmbH\a')); Says = 'office share' },
        @{ Name = 'a short name'; Root = 'C:\NOSUCH~1\a\ConnectionFile'; Says = 'short name' },
        @{ Name = 'a synchronized folder'; Root = (Join-Path $Work 'OneDrive - Office\a'); Says = 'synchronized' }
    )
    foreach ($case in $places) {
        $run = Invoke-Script 'New-ConnectionFilePackage.ps1' @('-SourceFile', $source, '-OutputRoot', $case.Root)
        $made = -not $case.Root.StartsWith('\\') -and (Test-Path -LiteralPath (Join-Path $case.Root 'database.json'))
        Check "no package in $($case.Name)" ($run.Code -eq 1 -and $run.Output -match $case.Says -and -not $made)
    }
    Check 'the other folder is untouched' ([IO.File]::ReadAllText((Join-Path $foreign 'mine.txt')) -eq 'keep')

    $oneDrive = $env:OneDrive
    $env:OneDrive = Join-Path $Work 'Cloud'
    $run = Invoke-Script 'New-ConnectionFilePackage.ps1' @('-SourceFile', $source, '-OutputRoot', (Join-Path $Work 'Cloud\a\ConnectionFile'))
    $env:OneDrive = $oneDrive
    Check 'no package in OneDrive, whatever its folder is called' ($run.Code -eq 1 -and $run.Output -match 'OneDrive')

    $run = Invoke-Script 'New-ConnectionFilePackage.ps1' @('-SourceFile', (New-File 'source\template3.json' (Office -Password '__PASSWORD__')), '-OutputRoot', (Join-Path $Work 'handover2\ConnectionFile'))
    Check 'no package from a template' ($run.Code -eq 1 -and $run.Output -match 'placeholder' -and -not (Test-Path -LiteralPath (Join-Path $Work 'handover2')))

    # --- the password ------------------------------------------------------------------------------

    Check 'no script ever said the password' (-not $script:AllOutput.ToString().Contains($MadeUp))
    Check 'no script ever showed a PowerShell error' (-not ($script:AllOutput.ToString() -match 'At line:|At .*\.ps1:\d|CategoryInfo|In .*\.ps1:\d'))
}
finally {
    if (Test-Path -LiteralPath $Work) { Remove-Item -LiteralPath $Work -Recurse -Force -ErrorAction SilentlyContinue }
}

Write-Host ''
Write-Host "  PowerShell $($PSVersionTable.PSVersion): $script:Passed passed, $script:Failed failed"
if ($script:Failed) { exit 1 }
exit 0
