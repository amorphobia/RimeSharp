# RimeSharp.PowerShell

RimeSharp.PowerShell 0.2 provides process-owned, serialized PowerShell access
to librime. PowerShell 7.4 on Windows x64 is the first supported release
target. The source continues to compile for net472, but the 0.2 module manifest
declares Core edition only.

## Lifecycle and sessions

The lifecycle and its sessions are separate. Start-Rime creates no implicit
session and emits no success object. Every session-scoped command requires an
explicit module-owned RimeSession.

    $traits = @{
        AppName       = 'My.Application'
        SharedDataDir = './shared'
        UserDataDir   = './user'
    }

    Start-Rime @traits
    $session = New-RimeSession

    try {
        $result = Send-RimeKeyEvent -Session $session -KeyCode 97
        $result.Input
        $result.Context.Menu.Candidates
    }
    finally {
        Remove-RimeSession -Session $session -ErrorAction Continue
        Stop-Rime -Confirm:$false
    }

One lifecycle can own multiple sessions. The module serializes all native
operations across runspaces while keeping session state and atomic key results
isolated.

## Notifications

The module always owns librime's native notification handler. Poll queued
notifications with Receive-RimeNotification. Startup and deployment
notifications can also be observed through the process-wide managed event
source:

    $source = Get-RimeNotificationSource
    $eventParameters = @{
        InputObject = $source
        EventName = 'NotificationReceived'
        Action = { $EventArgs.Notification }
    }
    $subscription = Register-ObjectEvent @eventParameters

Native callbacks only copy and route immutable managed notifications. Event
subscribers run asynchronously on independent serial delivery lanes.

## Deployment

Deploy-Rime is a non-coordinating primitive and requires the process lifecycle
to be inactive. Callers remain responsible for coordination with other
processes that share writable RIME directories.

    Deploy-Rime @traits -Workspace -Confirm:$false

    $deployment = @{
        ConfigFile = 'build/default.yaml'
        VersionKey = 'config_version'
        Confirm = $false
    }
    Deploy-Rime @traits @deployment

Each invocation performs exactly one selected native operation and returns one
RimeDeploymentResult only on success.

## Configuration snapshots

Get-RimeConfig uses an explicit shape. It does not parse YAML, infer node
types, or support optional members.

    $shape = @{
        name = [string]
        enabled = [bool]
        values = [RimeConfigShape]::List([int])
    }

    $snapshot = Get-RimeConfig -ConfigId 'default' -Shape $shape

See PLAN.md for the complete 0.2 behavioral and error contract.

### Known config iterator risk

The upstream RimeConfig.GetList and RimeConfig.GetMap wrappers call ConfigEnd
only after normal iteration. An unexpected exception after an iterator begins
can therefore skip ConfigEnd. RimeSharp.PowerShell prevents ordinary shape-read
failures from escaping its iterator callbacks and always closes the config,
but it cannot make the upstream iterator wrappers exception-safe. Version 0.2
assumes native iteration and managed collection insertion complete without
throwing; a try/finally fix remains tracked as upstream RimeSharp work.

## Development verification

Agents do not run restore, build, tests, or the project in this repository.
Use the repository commands supplied during implementation and return their
complete output for review.

The minimal deterministic native shim requires a Windows x64 GCC toolchain and
librime headers:

    pwsh -NoProfile -File RimeSharp.PowerShell/Tests/NativeShim/Build-NativeShim.ps1

Run its selected version 0.2 scenarios in a fresh PowerShell process:

    pwsh -NoProfile -File RimeSharp.PowerShell/Tests/NativeShimIntegration.ps1

NativeScenarioMatrix.md separates the automated shim coverage from scenarios
deferred to later test expansion.
