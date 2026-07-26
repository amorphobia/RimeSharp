# RimeSharp.PowerShell 0.2 command reference

All native operations except Receive-RimeNotification share one process-wide,
cancellation-aware gate. Every session parameter is explicit and accepts the
exact live RimeSession returned by New-RimeSession.

## Lifecycle

Start-Rime starts the process lifecycle and creates no session. Common traits
parameters are named-only. FullMaintenance requests a full maintenance check.
The command has no success output.

New-RimeSession creates and returns one explicit session in the active
lifecycle. Multiple sessions may coexist.

Remove-RimeSession destroys one explicit session. Repeating the command with
the same genuine destroyed wrapper is a quiet no-op.

Stop-Rime destroys all registered sessions and finalizes the lifecycle. It
supports WhatIf and Confirm and is a quiet no-op while inactive. A faulted
lifecycle can be recovered by retrying this command.

## Input

Send-RimeKeyEvent processes one KeyCode and optional Mask. It returns one
RimeKeyEventResult with Handled, Commit, Status, Context, Input, and
Notifications.

Send-RimeKey asks librime to simulate one Sequence. It returns final state in
one RimeKeySequenceResult. It is intended for simulation and testing, not real
per-event input handling.

Receive-RimeCommit consumes and returns unread commit text. It emits no object
when no commit is available. Get-RimeInput returns raw input, with no input
represented by an empty string. Get-RimeContext and Get-RimeStatus return fully
managed snapshots.

## Session operations

Get-RimeCandidate enumerates managed candidate information. Select-RimeCandidate
selects a candidate. Remove-RimeCandidate and Invoke-RimeHighlight remove or
highlight a candidate. Set-RimePage changes the candidate page.

Get-RimeSchema lists schemas and marks the current one. Set-RimeSchema changes
the current schema. Get-RimeSwitcherSchema reads available or selected
switcher schemas.

Get-RimeOption and Set-RimeOption read and write boolean options.
Get-RimeStateLabel returns the display label for an option state.

## Notifications

Receive-RimeNotification atomically emits everything pending at the start of
the call. It does not acquire the native gate.

Get-RimeNotificationSource returns the process-wide singleton whose
NotificationReceived event forwards startup and deployment notifications.
Subscribers have independent serial delivery lanes. Subscriber work never runs
on the native callback stack.

## Deployment

Deploy-Rime has two parameter sets:

    Deploy-Rime @traits -Workspace

    Deploy-Rime @traits -ConfigFile relative/path.yaml -VersionKey config_version

Each invocation executes exactly one native operation. Real deployment requires
the module lifecycle to be inactive. WhatIf validates parameters without
requiring an inactive lifecycle or entering native code.

## Config materialization

Get-RimeConfig opens either ConfigId or SchemaId, applies optional Path, and
requires Shape.

Supported scalar markers are String, Boolean, Int32, and Double CLR types.
A Hashtable or OrderedDictionary declares a fixed-key map.

    $shape = @{
        name = [string]
        enabled = [bool]
    }

Use RimeConfigShape.List for homogeneous lists and RimeConfigShape.MapOf for
runtime-key maps:

    $shape = [RimeConfigShape]::MapOf(
        [RimeConfigShape]::List([int]))

Every declared scalar is required. The command does not infer types, parse
YAML, or support optional members.

## Common traits

Start-Rime and Deploy-Rime share AppName, SharedDataDir, UserDataDir,
DistributionName, DistributionCodeName, DistributionVersion, MinLogLevel,
LogDir, PrebuiltDataDir, and StagingDir.

SharedDataDir, UserDataDir, non-empty LogDir, PrebuiltDataDir, and StagingDir
are resolved to absolute FileSystem paths against the caller's PowerShell
location. Paths need not already exist.

## Stable errors

Downstream code should branch on FullyQualifiedErrorId. Stable identifiers are
listed in PLAN.md under Error Contract.
