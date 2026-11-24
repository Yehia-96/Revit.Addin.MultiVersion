<#
.SYNOPSIS
    Shared helpers for the connection file's scripts. Dot-sourced, never run directly.

.DESCRIPTION
    Reads and checks a connection file, locks folders, and installs the file. Nothing here ever shows the
    password: not in a result, a message or an error.

    Windows PowerShell 5.1 compatible, and it needs nothing from the add-in's other scripts: it's handed over
    on its own.
#>

$ConnectionFileName = 'database.json'
$MaxFileBytes = 64KB

# Well-known accounts by SID, so Windows' language doesn't matter (Administratoren, Benutzer).
$SidSystem = 'S-1-5-18'
$SidAdministrators = 'S-1-5-32-544'
$SidUsers = 'S-1-5-32-545'
$SidTrustedInstaller = 'S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464'

# --- output -------------------------------------------------------------------------------------

function Write-Step { param([string]$Text) Write-Host "  $Text" }
function Write-Info { param([string]$Text) Write-Host "  $Text" -ForegroundColor DarkGray }
function Write-Ok   { param([string]$Text) Write-Host "  $Text" -ForegroundColor Green }
function Write-Warn { param([string]$Text) Write-Host "  $Text" -ForegroundColor Yellow }
function Write-Bad  { param([string]$Text) Write-Host "  $Text" -ForegroundColor Red }

function Write-Banner {
    param([string]$Title, [string]$Subtitle)
    Write-Host ''
    Write-Host "  $Title" -ForegroundColor Cyan
    if ($Subtitle) { Write-Host "  $Subtitle" -ForegroundColor DarkGray }
    Write-Host ('  ' + ('-' * 68)) -ForegroundColor DarkGray
}

# --- the connection file ------------------------------------------------------------------------

<#
True when the text is one { } block and nothing else. Windows' own JSON reader doesn't mind a file that was cut off,
or a second block after the first; the add-in does.
#>
function Test-SingleBlock {
    param([string]$Text)

    $depth = 0; $inText = $false; $escaped = $false; $closed = $false
    foreach ($c in $Text.Trim().ToCharArray()) {
        if ($closed) { return $false }
        if ($inText) {
            if ($escaped) { $escaped = $false }
            elseif ($c -eq '\') { $escaped = $true }
            elseif ($c -eq '"') { $inText = $false }
            continue
        }
        if ($c -eq '"') { $inText = $true }
        elseif ($c -eq '{' -or $c -eq '[') { $depth++ }
        elseif ($c -eq '}' -or $c -eq ']') { $depth--; if ($depth -eq 0) { $closed = $true } }
        elseif ($depth -eq 0) { return $false }
    }
    return $closed
}

# The file's bytes, read once: what's checked is what's written. Throws when there's nothing to read.
function Read-ConnectionBytes {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "There is no connection file at $Path." }
    if ((Get-Item -LiteralPath $Path -Force).Length -gt $MaxFileBytes) { throw "$Path is too big to be a connection file." }
    try { $bytes = [IO.File]::ReadAllBytes($Path) } catch { throw "$Path can't be read." }
    return , $bytes
}

function Get-BytesHash {
    param([byte[]]$Bytes)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($sha.ComputeHash($Bytes)).Replace('-', '') } finally { $sha.Dispose() }
}

