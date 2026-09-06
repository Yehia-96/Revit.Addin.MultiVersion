# Path to version.txt and AssemblyInfo.cs
# Use script location to find project root dynamically
$UserProfile = $env:USERPROFILE
$AssemblyFolder = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path

# Define server paths for ALL Revit versions
$ServerDir2024 = Join-Path $UserProfile "Müller+Hereth GmbH\31 BIM - Dokumente\General\01 Software\01 Revit-Tools\Updates\Versions\2024"
$ServerDir2023 = Join-Path $UserProfile "Müller+Hereth GmbH\31 BIM - Dokumente\General\01 Software\01 Revit-Tools\Updates\Versions\2023"
$ServerDir2022 = Join-Path $UserProfile "Müller+Hereth GmbH\31 BIM - Dokumente\General\01 Software\01 Revit-Tools\Updates\Versions\2022"

$assemblyInfo = Join-Path $AssemblyFolder "Properties\AssemblyInfo.cs"
$versionFile = Join-Path $AssemblyFolder "version.txt"
$excelVersionFile = Join-Path $AssemblyFolder "version-docs.xlsx"

Write-Host "Project Folder: $AssemblyFolder"
Write-Host "Server 2024: $ServerDir2024"
Write-Host "Server 2023: $ServerDir2023"
Write-Host "Server 2022: $ServerDir2022"
Write-Host "Excel: $excelVersionFile"

# Read and parse current version
Write-Host "Looking for version file at: $versionFile"
Test-Path $versionFile

$currentVersion = Get-Content $versionFile
$parts = $currentVersion.Split('.')
$major = [int]$parts[0]
$minor = [int]$parts[1]
$patch = [int]$parts[2]

# Increment the version based on user input
$versionIncrement = [int](Read-Host "Enter the version increment (major, minor, patch): `n1. Major version increment`n2. Minor version increment`n3. Patch version increment `n").Trim()
switch($versionIncrement){
    1{$major++; $minor = 0; $patch = 0 }
    2{$minor++; $patch = 0 }
    3{$patch++}
    default {
        Write-Host "Invalid input. Please enter 1, 2, or 3."
        exit 1}
}
# Build new version string
$newVersion = "$major.$minor.$patch.0"

# Update version.txt
Set-Content $versionFile $newVersion
Write-Host "[OK] version.txt updated to $newVersion"

# Update AssemblyInfo.cs
(Get-Content $assemblyInfo) |
    ForEach-Object {
        $_ -replace 'AssemblyVersion\(".*"\)', "AssemblyVersion(`"$newVersion`")" `
           -replace 'AssemblyFileVersion\(".*"\)', "AssemblyFileVersion(`"$newVersion`")"
    } | Set-Content $assemblyInfo

Write-Host "[OK] AssemblyInfo.cs updated to $newVersion"

# Read version notes
$message = Read-Host "Enter a message describing the version changes $major.$minor.$patch.0"

# Update local Excel file
$excel = New-Object -ComObject Excel.Application
$excel.Visible = $false
$workbook = $excel.Workbooks.Open($excelVersionFile)
$sheet = $workbook.Sheets.Item(1)

# Write the new version and message to the Excel file
$row = $sheet.UsedRange.Rows.Count + 1
$sheet.Cells.Item($row, 1).Value2 = "$major.$minor.$patch.0"
$sheet.Cells.Item($row, 2).Value2 = $message

Write-Host "[OK] Local Excel file updated with version: $newVersion - message: $message"
# Save and close the Excel file
$workbook.Save()
$workbook.Close($false)
[System.Runtime.InteropServices.Marshal]::ReleaseComObject($sheet) | Out-Null
[System.Runtime.InteropServices.Marshal]::ReleaseComObject($workbook) | Out-Null

$excel.Quit()
[System.Runtime.InteropServices.Marshal]::ReleaseComObject($excel) | Out-Null

[GC]::Collect()
[GC]::WaitForPendingFinalizers()

# Copy updated version files to ALL server paths (2022, 2023, 2024)
foreach ($serverDir in @($ServerDir2024, $ServerDir2023, $ServerDir2022)) {
    if (Test-Path $serverDir) {
        Copy-Item $excelVersionFile -Destination $serverDir -Force
        Copy-Item $versionFile -Destination $serverDir -Force
        Write-Host "[OK] Copied version files to $serverDir"
    } else {
        Write-Host "[WARN] Server path not found, skipping: $serverDir"
    }
}

Write-Host "[OK] Version increment complete for Revit 2022, 2023, and 2024."
