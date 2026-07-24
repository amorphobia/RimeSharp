# PowerShell 7+ runs on CoreCLR (.NET 8) — can only load net8.0 assemblies.
# Windows PowerShell 5.1 runs on .NET Framework CLR — can only load net472 assemblies.
# Detection is based on PSEdition, not on what runtimes are installed on the machine.

# Packaged PowerShell hosts do not ask the Windows loader to search PATH for
# native libraries. Resolve LIBRIME_LIB_DIR and PATH explicitly, then preload
# rime.dll before any managed RIME type can initialize its P/Invoke table.
if ($env:OS -eq 'Windows_NT') {
    $librimeLibDir = [Environment]::GetEnvironmentVariable('LIBRIME_LIB_DIR')
    $searchDirectories = @()
    if (-not [string]::IsNullOrWhiteSpace($librimeLibDir)) {
        $searchDirectories += $librimeLibDir
    }
    if (-not [string]::IsNullOrWhiteSpace($env:PATH)) {
        $searchDirectories += $env:PATH -split [IO.Path]::PathSeparator
    }

    $rimeDll = $null
    foreach ($directory in $searchDirectories) {
        if ([string]::IsNullOrWhiteSpace($directory)) { continue }

        $directory = [Environment]::ExpandEnvironmentVariables(
            $directory.Trim().Trim([char]'"'))
        $candidate = [IO.Path]::Combine($directory, 'rime.dll')
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            $rimeDll = (Resolve-Path -LiteralPath $candidate).ProviderPath
            break
        }
    }

    if ($null -ne $rimeDll) {
        if (-not ('RimeSharp.PowerShell.NativeLibraryLoader' -as [type])) {
            Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace RimeSharp.PowerShell
{
    public static class NativeLibraryLoader
    {
        private const uint LoadLibrarySearchDllLoadDir = 0x00000100;
        private const uint LoadLibrarySearchDefaultDirs = 0x00001000;

        [DllImport("kernel32.dll", EntryPoint = "LoadLibraryExW",
            CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr LoadLibraryEx(
            string fileName,
            IntPtr fileHandle,
            uint flags);

        public static IntPtr Load(string path)
        {
            var handle = LoadLibraryEx(
                path,
                IntPtr.Zero,
                LoadLibrarySearchDllLoadDir | LoadLibrarySearchDefaultDirs);
            if (handle == IntPtr.Zero)
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "Failed to load native RIME library: " + path);
            }

            return handle;
        }
    }
}
'@
        }

        $script:rimeNativeHandle =
            [RimeSharp.PowerShell.NativeLibraryLoader]::Load($rimeDll)
    } elseif (-not [string]::IsNullOrWhiteSpace($librimeLibDir)) {
        throw "rime.dll not found in LIBRIME_LIB_DIR or PATH."
    }
}

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
    'Remove-RimeCandidate'
    'Invoke-RimeHighlight'
)