<#
Reads a connection file by the add-in's own rules (MH.RevitTools.Database, ConnectionFile). Returns the
settings without the password, and what's wrong in Problem. The JSON reader's errors can quote the text
around a mistake, and that may be the password, so they are never passed on.
#>
function Read-ConnectionFile {
    param([string]$Path, [byte[]]$Bytes)

    $result = [pscustomobject]@{
        Problem = $null; Server = ''; Database = ''; Login = ''; HasPassword = $false; WindowsSignIn = $false
        Encrypt = $true; TrustServerCertificate = $false; MasterParameterFile = ''; Placeholders = @()
    }

    if ($null -eq $Bytes) {
        try { $Bytes = Read-ConnectionBytes $Path } catch { $result.Problem = $_.Exception.Message; return $result }
    }

    $root = $null
    try {
        Add-Type -AssemblyName System.Runtime.Serialization -ErrorAction SilentlyContinue
        # A .NET reader rather than ConvertFrom-Json: what a cmdlet is given can end up in PowerShell's logs.
        $json = $Bytes
        if ($json.Length -gt 3 -and $json[0] -eq 0xEF -and $json[1] -eq 0xBB -and $json[2] -eq 0xBF) { $json = [byte[]]($json[3..($json.Length - 1)]) }
        if (Test-SingleBlock ([Text.Encoding]::UTF8.GetString($json))) {
            $quotas = New-Object Xml.XmlDictionaryReaderQuotas
            $quotas.MaxDepth = 8
            $quotas.MaxStringContentLength = $MaxFileBytes
            $reader = [Runtime.Serialization.Json.JsonReaderWriterFactory]::CreateJsonReader($json, $quotas)
            try {
                $xml = New-Object Xml.XmlDocument
                $xml.Load($reader)
                $root = $xml.DocumentElement
            }
            finally { $reader.Dispose() }
        }
    }
    catch { $root = $null }
    if ($null -eq $root) { $result.Problem = "$Path isn't valid JSON, or can't be read."; return $result }

    if ($root.GetAttribute('type') -ne 'object') { $result.Problem = "$Path should hold a single { } block with the settings."; return $result }

    $values = @{}
    foreach ($node in $root.ChildNodes) {
        # A key that isn't a plain name becomes an "item" element with the name in an attribute.
        $name = if ($node.HasAttribute('item')) { $node.GetAttribute('item') } else { $node.LocalName }
        $key = $name.ToLowerInvariant()
        if ($values.ContainsKey($key)) { $result.Problem = "$Path has ""$name"" more than once."; return $result }
        $values[$key] = $node
    }

    foreach ($key in 'server', 'database', 'login', 'password', 'masterParameterFile') {
        $node = $values[$key.ToLowerInvariant()]
        if ($null -ne $node -and $node.GetAttribute('type') -notin 'string', 'null') {
            $result.Problem = "$Path has ""$key"" as something other than text in quotes."; return $result
        }
    }
    foreach ($key in 'windowsSignIn', 'encrypt', 'trustServerCertificate') {
        $node = $values[$key.ToLowerInvariant()]
        if ($null -ne $node -and $node.GetAttribute('type') -notin 'boolean', 'null') {
            $result.Problem = "$Path has ""$key"" set to something other than true or false."; return $result
        }
    }

    $textOf = {
        param($key)
        $node = $values[$key]
        if ($null -eq $node -or $node.GetAttribute('type') -eq 'null') { return '' }
        return $node.InnerText
    }
    $flagOf = {
        param($key, $default)
        $node = $values[$key]
        if ($null -eq $node -or $node.GetAttribute('type') -eq 'null') { return $default }
        return ($node.InnerText -eq 'true')
    }

    $result.Server = (& $textOf 'server').Trim()
    $result.Database = (& $textOf 'database').Trim()
    $result.Login = (& $textOf 'login').Trim()
    $result.MasterParameterFile = (& $textOf 'masterparameterfile').Trim()
    $result.HasPassword = (& $textOf 'password').Length -gt 0
    $result.WindowsSignIn = & $flagOf 'windowssignin' $false
    $result.Encrypt = & $flagOf 'encrypt' $true
    $result.TrustServerCertificate = & $flagOf 'trustservercertificate' $false

    # <read-only login>, __PASSWORD__: a template that was never filled in.
    $result.Placeholders = @('server', 'database', 'login', 'password' | Where-Object { (& $textOf $_).Trim() -match '^(<.*>|__.*__)$' })

    if (-not $result.Server) { $result.Problem = "$Path has no ""server""." }
    elseif (-not $result.Database) { $result.Problem = "$Path has no ""database""." }
    elseif (-not $result.WindowsSignIn -and -not $result.Login) { $result.Problem = "$Path has no ""login""." }
    elseif (-not $result.WindowsSignIn -and -not $result.HasPassword) { $result.Problem = "$Path has no ""password"" for the login." }
    return $result
}

# The settings of a file that may be handed out; throws with the reason when it may not.
function Get-OfficeConnection {
    param([string]$Path, [byte[]]$Bytes, [switch]$AllowDeveloper)

    $settings = Read-ConnectionFile $Path $Bytes
    if ($settings.Problem) { throw $settings.Problem }
    if ($settings.Placeholders.Count) {
        throw "$Path still has a placeholder for: $($settings.Placeholders -join ', '). It's the template, not the office's file."
    }
    if ($settings.WindowsSignIn -and -not $AllowDeveloper) {
        throw "$Path is a developer's connection file (Windows sign-in), not one for the office."
    }
    return $settings
}

