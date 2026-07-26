<#
.SYNOPSIS
    Runs the minimal deterministic native-shim scenarios for version 0.2.

.DESCRIPTION
    Run this script in a fresh PowerShell process after Build-NativeShim.ps1.
    The shim covers the release-critical cancellation, recovery, deployment,
    and native-gate scenarios selected for the initial version 0.2 test set.
#>

[CmdletBinding()]
param(
    [string]$ModulePath = (
        Join-Path $PSScriptRoot '..\RimeSharp.PowerShell.psd1'),
    [string]$ShimDirectory = (
        Join-Path $PSScriptRoot 'NativeShim\bin'),
    [int]$WaitTimeoutMilliseconds = 10000
)

$ErrorActionPreference = 'Stop'

function Assert-True {
    param(
        [Parameter(Mandatory)]
        [bool]$Condition,
        [Parameter(Mandatory)]
        [string]$Message
    )

    if (-not $Condition) { throw $Message }
}

function Assert-Equal {
    param(
        $Expected,
        $Actual,
        [Parameter(Mandatory)]
        [string]$Message
    )

    if ($Expected -ne $Actual) {
        throw "$Message Expected '$Expected', got '$Actual'."
    }
}

function Assert-ErrorId {
    param(
        [Parameter(Mandatory)]
        [scriptblock]$Action,
        [Parameter(Mandatory)]
        [string]$ExpectedId
    )

    try {
        & $Action
    }
    catch {
        if (-not $_.FullyQualifiedErrorId.StartsWith($ExpectedId)) {
            throw "Expected error '$ExpectedId', got '$($_.FullyQualifiedErrorId)'."
        }
        return $_
    }

    throw "Expected error '$ExpectedId', but the operation succeeded."
}

function Get-ShimLog {
    if (-not (Test-Path -LiteralPath $script:shimLog -PathType Leaf)) {
        return @()
    }

    return @(Get-Content -LiteralPath $script:shimLog)
}

function Wait-ShimEvent {
    param(
        [Parameter(Mandatory)]
        [string]$EventName,
        [Parameter(Mandatory)]
        [PowerShell]$Worker,
        [Parameter(Mandatory)]
        $AsyncResult
    )

    $deadline = [DateTime]::UtcNow.AddMilliseconds(
        $WaitTimeoutMilliseconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        if ((Get-ShimLog) -contains $EventName) { return }
        if ($AsyncResult.IsCompleted) {
            $invocationFailure = $null
            try {
                $null = $Worker.EndInvoke($AsyncResult)
            }
            catch {
                $invocationFailure = $_
            }

            $workerErrors = @($Worker.Streams.Error | ForEach-Object {
                $_.ToString()
            })
            $details = @(
                if ($null -ne $invocationFailure) {
                    $invocationFailure.ToString()
                }
                $workerErrors
            ) -join [Environment]::NewLine
            throw (
                "Worker completed before native shim event '$EventName'. " +
                $details)
        }
        Start-Sleep -Milliseconds 20
    }

    throw "Timed out waiting for native shim event '$EventName'."
}

function Assert-LogSubsequence {
    param(
        [Parameter(Mandatory)]
        [string[]]$Expected
    )

    $actual = @(Get-ShimLog)
    $position = 0
    foreach ($entry in $actual) {
        if ($position -lt $Expected.Count -and
            $entry -eq $Expected[$position]) {
            $position++
        }
    }

    if ($position -ne $Expected.Count) {
        throw (
            "Expected log subsequence '{0}', got '{1}'." -f
            ($Expected -join ', '),
            ($actual -join ', '))
    }
}

function Reset-ShimScenario {
    param(
        [Parameter(Mandatory)]
        [string]$Scenario
    )

    [RimeSharpNativeShimControl]::Reset()
    [RimeSharpNativeShimControl]::Configure($Scenario, $script:shimLog)
    [IO.File]::WriteAllText($script:shimLog, '')
}

function New-ModuleWorker {
    $worker = [PowerShell]::Create()
    $null = $worker.AddCommand('Import-Module').
        AddParameter('Name', $ModulePath).
        AddParameter('Force').
        Invoke()
    if ($worker.HadErrors) {
        $message = ($worker.Streams.Error | Out-String)
        $worker.Dispose()
        throw "Worker module import failed: $message"
    }

    $worker.Commands.Clear()
    return $worker
}

function Stop-WorkerInvocation {
    param(
        [Parameter(Mandatory)]
        [PowerShell]$Worker,
        [Parameter(Mandatory)]
        $AsyncResult
    )

    $Worker.Stop()
    try {
        $null = $Worker.EndInvoke($AsyncResult)
    }
    catch {
        $exception = $_.Exception
        while ($null -ne $exception -and
            $exception -isnot
                [Management.Automation.PipelineStoppedException]) {
            $exception = $exception.InnerException
        }
        if ($null -eq $exception) {
            throw
        }
    }
}

