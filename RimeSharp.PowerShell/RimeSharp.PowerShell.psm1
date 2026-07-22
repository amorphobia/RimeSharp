# PowerShell 7+ runs on CoreCLR (.NET 8) — can only load net8.0 assemblies.
# Windows PowerShell 5.1 runs on .NET Framework CLR — can only load net472 assemblies.
# Detection is based on PSEdition, not on what runtimes are installed on the machine.

$tf = if ($PSVersionTable.PSEdition -eq 'Core') { 'net8.0' } else { 'net472' }
$moduleDir = $PSScriptRoot

# Probe Release first (published module), then Debug (development).
$loaded = $false
foreach ($config in @('Release', 'Debug')) {
    $assemblyDir = Join-Path $moduleDir "bin" $config $tf
    $binaryPath = Join-Path $assemblyDir 'RimeSharp.PowerShell.dll'
    if (-not (Test-Path $binaryPath)) { continue }

    $rimeAssembly = Join-Path $assemblyDir 'RimeSharp.dll'
    if (Test-Path $rimeAssembly) {
        Add-Type -Path $rimeAssembly
    }

    Import-Module -Name $binaryPath -DisableNameChecking
    $loaded = $true
    break
}

if (-not $loaded) {
    throw "RimeSharp.PowerShell.dll not found. Run build.ps1 first."
}

Export-ModuleMember -Cmdlet @(
    'Start-Rime'
    'Stop-Rime'
    'Send-RimeKey'
    'Send-RimeKeyEvent'
    'Get-RimeCommit'
    'Get-RimeContext'
    'Get-RimeStatus'
    'Select-RimeCandidate'
    'Set-RimePage'
    'Get-RimeSchema'
    'Set-RimeSchema'
    'Get-RimeOption'
    'Set-RimeOption'
)