function Get-ConnectionLabel {
    param($Settings)
    return "$($Settings.Database) on $($Settings.Server)"
}

function Show-Connection {
    param($Settings)
    Write-Step "$(Get-ConnectionLabel $Settings), as $(if ($Settings.WindowsSignIn) { 'the Windows user' } else { $Settings.Login })"
    if (-not $Settings.Encrypt) { Write-Warn 'The file switches encryption off. Signing in is encrypted anyway; ask whether the rest should be.' }
    if (-not $Settings.MasterParameterFile) { Write-Warn 'The file names no masterParameterFile, so Check parameters will not be able to add parameters.' }
}

function Test-SameDatabase {
    param($First, $Second)
    return ($First.Server -ieq $Second.Server) -and ($First.Database -ieq $Second.Database)
}

# --- folders ------------------------------------------------------------------------------------

function Test-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    return (New-Object Security.Principal.WindowsPrincipal($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Get-CurrentSid { return [Security.Principal.WindowsIdentity]::GetCurrent().User.Value }

# A link in the path would put the file somewhere else. What can't be looked at counts as one.
function Assert-NotLinked {
    param([string[]]$Path)
    foreach ($p in $Path) {
        if (-not $p) { continue }
        try { $attributes = [IO.File]::GetAttributes($p) }
        catch {
            $reason = $_.Exception.GetBaseException()
            if ($reason -is [IO.FileNotFoundException] -or $reason -is [IO.DirectoryNotFoundException]) { continue }
            throw "$p can't be looked at: $($reason.Message)"
        }
        if ($attributes -band [IO.FileAttributes]::ReparsePoint) {
            throw "$p is a link to somewhere else. Nothing was written; tell whoever looks after this PC."
        }
    }
}

# Never over something that's there already.
function Write-NewFile {
    param([string]$Path, [byte[]]$Bytes)
    $stream = New-Object IO.FileStream($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try { $stream.Write($Bytes, 0, $Bytes.Length) } finally { $stream.Dispose() }
}

# The path, with a number after it when there's something by that name already.
function Get-FreeName {
    param([string]$Path)
    $free = $Path
    for ($number = 2; (Test-Path -LiteralPath $free); $number++) { $free = "$Path-$number" }
    return $free
}

# Waits a moment for Revit, which opens the file now and then to read it.
function Move-Folder {
    param([string]$From, [string]$To)
    for ($try = 1; ; $try++) {
        try { [IO.Directory]::Move($From, $To); return }
        catch {
            if ($try -ge 5) { throw "Windows didn't rename $From to $(Split-Path -Leaf $To): $($_.Exception.GetBaseException().Message)" }
            Start-Sleep -Milliseconds 300
        }
    }
}

# Deletes a folder these scripts made, and only what they put there: anything else keeps it.
function Remove-OwnFolder {
    param([string]$Root, [string]$Leaf)
    try {
        $inner = Join-Path $Root $Leaf
        Assert-NotLinked $Root, $inner
        $file = Join-Path $inner $ConnectionFileName
        if ([IO.File]::Exists($file)) { [IO.File]::Delete($file) }
        foreach ($folder in $inner, $Root) { if ([IO.Directory]::Exists($folder)) { [IO.Directory]::Delete($folder, $false) } }
    }
    catch { Write-Warn "$Root couldn't be removed: $($_.Exception.GetBaseException().Message)" }
}

function Set-AdministratorsAsOwner {
    param([string]$Path)
    $icacls = Join-Path ([Environment]::GetFolderPath('System')) 'icacls.exe'
    & $icacls $Path /setowner "*$SidAdministrators" /Q | Out-Null
    if ($LASTEXITCODE) { throw "Windows didn't give $Path to the administrators (icacls $LASTEXITCODE)." }
}

<#
Who may do what in a folder: administrators and SYSTEM have full control, the others are given as SIDs, and nothing
is inherited from above.
#>
function New-FolderSecurity {
    param([string[]]$FullControl = @(), [string[]]$ReadOnly = @(), [switch]$OwnedByAdministrators)

    $inherit = [Security.AccessControl.InheritanceFlags]'ContainerInherit, ObjectInherit'
    $none = [Security.AccessControl.PropagationFlags]::None
    $allow = [Security.AccessControl.AccessControlType]::Allow
    $security = New-Object Security.AccessControl.DirectorySecurity
    $security.SetAccessRuleProtection($true, $false)
    foreach ($sid in @($SidSystem, $SidAdministrators) + $FullControl) {
        $account = New-Object Security.Principal.SecurityIdentifier($sid)
        $security.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($account, 'FullControl', $inherit, $none, $allow)))
    }
    foreach ($sid in $ReadOnly) {
        $account = New-Object Security.Principal.SecurityIdentifier($sid)
        $security.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($account, 'ReadAndExecute', $inherit, $none, $allow)))
    }
    if ($OwnedByAdministrators) { $security.SetOwner((New-Object Security.Principal.SecurityIdentifier($SidAdministrators))) }
    return $security
}

