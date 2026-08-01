<#
.SYNOPSIS
    Builds the minimal librime-compatible native test shim.
#>

[CmdletBinding()]
param(
    [string]$Compiler = 'gcc',
    [string]$IncludeDir,
    [string]$OutputDir = (Join-Path $PSScriptRoot 'bin')
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($IncludeDir)) {
    if ([string]::IsNullOrWhiteSpace($env:LIBRIME_LIB_DIR)) {
        throw 'Specify -IncludeDir or set LIBRIME_LIB_DIR.'
    }

    $IncludeDir = Join-Path (
        Split-Path -Parent $env:LIBRIME_LIB_DIR) 'include'
}

$header = Join-Path $IncludeDir 'rime_api.h'
if (-not (Test-Path -LiteralPath $header -PathType Leaf)) {
    throw "rime_api.h was not found in '$IncludeDir'."
}

$compilerCommand = Get-Command $Compiler -ErrorAction Stop
$source = Join-Path $PSScriptRoot 'rime_test_shim.c'
$null = New-Item -ItemType Directory -Path $OutputDir -Force
$output = Join-Path $OutputDir 'rime.dll'
$compilerArguments = @(
    '-shared'
    '-std=c11'
    '-O2'
    '-static-libgcc'
    "-I$IncludeDir"
    '-o'
    $output
    $source
)

& $compilerCommand.Source @compilerArguments
if ($LASTEXITCODE -ne 0) {
    throw "Native shim compilation failed with exit code $LASTEXITCODE."
}

Write-Host "Native shim built at $output"
