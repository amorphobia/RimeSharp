<#
.SYNOPSIS
    Builds RimeSharp.PowerShell and copies outputs into the module layout.

.DESCRIPTION
    Runs `dotnet build` for both target frameworks, then assembles the
    output DLLs (RimeSharp.dll + RimeSharp.PowerShell.dll) into framework
    subdirectories under the module folder so it is ready for Import-Module.

    The result directory structure:
        RimeSharp.PowerShell/
        ├── RimeSharp.PowerShell.psd1
        ├── RimeSharp.PowerShell.psm1
        ├── net8.0/
        │   ├── RimeSharp.dll
        │   └── RimeSharp.PowerShell.dll
        └── net472/
            ├── RimeSharp.dll
            └── RimeSharp.PowerShell.dll
#>

[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

Write-Host "Building RimeSharp (multi-target)..." -ForegroundColor Cyan
dotnet build "$root\RimeSharp\RimeSharp.csproj" -c $Configuration
if ($LASTEXITCODE -ne 0) { throw "RimeSharp build failed." }

Write-Host "Building RimeSharp.PowerShell (multi-target)..." -ForegroundColor Cyan
dotnet build "$PSScriptRoot\RimeSharp.PowerShell.csproj" -c $Configuration
if ($LASTEXITCODE -ne 0) { throw "RimeSharp.PowerShell build failed." }

Write-Host "Module ready at: $PSScriptRoot" -ForegroundColor Green
Write-Host "Import with: Import-Module $PSScriptRoot\RimeSharp.PowerShell.psd1" -ForegroundColor DarkGray