# As Get-Acl, without its module: started from PowerShell 7, Windows PowerShell can't load it.
function Get-PathSecurity {
    param([string]$Path)
    $item = Get-Item -LiteralPath $Path -Force
    if ($item.PSObject.Methods['GetAccessControl']) { return $item.GetAccessControl() }
    return [IO.FileSystemAclExtensions]::GetAccessControl($item)
}

# Made with its permissions from the start, so there's no moment in which someone else could put something in.
function New-FolderWithSecurity {
    param([string]$Path, $Security)
    if ($PSVersionTable.PSEdition -eq 'Core') { [IO.FileSystemAclExtensions]::CreateDirectory($Security, $Path) | Out-Null }
    else { [IO.Directory]::CreateDirectory($Path, $Security) | Out-Null }
}


<#
Says who else may change a path, or owns it; empty when only administrators, SYSTEM and the trusted ones do. Read
back from Windows, as the commands above succeeding proves nothing.
#>
function Get-OpenAccess {
    param([string]$Path, [string[]]$AlsoTrusted = @(), [switch]$IgnoreOwner)

    $trusted = @($SidSystem, $SidAdministrators, $SidTrustedInstaller) + $AlsoTrusted
    $sid = [Security.Principal.SecurityIdentifier]
    $changing = [int64][Security.AccessControl.FileSystemRights]'Write, Delete, DeleteSubdirectoriesAndFiles, ChangePermissions, TakeOwnership'
    # The generic rights "all" and "write", which Windows keeps as they are on entries that are only inherited.
    $generic = [int64]0x10000000 -bor [int64]0x40000000

    $acl = Get-PathSecurity $Path
    $owner = $acl.GetOwner($sid).Value
    if (-not $IgnoreOwner -and $owner -notin $trusted) { "$Path belongs to $owner" }
    foreach ($rule in $acl.GetAccessRules($true, $true, $sid)) {
        if ($rule.AccessControlType -ne 'Allow' -or $rule.IdentityReference.Value -in $trusted) { continue }
        $rights = [int64]$rule.FileSystemRights
        if (($rights -band $changing) -or ($rights -band $generic)) { "$($rule.IdentityReference.Value) may change $Path" }
    }
}

# True for a folder as this script leaves it: nothing inherited, users read it, and only administrators change it.
function Test-LockedFolder {
    param([string]$Path, [string[]]$AlsoTrusted = @(), [switch]$IgnoreOwner)

    $acl = Get-PathSecurity $Path
    if (-not $acl.AreAccessRulesProtected) { return $false }
    if (@(Get-OpenAccess $Path -AlsoTrusted $AlsoTrusted -IgnoreOwner:$IgnoreOwner).Count) { return $false }
    $read = [int64][Security.AccessControl.FileSystemRights]'ReadAndExecute'
    return [bool]@($acl.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier]) |
        Where-Object { $_.IdentityReference.Value -eq $SidUsers -and $_.AccessControlType -eq 'Allow' -and ([int64]$_.FileSystemRights -band $read) -eq $read }).Count
}

# --- installing ---------------------------------------------------------------------------------

<#
Puts the connection file into <folder> (...\MH\RevitTools), with that folder and MH above it locked: whoever owns MH
could move RevitTools away. Administrators change, everyone reads.

Anyone may have made an MH before, so an existing one is never taken over. A new one is built beside it, locked from
the start, with the file in it; then it takes the other's place. An MH that was locked already is deleted then, as it's
ours. Any other is kept as MH.before-<time>.