$shim = Join-Path $ShimDirectory 'rime.dll'
if (-not (Test-Path -LiteralPath $shim -PathType Leaf)) {
    throw (
        "Native shim '$shim' was not found. Run " +
        'Tests/NativeShim/Build-NativeShim.ps1 first.')
}

$savedLibraryDirectory = $env:LIBRIME_LIB_DIR
$script:shimLog = Join-Path (
    [IO.Path]::GetTempPath()) (
    "rimesharp-native-shim-$([Guid]::NewGuid().ToString('N')).log")
$env:LIBRIME_LIB_DIR = (Resolve-Path -LiteralPath $ShimDirectory).ProviderPath
[IO.File]::WriteAllText($script:shimLog, '')

$traits = @{
    AppName = 'RimeSharp.PowerShell.NativeShim'
    SharedDataDir = $PSScriptRoot
    UserDataDir = $PSScriptRoot
}

try {
    Import-Module $ModulePath -Force
    if (-not ('RimeSharpNativeShimControl' -as [type])) {
        Add-Type -TypeDefinition @'
using System.Runtime.InteropServices;

public static class RimeSharpNativeShimControl
{
    [DllImport("rime.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern void rimesharp_shim_reset();

    [DllImport("rime.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void rimesharp_shim_configure(
        [MarshalAs(UnmanagedType.LPStr)] string scenario,
        [MarshalAs(UnmanagedType.LPWStr)] string logPath);

    [DllImport("rime.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern void rimesharp_shim_reset_concurrency();

    [DllImport("rime.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern int rimesharp_shim_get_max_concurrency();

    [DllImport("rime.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int rimesharp_shim_wait_for_second_find_session(
        int timeoutMilliseconds);

    [DllImport("rime.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void rimesharp_shim_release_find_session();

    public static void Reset() => rimesharp_shim_reset();
    public static void Configure(string scenario, string logPath) =>
        rimesharp_shim_configure(scenario, logPath);
    public static void ResetConcurrency() => rimesharp_shim_reset_concurrency();
    public static int GetMaxConcurrency() =>
        rimesharp_shim_get_max_concurrency();
    public static bool WaitForSecondFindSession(int timeoutMilliseconds) =>
        rimesharp_shim_wait_for_second_find_session(timeoutMilliseconds) != 0;
    public static void ReleaseFindSession() =>
        rimesharp_shim_release_find_session();
}
'@
    }

    Write-Host 'SCENARIO startup cancellation rolls back and remains retryable'
    Reset-ShimScenario -Scenario 'start_cancel'
    $worker = New-ModuleWorker
    try {
        $null = $worker.AddCommand('Start-Rime').AddParameters($traits)
        $async = $worker.BeginInvoke()
        Wait-ShimEvent -EventName 'initialize_enter' -Worker $worker `
            -AsyncResult $async
        Stop-WorkerInvocation -Worker $worker -AsyncResult $async
    }
    finally {
        $worker.Dispose()
    }
    Assert-LogSubsequence -Expected @(
        'setup'
        'set_notification_handler'
        'initialize_enter'
        'initialize_exit'
        'finalize_enter'
        'finalize_callback'
        'finalize_exit'
    )
    $rollbackNotifications = @(Receive-RimeNotification)
    Assert-True (
        $rollbackNotifications.Where({
            $_.MessageType -eq 'shim' -and $_.MessageValue -eq 'finalize'
        }).Count -eq 1
    ) 'The rollback Finalize callback must be routed before bridge release.'
    Start-Rime @traits
    Stop-Rime -Confirm:$false
    Write-Host 'PASS startup cancellation rollback'

    Write-Host 'SCENARIO cancelled session creation retains failed cleanup'
    Reset-ShimScenario -Scenario 'new_session_cancel'
    Start-Rime @traits
    $worker = New-ModuleWorker
    try {
        $null = $worker.AddCommand('New-RimeSession')
        $async = $worker.BeginInvoke()
        Wait-ShimEvent -EventName 'create_session_enter' -Worker $worker `
            -AsyncResult $async
        Stop-WorkerInvocation -Worker $worker -AsyncResult $async
    }
    finally {
        $worker.Dispose()
    }
    Assert-LogSubsequence -Expected @(
        'create_session_enter'
        'create_session_exit'
        'destroy_session'
        'destroy_session_false'
    )
    $null = Assert-ErrorId {
        New-RimeSession
    } 'RimeLifecycleFaulted'
    Stop-Rime -Confirm:$false
    Assert-LogSubsequence -Expected @(
        'destroy_session_false'
        'destroy_session'
        'finalize_enter'
        'finalize_exit'
    )
    Start-Rime @traits
    Stop-Rime -Confirm:$false
    Write-Host 'PASS cancelled session cleanup recovery'

    Write-Host 'SCENARIO concurrent runspaces serialize native calls'
    Reset-ShimScenario -Scenario 'gate'
    Start-Rime @traits
    $firstSession = New-RimeSession
    $secondSession = New-RimeSession
    [RimeSharpNativeShimControl]::ResetConcurrency()
    $firstWorker = New-ModuleWorker
    $secondWorker = New-ModuleWorker
    $secondReady = [Threading.ManualResetEventSlim]::new($false)
    $firstAsync = $null
    $secondAsync = $null
    try {
        $null = $firstWorker.AddCommand('Get-RimeInput').
            AddParameter('Session', $firstSession)
        $firstAsync = $firstWorker.BeginInvoke()
        Wait-ShimEvent -EventName 'find_session_enter' -Worker $firstWorker `
            -AsyncResult $firstAsync

        $null = $secondWorker.AddScript({
            param($targetSession, $ready)
            $ready.Set()
            Get-RimeInput -Session $targetSession
        }).AddArgument($secondSession).AddArgument($secondReady)
        $secondAsync = $secondWorker.BeginInvoke()
        Assert-True (
            $secondReady.Wait($WaitTimeoutMilliseconds)) (
            'The second worker must begin its gate-contending command.')
        Assert-Equal $false (
            [RimeSharpNativeShimControl]::WaitForSecondFindSession(1000)) (
            'The second call must not enter native code before release.')

        [RimeSharpNativeShimControl]::ReleaseFindSession()
        $null = $firstWorker.EndInvoke($firstAsync)
        $null = $secondWorker.EndInvoke($secondAsync)
        Assert-Equal 0 $firstWorker.Streams.Error.Count (
            'The first concurrent native call must succeed.')
        Assert-Equal 0 $secondWorker.Streams.Error.Count (
            'The second concurrent native call must succeed.')

        $firstWorker.Commands.Clear()
        $firstWorker.Streams.Error.Clear()
        $removalResult = @($firstWorker.AddScript({
            Remove-Module RimeSharp.PowerShell -Force -ErrorAction Stop
            [bool](Get-Module RimeSharp.PowerShell)
        }).Invoke())
        Assert-Equal 0 $firstWorker.Streams.Error.Count (
            'Removing the first worker module must not report an error.')
        Assert-Equal $false ([bool]$removalResult[0]) (
            'The first worker module must actually be removed.')

        $secondWorker.Commands.Clear()
        $secondWorker.Streams.Error.Clear()
        $acceleratorResult = $secondWorker.AddScript({
            [RimeConfigShape]::List([string]).GetType().FullName
        }).Invoke()
        Assert-Equal 0 $secondWorker.Streams.Error.Count (
            'The second worker accelerator check must not report an error.')
        Assert-Equal 'RimeSharp.PowerShell.RimeConfigShape' (
            $acceleratorResult[0]) (
            'Removing one runspace module must not remove the process accelerator.')
    }
    finally {
        [RimeSharpNativeShimControl]::ReleaseFindSession()
        $secondReady.Dispose()
        $firstWorker.Dispose()
        $secondWorker.Dispose()
    }
    Assert-Equal 1 (
        [RimeSharpNativeShimControl]::GetMaxConcurrency()) (
        'The native gate must limit concurrent entry to one.')
    Remove-RimeSession -Session $firstSession
    Remove-RimeSession -Session $secondSession
    Stop-Rime -Confirm:$false
    Write-Host 'PASS native gate and accelerator lifetime'

    Write-Host 'SCENARIO deployment false result still finalizes'
    Reset-ShimScenario -Scenario 'deploy_fail'
    $deploymentError = Assert-ErrorId {
        Deploy-Rime @traits -Workspace -Confirm:$false
    } 'RimeDeploymentFailed'
    $deployment = $deploymentError.TargetObject
    Assert-Equal $false $deployment.NativeOperationSucceeded (
        'The diagnostic must retain the native false result.')
    Assert-Equal $true $deployment.CleanupSucceeded (
        'Finalize cleanup must succeed after native deployment failure.')
    Assert-Equal $false $deployment.Succeeded (
        'The deployment diagnostic must not report success.')
    Assert-True ($deployment.Notifications.Count -eq 1) (
        'The Finalize callback must remain in the deployment diagnostic.')
    Assert-LogSubsequence -Expected @(
        'setup'
        'set_notification_handler'
        'deployer_initialize'
        'deploy'
        'finalize_enter'
        'finalize_callback'
        'finalize_exit'
    )
    Start-Rime @traits
    Stop-Rime -Confirm:$false
    Write-Host 'PASS deployment failure cleanup'

    Write-Host '4 native shim scenarios passed.'
}
finally {
    Stop-Rime -Confirm:$false -ErrorAction SilentlyContinue
    Remove-Module RimeSharp.PowerShell -Force -ErrorAction SilentlyContinue
    $env:LIBRIME_LIB_DIR = $savedLibraryDirectory
    if (Test-Path -LiteralPath $script:shimLog -PathType Leaf) {
        [IO.File]::Delete($script:shimLog)
    }
}