-Trial is for trying it on another folder without administrator rights: ownership stays as it is, and the person trying
it may change the folder too.
#>
function Install-ConnectionFile {
    param([string]$Source, [string]$Folder, [switch]$Replace, [switch]$Preview, [switch]$Trial)

    $bytes = Read-ConnectionBytes $Source
    $settings = Get-OfficeConnection $Source $bytes -AllowDeveloper:$Replace
    $parent = Split-Path -Parent $Folder
    $leaf = Split-Path -Leaf $Folder
    $target = Join-Path $Folder $ConnectionFileName
    Assert-NotLinked $parent, $Folder, $target

    Write-Info "from: $Source"
    Write-Info "to  : $target"
    Show-Connection $settings

    if (Test-Path -LiteralPath $target) {
        $installed = Read-ConnectionFile $target
        if ($installed.Problem) { Write-Warn 'The file that is there now cannot be used; it is replaced.' }
        elseif (-not (Test-SameDatabase $installed $settings) -and -not $Replace) {
            throw "This PC's connection file is for another database: $(Get-ConnectionLabel $installed). Nothing was changed. Use -Force to replace it."
        }
    }

    $trialist = @(if ($Trial) { Get-CurrentSid })
    $aside = $null
    $wasLocked = $false
    if (Test-Path -LiteralPath $parent) {
        $others = @(Get-ChildItem -LiteralPath $parent -Force | Where-Object { $_.Name -ne $leaf })
        if ($others.Count) {
            throw "$parent also holds $(($others | ForEach-Object { $_.Name }) -join ', '). Nothing was changed; tell whoever looks after the add-in."
        }
        $aside = Get-FreeName ($parent + '.before-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
        $wasLocked = Test-LockedFolder $parent -AlsoTrusted $trialist -IgnoreOwner:$Trial
        if (-not $wasLocked) { Write-Warn "$parent isn't locked yet. It becomes $(Split-Path -Leaf $aside), and a new one takes its place." }
    }

    if ($Preview) {
        Write-Info "would make $Folder with $ConnectionFileName, locked: administrators change, everyone reads"
        Write-Info 'preview, nothing written'
        return
    }

    $stage = $parent + '.new-' + [Guid]::NewGuid().ToString('N')
    try {
        $security = New-FolderSecurity -FullControl $trialist -ReadOnly $SidUsers -OwnedByAdministrators:(-not $Trial)
        foreach ($path in $stage, (Join-Path $stage $leaf)) { New-FolderWithSecurity $path $security }
        $staged = Join-Path $stage (Join-Path $leaf $ConnectionFileName)
        Write-NewFile $staged $bytes
        if (-not $Trial) { Set-AdministratorsAsOwner $staged }
        Assert-Locked $stage $leaf $bytes -AlsoTrusted $trialist -IgnoreOwner:$Trial

        if ($aside) { Move-Folder $parent $aside }
        try { Move-Folder $stage $parent }
        catch {
            if ($aside -and -not (Test-Path -LiteralPath $parent)) { Move-Folder $aside $parent }
            throw
        }
    }
    finally {
        if (Test-Path -LiteralPath $stage) { Remove-OwnFolder $stage $leaf }
    }

    Assert-Locked $parent $leaf $bytes -AlsoTrusted $trialist -IgnoreOwner:$Trial
    if ($aside -and $wasLocked) { Remove-OwnFolder $aside $leaf }
    Write-Ok 'Installed and locked: administrators can change it, everyone on this PC can read it.'
}

# Reads back from Windows what's there: the same file, and nobody else who may change it, RevitTools or MH.
function Assert-Locked {
    param([string]$Root, [string]$Leaf, [byte[]]$Bytes, [string[]]$AlsoTrusted = @(), [switch]$IgnoreOwner)

    $inner = Join-Path $Root $Leaf
    $file = Join-Path $inner $ConnectionFileName
    Assert-NotLinked $Root, $inner, $file
    foreach ($folder in $Root, $inner) {
        if (-not (Test-LockedFolder $folder -AlsoTrusted $AlsoTrusted -IgnoreOwner:$IgnoreOwner)) {
            throw "$folder isn't locked. $(@(Get-OpenAccess $folder -AlsoTrusted $AlsoTrusted -IgnoreOwner:$IgnoreOwner) -join '; ')"
        }
    }
    $open = @(Get-OpenAccess $file -AlsoTrusted $AlsoTrusted -IgnoreOwner:$IgnoreOwner)
    if ($open.Count) { throw "$file isn't locked. $($open -join '; ')" }
    if ((Get-BytesHash (Read-ConnectionBytes $file)) -ne (Get-BytesHash $Bytes)) { throw "$file isn't the file it should be." }
}