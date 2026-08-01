# RimeSharp.PowerShell 0.2 Design

## Approval Status

**Status: Approved on 2026-08-01.**

This document records the design decisions agreed before implementation. It is
the approved implementation contract for version 0.2. Implementation work is
authorized to proceed according to this document.

The current source code implements the earlier 0.1 design and does not yet
conform to this document.

## Scope

RimeSharp.PowerShell provides managed PowerShell cmdlets for applications that
embed librime. The public API models general RIME concepts, but new work is
limited to requirements demonstrated by a downstream application such as
BlueInk.

Version 0.2 adds or redesigns:

- process lifecycle and concurrency ownership;
- explicit management of multiple sessions within one lifecycle;
- atomic key-event results and raw input access;
- automatic notification forwarding;
- non-coordinating deployment primitives;
- explicit-shape config materialization;
- stable PowerShell error contracts.

The following are out of scope:

- YAML parsing or serialization in PowerShell;
- config node-type inference;
- optional config shape members;
- `user_config_open`;
- custom librime module lists;
- cross-process deployment coordination;
- bundling a native runtime in the current change;
- editing arbitrary RIME configuration.

## Process Ownership and Concurrency

RimeSharp.PowerShell owns librime's process-global state while its cmdlets are
in use. Mixing these cmdlets with direct calls through `RimeSharp.Rime`,
another binding, or handwritten P/Invoke in the same process is unsupported.
Such calls can replace the notification handler, destroy sessions, or mutate
state without the module's knowledge.

Process-global means global within one operating-system process, not
machine-global. Separate PowerShell processes have independent RIME
lifecycles, gates, notification bridges, and session registries. Callers that
share writable RIME data directories across processes are responsible for
external coordination. A broker may own the lifecycle in one process and
represent its managed sessions to IPC clients with broker-defined logical
handles; a `RimeSession` object itself is never a cross-process handle.

All native operations except notification dequeueing use one process-wide,
cancellation-aware gate. This includes lifecycle, deployment, session
validation and operations, key processing, queries, and the complete config
materialization operation. Multiple PowerShell runspaces therefore serialize
their access to librime.

`Receive-RimeNotification` only accesses a managed queue and does not acquire
the native-operation gate.

Native notification callbacks never call PowerShell APIs or downstream
application code. For live observation, the callback copies the native
arguments into an immutable managed notification, enqueues it for a separate
managed dispatcher, and returns. The native operation does not wait for that
dispatcher or for downstream notification processing.

Cancellation is honored while waiting for the gate. Once a synchronous native
call begins, the module does not try to interrupt it by concurrently calling
`Finalize`, clearing the handler, or destroying a session. A cancellation
request is observed after the native call returns and after required cleanup;
the cmdlet then throws `OperationCanceledException` and does not emit its normal
result.

## Common Traits Contract

`Start-Rime` and `Deploy-Rime` expose the same common traits parameters and use
one internal construction and path-normalization implementation. Applications
can keep the common values in one hashtable and splat it into both cmdlets.
No public profile object is introduced.

Common parameters:

```text
-AppName <string>                 default: RimeSharp.PowerShell
-SharedDataDir <string>           default: shared
-UserDataDir <string>             default: user
-DistributionName <string?>
-DistributionCodeName <string?>
-DistributionVersion <string?>
-MinLogLevel <int>                default: 0
-LogDir <string?>
-PrebuiltDataDir <string?>
-StagingDir <string?>
```

All traits parameters are named-only.

Validation rules:

- `AppName` must not be empty or whitespace.
- `SharedDataDir` and `UserDataDir` must be non-empty FileSystem paths. They
  are resolved against the caller's PowerShell location to absolute paths.
- `PrebuiltDataDir` and `StagingDir` are optional. When present, each must be a
  non-empty FileSystem path and is resolved to an absolute path.
- `LogDir` is optional. An explicit empty string is preserved because librime
  defines it as stderr-only logging. A non-empty value is resolved as a
  FileSystem path.
- `MinLogLevel` must be in the inclusive range `0..3`.
- Distribution strings are optional. A supplied value must not be whitespace.
  The module does not interpret their formats.
- Paths are not required to exist during parameter validation, and symlinks are
  not canonicalized. The native operation decides whether its paths are usable.

Custom module selection is deliberately unsupported. The existing
`Start-Rime -Modules` parameter is removed, `Deploy-Rime` does not add one, and
the native `RimeTraits.modules` pointer remains null so that librime selects its
appropriate default modules.

The existing public `RimeTraits.Modules` marshalling defect in RimeSharp is not
changed here. It will be reported and handled as separate upstream work.

## Lifecycle

Lifecycle and session management are deliberately separate:

```powershell
Start-Rime @traits
$session = New-RimeSession

Send-RimeKeyEvent -Session $session -KeyCode 97
Get-RimeInput -Session $session

Remove-RimeSession -Session $session
Stop-Rime
```

One process has at most one active RIME lifecycle. That lifecycle may manage
zero, one, or multiple explicit sessions. There is no implicit current or
default session.

### Lifecycle States

The process lifecycle has the internal states `Inactive`, `Starting`, `Active`,
`Stopping`, and `Faulted`:

```text
Inactive -> Starting -> Active -> Stopping -> Inactive
               |                       |
               +-- rollback failure ---+--> Faulted
                                               |
                                               +-- successful Stop-Rime cleanup
                                                   -> Inactive
```

Ordinary lifecycle and session operations require `Active`. `Start-Rime`
requires `Inactive`. `Stop-Rime` accepts `Active` or `Faulted` and is a quiet
no-op in `Inactive`. `Faulted` means that native finalization did not complete;
it is not a usable active lifecycle and exists only so that cleanup state,
including a rooted notification handler, can be retained for a later
`Stop-Rime` retry. Other operations fail with `RimeLifecycleFaulted`.

### `Start-Rime`

```powershell
Start-Rime @traits [-FullMaintenance]
```

The operation:

1. acquires the process-wide gate and changes `Inactive` to `Starting`;
2. calls `Setup`;
3. registers and strongly roots the module-owned native notification handler;
4. calls `Initialize`;
5. calls `StartMaintenance(false)` by default, or
   `StartMaintenance(true)` when `-FullMaintenance` is specified;
6. calls `JoinMaintenanceThread` only when `StartMaintenance` returns true;
7. changes `Starting` to `Active` without creating a session.

`Start-Rime` has no success output. A lifecycle may remain active with zero
sessions. Applications create each input context explicitly with
`New-RimeSession` after startup has completed. `Start-Rime` does not provide an
option that creates an initial session.

Initialization and maintenance notifications are retained in the ordinary
managed queue. They may also be observed through the decoupled managed
notification forwarding mechanism described below.

A false result from `StartMaintenance(false)` does not by itself fail startup;
it can mean that no maintenance was needed. A second `Start-Rime` while the
current process already has an active module lifecycle is a terminating error.

Startup is atomic from the caller's perspective. Validation or a failure known
to occur before the first native invocation returns the state to `Inactive`
without native cleanup. Once `Setup` may have entered native code, the module
conservatively treats native state as present. Any subsequent catchable
exception or cancellation observed at a safe boundary causes rollback before
the cmdlet terminates:

1. keep the native notification handler strongly rooted if it was registered;
2. attempt `Finalize` without concurrently interrupting a native call or
   maintenance thread;
3. if native finalization succeeds, release bridge state, clear the lifecycle
   generation and session registry, and change to `Inactive`;
4. if native finalization does not complete, retain the handler and minimum
   cleanup state and change to `Faulted`.

No failed startup is retained as `Active`. A successful rollback leaves the
process retryable, so a later `Start-Rime` is allowed. A failed rollback does
not falsely report `RimeLifecycleAlreadyActive`; later starts fail with
`RimeLifecycleFaulted` until `Stop-Rime` completes cleanup. If startup and
rollback both fail, the startup exception remains the primary failure and the
rollback failure is exposed through the lifecycle diagnostic target described
in the error contract.

There is no `-SetDefaultSession`, and the module neither reads nor writes
`$global:RimeDefaultSession`.

### `New-RimeSession`

```powershell
New-RimeSession
```

The cmdlet accepts no parameters. It requires an active lifecycle, acquires the
process-wide native gate, calls `CreateSession` exactly once, and treats native
ID `0` as a terminating `RimeSessionCreationFailed` error. On success it creates
and registers one module-owned `RimeSession`, then emits that object.

Cancellation and failure after native creation are atomic from the caller's
perspective. After `CreateSession` returns a nonzero ID, the module creates and
registers the wrapper internally before the next cancellation boundary, but it
does not emit the wrapper yet. If cancellation or another failure is then
observed before output:

1. attempt `DestroySession` for the newly created native session;
2. if destruction succeeds, remove the registry entry, mark the wrapper
   destroyed, and terminate with the original cancellation or failure;
3. if destruction does not complete, retain the non-emitted registry entry and
   the minimum cleanup state, change the lifecycle to `Faulted`, and expose the
   destruction failure as secondary lifecycle diagnostic information.

The original cancellation or creation-stage failure remains primary. No
cancelled invocation emits a session, and no native session that the caller
cannot address is silently abandoned. `Stop-Rime` owns recovery if immediate
destruction fails. Cancellation observed before the native call creates
nothing and requires no rollback.

A lifecycle may own zero, one, or multiple sessions. Creating or removing a
session does not initialize, finalize, or otherwise replace the lifecycle.

### `RimeSession`

`RimeSession` is created only by the module. It exposes a read-only `Id : ulong`
for logging and notification correlation. Internally it carries an unforgeable
lifecycle-generation token, a per-session identity token, and destroyed state.
The active lifecycle maintains a registry from native ID to the exact managed
session object.

Every session-scoped cmdlet requires an explicit `-Session`, with pipeline
binding where appropriate. Validation requires:

- an active module lifecycle;
- a lifecycle-generation token matching the active lifecycle;
- a registry entry for the native ID that refers to the exact same managed
  object;
- a session that has not been destroyed;
- a successful native `FindSession`.

The registry identity check prevents a removed wrapper from targeting a later
session if librime reuses the same numeric ID within one lifecycle. The
generation check prevents reuse across lifecycle restarts. A forged or
deserialized object from another process is not accepted as a module-owned
session. The same live managed session object may be passed between runspaces
in the owning process.

### `Remove-RimeSession`

```powershell
Remove-RimeSession -Session <RimeSession>
```

`-Session` is mandatory and accepts pipeline input. The cmdlet acquires the
process-wide gate. For a current registered session it calls `DestroySession`
exactly once, removes the registry entry, marks the wrapper destroyed, and
emits no success output. It never finalizes librime and does not affect another
session.

Removal is idempotent for the same genuine module-owned wrapper after it has
already been marked destroyed, including a wrapper invalidated by `Stop-Rime`.
Such a repeated removal is a quiet no-op. A live wrapper from another lifecycle,
a forged or deserialized object, or an object whose registry identity does not
match is a terminating `RimeSessionInvalid` error.

If native `DestroySession` returns false for a session that the registry still
considered live, the module removes and invalidates the wrapper and throws
`RimeSessionRemovalFailed`; it can no longer claim that native session as
valid.

### `Stop-Rime`

```powershell
Stop-Rime [-WhatIf] [-Confirm]
```

`Stop-Rime` is process-lifecycle-scoped and accepts no session parameter. It
accepts an `Active` or `Faulted` lifecycle and is idempotent in `Inactive`.

For an active lifecycle it changes `Active` to `Stopping`. For a faulted
lifecycle it retries the retained cleanup. In either case it attempts all
cleanup steps even if an earlier step fails:

- call `DestroySession` for every session still represented by the lifecycle
  registry, continuing after individual failures;
- immediately mark and remove a wrapper only when its native destruction is
  known to have succeeded;
- retain the notification handler through native finalization;
- call `Finalize` even if individual session destruction failed;
- if finalization succeeds, mark every remaining wrapper destroyed, clear the
  registry and generation, release handler and bridge state, and change to
  `Inactive`;
- if finalization does not complete, retain the handler, unresolved registry
  entries, and minimum cleanup state, and change to `Faulted`.

Successful native finalization is the cleanup boundary: it invalidates all
remaining wrappers even if an earlier individual `DestroySession` reported a
failure. Failed finalization must not release callback state or claim that
uncertain native state is gone. A later `Stop-Rime` retries cleanup from
`Faulted`.

After attempting all cleanup, the cmdlet throws `RimeLifecycleStopFailed` if
any step failed, even when finalization ultimately left the lifecycle
`Inactive`. The first cleanup failure is primary and later failures are exposed
through the lifecycle diagnostic target. It uses `SupportsShouldProcess` with
`ConfirmImpact = Medium`. `-WhatIf` does not mutate state. An inactive
lifecycle is a quiet no-op and does not emit a misleading WhatIf message.

## Notifications

RimeSharp.PowerShell owns the single native notification handler. The public
`Register-RimeNotification` cmdlet is removed. The callback only copies native
arguments into fully managed immutable objects and routes them; it never
executes PowerShell, writes to a pipeline, calls another librime API, or invokes
downstream application code.

The native handler's `context_object` identifies internal bridge state for the
current lifecycle. It is not an application subscription token and has no
per-notification business meaning. The bridge may use a managed handle
internally, but that handle remains strongly rooted until native finalization
has completed and is never exposed in `RimeNotification`.

`RimeNotification` contains:

```text
SessionId   : ulong        # 0 means a non-session notification
MessageType : string
MessageValue: string
OptionName  : string?
OptionState : bool?
```

`OptionName` and `OptionState` are convenience projections for `option`
notifications. The old `Session` and `ContextObject` properties are removed
because they imply ownership or semantics that the module cannot guarantee.
No module-generated timestamp or sequence number is added.

`SessionId` always preserves the numeric ID supplied by librime. The module
never constructs or exposes a managed `RimeSession` merely from that number.
Outside a synchronous operation on a known session, the ID is correlation data,
not a durable managed identity. An ID absent from the current registry remains
unresolved and may still be retained in the ordinary notification queue; it is
never converted into or accompanied by a managed wrapper.

The C API notification does not contain a lifecycle generation. If librime
were to deliver an old notification after reusing the same numeric native ID,
the PowerShell layer could not distinguish the two events from callback data
alone. The design therefore relies on librime's callback lifetime and
synchronous session notification behavior and does not claim a stronger native
guarantee. Managed wrapper validation remains strict regardless of notification
correlation.

Every notification has exactly one durable destination:

- callbacks received during one `ProcessKey` call whose `SessionId` matches the
  exact target session are placed only in that `RimeKeyEventResult`;
- callbacks received during one `SimulateKeySequence` call whose `SessionId`
  matches the exact target session are placed only in that
  `RimeKeySequenceResult`;
- callbacks received during a `Deploy-Rime` temporary lifecycle are placed
  only in that `RimeDeploymentResult`;
- startup notifications and all other active-lifecycle notifications go to the
  ordinary managed queue.

A `SessionId` of `0`, an unknown session ID, or a notification for another
registered session never enters the atomic result for the current session. It
follows the ordinary active-lifecycle route instead. This rule prevents a
notification for session A from being captured in session B's key result.

Long-running lifecycle cmdlets may also provide a non-durable managed live
mirror:

- `Start-Rime` forwards its notifications while retaining them in the ordinary
  queue;
- `Deploy-Rime` forwards its notifications while retaining them in its final
  deployment result.

Forwarding is always decoupled from the native callback. The callback copies
the native strings and enqueues the immutable `RimeNotification`, then returns.
A separate managed dispatcher invokes downstream application handling outside
the native callback stack. The native operation never waits for the dispatcher,
so a slow handler or a handler that calls another module cmdlet cannot block the
native operation from completing and releasing the process-wide gate.

The module does not write live notifications to the PowerShell Information
stream. A downstream application may choose to marshal a forwarded
notification to its UI thread, log it, write it to an Information stream, or
place it in another application-owned queue.

The public push surface is the process-wide singleton returned by:

```powershell
Get-RimeNotificationSource
```

The cmdlet accepts no parameters and emits one `RimeNotificationSource`.
That type exposes the `NotificationReceived` .NET event. Its
`RimeNotificationEventArgs` contains:

```text
Origin      : RimeNotificationOrigin  # Startup | Deployment
Notification: RimeNotification
```

Both properties are read-only. `Origin` is assigned by the managed bridge and
distinguishes maintenance performed by `Start-Rime` from the temporary
lifecycle of an explicit `Deploy-Rime`, even when librime emits the same
`deploy` message type and value. It is not derived from and does not expose the
native `context_object`.

A PowerShell application can subscribe without polling:

```powershell
$source = Get-RimeNotificationSource
$subscription = Register-ObjectEvent `
    -InputObject $source `
    -EventName NotificationReceived `
    -Action {
        $notification = $EventArgs.Notification
        # Application-defined UI, logging, or forwarding.
    }
```

The event action is application code and returns no value to the module. If
`-Action` is omitted, the application may instead consume the PowerShell event
with `Get-Event` or `Wait-Event`. None of these modes expose or poll the native
`context_object`.

The managed event source supports multiple subscribers. Each active subscriber
is offered the same forwarded notification. This fan-out is entirely managed;
RimeSharp.PowerShell remains the sole owner of librime's one native
notification handler.

Each subscription has an independent serial managed delivery lane. Delivery
order within one subscription matches the order in which the bridge accepted
the notifications. A slow subscriber delays only its own lane and does not
delay other subscribers, the bridge fan-out, or a native operation. Subscriber
counts are expected to be small, but the module does not impose an arbitrary
fixed subscriber limit.

Each delivery lane uses an unbounded FIFO. Enqueueing never waits for
application code, and the module does not silently drop a live notification.
A subscriber that remains blocked can therefore accumulate queued
notifications until the application removes that subscription. This is an
application-owned subscription-lifetime responsibility.

The event source and its application-owned subscriptions are independent of a
RIME lifecycle. `Stop-Rime` does not remove subscribers, and a later
`Start-Rime` continues forwarding to the same active subscriptions.
Applications explicitly remove PowerShell event subscriptions with
`Unregister-Event` when they no longer need them. Outside an active or faulted
regular lifecycle and outside a temporary deployer lifecycle, no native
notifications are expected. A `Faulted` lifecycle may still receive native
callbacks from incompletely finalized state, so its retained bridge continues
to copy and safely route those callbacks until `Stop-Rime` completes cleanup.
They never enter a completed session operation result.

Removing a subscriber is non-blocking. The source stops enqueueing new
notifications for that subscription and discards items in its private lane that
have not begun delivery. It does not wait for application code; at most one
handler invocation that already started may finish after removal. Events that
the `Register-ObjectEvent` adapter has already placed in PowerShell's own event
queue are outside the module's control and may be removed by the application
with PowerShell event commands such as `Remove-Event`.

The dispatcher invokes subscribers individually. An exception from one
subscriber is caught and does not prevent delivery to the remaining
subscribers, terminate the dispatcher, affect the native operation, or change
the cmdlet result. A throwing subscriber remains subscribed; the module does
not silently change application state by removing it. Subscriber failures are
not converted into `RimeNotification` objects.

Subscriber failures are reported through the
`RimeSharp.PowerShell.NotificationDispatch` `TraceSource` at error level. A
trace record identifies the subscriber method and includes the exception,
notification `SessionId`, and notification `MessageType`. It does not introduce
another module event, PowerShell stream write, or receive cmdlet. Errors thrown
later by a PowerShell `Register-ObjectEvent -Action` script are owned and
reported by that PowerShell event job rather than by this dispatcher.

RimeSharp.PowerShell does not create or manage application runspaces. If a
downstream application synchronously runs `Start-Rime` or `Deploy-Rime` in a
runspace, an event action owned by that same runspace cannot execute until the
pipeline becomes available. An application that requires live presentation
runs the long operation in a worker runspace and keeps its UI or event runspace
available. Module state, the native-operation gate, and the managed
notification bridge are shared across runspaces in the same process.

Live forwarding does not add notifications to a cmdlet's success pipeline.
`Start-Rime` emits no success object, and `Deploy-Rime` still emits only its
final result.

Before a native callback returns, its immutable notification has been routed
to the durable destination and enqueued for every subscription that was active
for that callback. Consequently, when `Start-Rime` or `Deploy-Rime` returns,
all notifications produced before completion have finished module-internal
routing. The cmdlet does not wait for subscriber handlers or PowerShell event
actions to execute. An application may therefore observe the cmdlet result
before it processes a queued completion notification. `RimeDeploymentResult`
or the terminating error remains the authoritative operation outcome; live
events are progress presentation.

The event source forwards only notifications actually emitted by librime. It
does not synthesize module-level command-started, command-completed, or
command-failed notifications. An application knows when it dispatches a long
command and uses that fact for its initial UI state; it uses the cmdlet result
or terminating error for the final state. A failure before librime enters the
selected deployment operation therefore does not produce a fabricated native
`deploy/failure` notification.

Version 0.2 does not attach a managed operation ID to live notifications.
Because subscriber delivery is asynchronous, an event from one deployment may
be processed after a later deployment has started. The final
`RimeDeploymentResult` or terminating error remains authoritative, and current
downstream requirements do not depend on exact live-event correlation. If live
progress becomes a generally reliable correlated API, a future version will
add the same managed operation ID to `RimeNotificationEventArgs` and the
corresponding operation result. That addition is explicitly deferred and does
not block version 0.2.

No notification is copied to two durable destinations. An application that
observes a live startup mirror and later drains the ordinary queue will see the
same startup notification again by design. It should choose live observation
or deferred durable consumption for that operation.

Short session operations are available in an atomic key result or in the
ordinary queue immediately after the short command returns. They are not
automatically forwarded through the long-operation live mirror. If a future
native operation is genuinely long-running, it may adopt the same decoupled
managed forwarding model.

A callback that arrives after a key or sequence native call has returned
belongs to the ordinary queue rather than the completed key result.

### `Receive-RimeNotification`

```powershell
Receive-RimeNotification
```

The cmdlet atomically takes and emits all notifications pending at the start of
the receive operation. It is non-blocking and has no wait, timeout, filter, or
subscription parameters. An empty queue emits no objects and is not an error.

Pending notifications remain readable after `Stop-Rime`. Starting a new
lifecycle clears unread notifications from the preceding lifecycle.
Deployment-result notifications never enter this queue.

## Input and Results

### `Get-RimeInput`

```powershell
Get-RimeInput -Session <RimeSession>
```

Returns librime's raw input as a non-null string. No current input is represented
by `''`. An invalid session is a terminating error. The cmdlet requires an
active lifecycle but is otherwise a read-only session operation.

### `Send-RimeKeyEvent`

```powershell
Send-RimeKeyEvent -KeyCode <int> [-Mask <int>] -Session <RimeSession>
```

Returns one fully managed `RimeKeyEventResult`:

```text
Handled      : bool
Commit       : string?
Status       : RimeStatusSnapshot
Context      : RimeContextSnapshot
Input        : string
Notifications: RimeNotification[]
```

The atomic operation:

1. acquires the gate and validates the session;
2. enables temporary notification capture for the exact managed session and
   its native ID;
3. calls `ProcessKey` exactly once;
4. ends the temporary capture when `ProcessKey` returns;
5. while still holding the gate, reads commit, status, context, and raw input;
6. emits a result only after all required snapshots have been created.

Operations for different sessions use the same process-wide gate. They may be
issued concurrently by different runspaces, but native calls and their complete
snapshot sequences are serialized. A result for one session therefore cannot
consume commit text or snapshot state from another session.

`Handled = false` is a normal result, not an error. No unread commit is
represented by `Commit = null`; empty raw input is `Input = ""`.
`Status`, `Context`, and `Notifications` are non-null. A required snapshot
failure is terminating and does not emit a partial result.

Native `get_commit` consumes unread commit text. The managed result retains its
copy and can be read repeatedly.

### `Send-RimeKey`

```powershell
Send-RimeKey -Sequence <string> -Session <RimeSession>
```

Returns one fully managed `RimeKeySequenceResult`:

```text
Commit       : string?
Status       : RimeStatusSnapshot
Context      : RimeContextSnapshot
Input        : string
Notifications: RimeNotification[]
```

This cmdlet is a simulation and testing helper. librime parses and processes
the whole sequence internally, ignores each individual key's handled state, and
only reports whether the session and sequence were valid. A false native result
is a terminating `RimeKeySequenceFailed` error.

The result represents final state, current unread commit, and notifications
received across the whole simulation call. It does not expose per-key handled
states or guarantee lossless capture of multiple intermediate commits.
Applications processing real input must use `Send-RimeKeyEvent` once per event.
The PowerShell layer does not reimplement librime's key-sequence parser.

The old ambiguous `RimeResponse` type is removed rather than retained as an
alias.

### `Receive-RimeCommit`

```powershell
Receive-RimeCommit -Session <RimeSession>
```

This replaces `Get-RimeCommit` so the name describes the native consume
semantics. It emits the current unread commit when one exists. No unread commit
produces no pipeline output and is not an error. There is no `-Peek` because
librime has no non-consuming commit API.

`Send-RimeKeyEvent` and `Send-RimeKey` consume the commit they include in their
results, so a following `Receive-RimeCommit` does not return the same text.

All other session-scoped cmdlets also require an explicit session and use the
process-wide gate. Existing managed snapshot outputs remain managed and contain
no native handles.

## Deployment

`Deploy-Rime` is a non-coordinating primitive. A real deployment requires the
process lifecycle to be `Inactive`; it refuses to run in `Starting`, `Active`,
`Stopping`, or `Faulted`. A faulted lifecycle must first be recovered with
`Stop-Rime`. The cmdlet does not inspect, stop, coordinate with, or make safety
claims about other processes.
An application that needs Weasel-like cross-process quiescence must implement
its own IPC, mutex, service, or equivalent coordination before invoking it.
Different applications should use distinct writable `UserDataDir` and
`StagingDir` locations unless they provide that coordination. A shared
read-only `SharedDataDir` does not by itself create the same write-coordination
requirement. An optional future directory-scoped cross-process lock would be a
defensive enhancement, not a version 0.2 prerequisite.

There are two explicit and mutually exclusive parameter sets:

```powershell
Deploy-Rime @traits -Workspace [-WhatIf] [-Confirm]

Deploy-Rime @traits `
    -ConfigFile <relative-path> `
    -VersionKey <config-path> `
    [-WhatIf] [-Confirm]
```

`-Workspace` is mandatory for a workspace deployment. Omitting the operation
never defaults to the larger workspace operation.

Each invocation performs exactly one native operation:

- workspace set: `Deploy()`;
- config-file set: `DeployConfigFile(fileName, versionKey)`.

There is no automatic chaining.

`ConfigFile` and `VersionKey` are required non-empty strings. `ConfigFile` must
be relative to the traits data directories. Absolute paths and `.` or `..`
segments are rejected; ordinary subdirectories are allowed. The normalized
relative path is passed to librime without adding or guessing an extension.
`VersionKey` uses the config-path slash normalization rules and is passed
without guessing a default such as `config_version`.

The cmdlet uses `SupportsShouldProcess` with `ConfirmImpact = Medium`.
`-WhatIf` performs parameter validation and reports the selected operation, but
does not require the current lifecycle to be stopped, does not initialize
librime, and does not emit a fake result.

### Temporary Deployer Lifecycle

While holding the process-wide gate, a real deployment performs:

1. `Setup`;
2. registration of a temporary notification handler;
3. `DeployerInitialize`;
4. exactly one selected deployment operation;
5. `Finalize` in cleanup;
6. handler and gate cleanup.

During the native lifecycle, deployment notifications are copied and enqueued
by the module-owned native handler. A separate managed dispatcher may forward
them to downstream application handling without making the native deployment
wait for that handling. The public cmdlet remains synchronous and its success
stream remains reserved for the final `RimeDeploymentResult`.

The handler remains active through `Finalize`. Cleanup is attempted whether
setup or deployment succeeds or fails. This unloads deployer modules and clears
the registry before a later deployment or `Start-Rime`.

### `RimeDeploymentResult`

The result has these stable properties:

```text
Operation                : RimeDeploymentOperation  # Workspace | ConfigFile
Succeeded                : bool
NativeOperationSucceeded : bool?
CleanupSucceeded         : bool
AppName                  : string
SharedDataDir             : string
UserDataDir               : string
DistributionName         : string?
DistributionCodeName     : string?
DistributionVersion      : string?
MinLogLevel              : int
LogDir                   : string?
PrebuiltDataDir           : string?
StagingDir                : string?
ConfigFile               : string?
VersionKey               : string?
StartedAtUtc             : DateTimeOffset
Duration                 : TimeSpan
Notifications            : RimeNotification[]
```

`NativeOperationSucceeded` is null when failure happened before the selected
native operation was entered. `Succeeded` is true only when the native
operation returned true and cleanup succeeded.

Only successful results are normal pipeline output. A false native result,
exception, cancellation, or cleanup failure produces a terminating error. Its
`TargetObject` is the diagnostic `RimeDeploymentResult`, including notifications
captured before failure. A native success followed by cleanup failure is
therefore distinguishable from a native operation failure.

## Config Materialization

### `Get-RimeConfig`

```powershell
Get-RimeConfig -ConfigId <string> `
    [-Path <config-path>] `
    -Shape <shape>

Get-RimeConfig -SchemaId <string> `
    [-Path <config-path>] `
    -Shape <shape>
```

The parameter sets are mutually exclusive:

- `ConfigId` calls `ConfigOpen`;
- `SchemaId` calls `SchemaOpen`.

`UserConfigOpen` is not exposed in version 0.2.

The cmdlet requires an active `Start-Rime` lifecycle so the required config
components and modules are loaded. It is not session-scoped and does not accept
`-Session`.

`ConfigId` and `SchemaId` are non-empty identifiers passed to librime without
adding or removing `.yaml`. librime's resource resolver owns identifier
interpretation.

`Path` selects the root at which the shape is applied. It defaults to `""`,
meaning config root. Leading and trailing slashes are normalized; the normalized
UTF-8 string is passed to native getters. Wildcards, provider syntax, `.`, and
`..` are not interpreted. A shape may be a tiny projection; for example, a
string shape at `schema/icon` reads only that leaf.

### Shape DSL

The downstream application owns the configuration design, so its explicit
shape is authoritative:

```powershell
[string]   # config_get_string
[bool]     # config_get_bool
[int]      # config_get_int, Int32
[double]   # config_get_double

@{
    name = [string]
    enabled = [bool]
}

[RimeConfigShape]::List([string])

[RimeConfigShape]::MapOf(
    [RimeConfigShape]::List([int])
)
```

Rules:

- scalar markers invoke only their corresponding librime getter;
- the module does not read a string and apply PowerShell conversion;
- no other scalar markers, enums, or custom converters are supported;
- a normal hashtable or ordered dictionary declares a fixed-key map;
- `[RimeConfigShape]::List(elementShape)` declares a homogeneous list;
- `[RimeConfigShape]::MapOf(valueShape)` declares runtime keys with homogeneous
  values;
- `RimeConfigShape` exposes immutable static factories;
- there is no `New-RimeConfigShape` cmdlet.

Shape normalization and validation complete before a native config is opened.
Null, unsupported, and recursive shapes are terminating errors.

There is intentionally no `Optional(...)`. Every declared member is required.
The long-term contract assumes the downstream application provides complete
defaults and treats `.custom.yaml` changes that violate its schema as user or
application errors.

If a declared scalar getter returns false, materialization fails. Current
RimeSharp list/map iteration cannot distinguish an empty container from a
missing or wrong-type container; an iteration with no entries is interpreted
according to the authoritative shape as an empty list or map. This limitation
is accepted by the contract.

Output mapping:

- scalar marker: the corresponding native CLR scalar;
- fixed-key map: one `PSCustomObject`;
- `List(...)`: one non-enumerated `object[]`;
- `MapOf(...)`: one `Dictionary<string, object>` using
  `StringComparer.Ordinal`.

Fixed maps reject declared keys that differ only by case because PowerShell
property access cannot represent them reliably. Case-sensitive dynamic keys
belong in `MapOf(...)`.

`Get-RimeConfig` always emits exactly one root snapshot. It never pipeline-
enumerates a root list, so an empty list is `object[0]`, not no output or null.
The snapshot is caller-owned and may be mutated without affecting librime.

### Atomic Failure

Materialization is all-or-nothing. It accumulates internal read status so that
all opened list/map iterators can be ended, closes the native config in a
`finally` path, and only emits output after the complete shape succeeds.
No partial map or list is written.

A materialization error contains:

- `ConfigId` or `SchemaId`;
- the effective normalized path;
- the expected shape kind;
- an inner exception when present.

## Error Contract

Expected contract and native failures are terminating `ErrorRecord` instances.
Downstream applications should branch on stable `FullyQualifiedErrorId` values,
not on message text or a new exception hierarchy.

Stable IDs include:

```text
RimeLifecycleAlreadyActive
RimeLifecycleInactive
RimeLifecycleStartFailed
RimeLifecycleStopFailed
RimeLifecycleFaulted
RimeSessionMissing
RimeSessionInvalid
RimeSessionCreationFailed
RimeSessionRemovalFailed
RimeDeploymentWhileActive
RimeDeploymentFailed
RimeConfigShapeInvalid
RimeConfigMaterializationFailed
RimeKeySequenceFailed
RimeSnapshotFailed
```

Standard .NET exception types and appropriate `ErrorCategory` values are used.
Inner exceptions are preserved. `TargetObject` contains the relevant session,
path, shape, or deployment diagnostic object. No public `RimeException`
hierarchy is introduced.

Lifecycle transitions use a read-only `RimeLifecycleFailureInfo` as the
`TargetObject` when cleanup has more than one relevant outcome:

```text
Operation       : RimeLifecycleOperation  # Start | Stop | NewSessionRollback
FinalState      : RimeLifecycleState      # Inactive | Faulted
PrimaryFailure  : Exception
CleanupFailures : IReadOnlyList<Exception>
```

`ErrorRecord.Exception` and `PrimaryFailure` refer to the same initiating or
first cleanup failure. `CleanupFailures` contains later rollback or cleanup
failures in observation order and is never used to replace the primary
exception. Startup contract or native failures use
`RimeLifecycleStartFailed`; stop cleanup failures use
`RimeLifecycleStopFailed`; operations rejected because retained cleanup is
required use `RimeLifecycleFaulted`. Cancellation remains cancellation as the
primary outcome even if its rollback adds lifecycle diagnostic information.

## Breaking Changes from 0.1

Version 0.2 intentionally does not preserve compatibility:

- remove `Start-Rime -SetDefaultSession`;
- change `Start-Rime` so that it establishes the lifecycle but neither creates
  nor returns a session;
- remove PowerShell `-Modules` support;
- make every session parameter explicit;
- add `New-RimeSession` and `Remove-RimeSession` for zero-to-many explicit
  sessions in one lifecycle;
- make `Stop-Rime` parameterless and lifecycle-scoped;
- remove `Register-RimeNotification`;
- add `Get-RimeNotificationSource` and the managed `NotificationReceived`
  event;
- remove `RimeNotification.Session` and `.ContextObject`;
- change `Send-RimeKeyEvent` from `bool` to `RimeKeyEventResult`;
- replace `RimeResponse` with `RimeKeySequenceResult`;
- replace `Get-RimeCommit` with `Receive-RimeCommit`;
- add `Get-RimeInput`;
- add `Deploy-Rime`;
- add `Get-RimeConfig` and `RimeConfigShape`.

The module version becomes `0.2.0`.

## Platform and Distribution

The source remains multi-targeted to `net8.0` and `net472`. PowerShell 7.4 on
.NET 8 is the primary implementation target. Windows PowerShell 5.1 remains a
long-term target but is not in the first production release because affected
native UTF-8 structure fields are not yet marshalled correctly on .NET
Framework.

The first formal release target is:

```text
Windows x64
PowerShell 7.4+
PSEdition Core
```

The initial Gallery artifact will exclude `net472` and will declare only the
Core edition. The source keeps the `net472` build so that future UTF-8 and
integration work can add Desktop edition without designing a different API.

### Native Runtime Packaging

The current change does not commit a native binary. Development continues to
use an explicitly supplied librime directory such as `LIBRIME_LIB_DIR`.

A later release task will:

- select and pin a librime version;
- build a reproducible `win-x64` native payload;
- include `rime.dll` and its complete runtime dependency closure;
- include all required third-party licenses and notices;
- load the module-private runtime by absolute path without PATH fallback or an
  external override in the formal package;
- validate the exact payload with native integration tests.

Linux native runtime acquisition and loading policy is deferred. Version 0.2
does not commit to either a module-private pinned payload or a system-provided
librime on Linux. A future Linux release must define its supported architecture
and libc baseline, library resolution rules, packaging ownership, and CI
coverage. Downstream applications do not implement dynamic-library discovery
under either future policy.

## Native Binding Prerequisite

Config iteration requires the already identified ABI correction that changes
the managed `ConfigEnd` return type from `bool` to `void`, matching librime.
That correction exists as commit `a15756f` on the separate `config-api` branch
and must be integrated into this branch before config materialization is
implemented.

No other RimeSharp or RimeSharp.Test public API defects are part of this
PowerShell change. In particular, custom module marshalling remains separate
upstream work.

Explicit multi-session management needs no new librime binding:
`RimeSharp.Rime` already exposes `CreateSession`, `FindSession`, and
`DestroySession`. `Stop-Rime` destroys the module registry entries individually
before finalization, so version 0.2 does not require a new managed
`CleanupAllSessions` binding.

## Planned Implementation Order

Implementation starts only after explicit approval of this document.

1. Integrate the `ConfigEnd` ABI prerequisite.
2. Add process ownership, gate, lifecycle state machine and generation, atomic
   startup rollback, fault recovery, lifecycle diagnostics, traits
   normalization, automatic notification routing, decoupled managed
   notification forwarding, the multi-session registry, and the new
   `Start-Rime`, `New-RimeSession`, `Remove-RimeSession`, and `Stop-Rime`
   contracts.
3. Add `Get-RimeInput`, the two key result types, atomic key operations, and
   `Receive-RimeCommit`.
4. Add the two `Deploy-Rime` parameter sets, temporary deployer lifecycle,
   diagnostics, and `ShouldProcess`.
5. Add `RimeConfigShape`, config path normalization, and atomic
   `Get-RimeConfig` materialization.
6. Apply explicit-session validation and the native gate to the remaining
   existing cmdlets.
7. Update exports, help, examples, manifest version, and release notes.
8. Add unit tests for pure managed behavior and native integration scenarios.

Required integration scenarios include:

- default and full maintenance startup with zero sessions;
- failure and cancellation at safe startup boundaries beginning with the first
  native setup invocation, including retention of the handler through rollback
  finalization;
- successful startup rollback leaving `Inactive` and allowing a later
  `Start-Rime` to succeed;
- failed startup rollback leaving `Faulted`, rejecting ordinary operations,
  and allowing a later `Stop-Rime` cleanup to restore `Inactive`;
- preservation of startup failure as primary when startup rollback also fails;
- creation of two or more sessions in one lifecycle;
- cancellation after successful native session creation without output or an
  orphaned session;
- failed new-session cancellation cleanup leaving recoverable `Faulted` state;
- isolation of composition, schema, options, raw input, and commit state across
  sessions;
- alternating key events across sessions without result or notification
  cross-routing;
- removal of one session without affecting another, including uncommitted
  composition in the removed session;
- idempotent repeated removal of the same genuine destroyed wrapper;
- rejection of forged, deserialized, wrong-generation, and registry-identity
  mismatched session objects;
- removal followed by creation in the same lifecycle without making the old
  wrapper valid if the native ID is reused;
- cleanup and invalidation of every remaining session by `Stop-Rime`;
- stop cleanup continuing after individual session failures, retaining bridge
  state when finalization fails, and succeeding on a later fault-recovery
  retry;
- stale-session rejection across lifecycle generations;
- runspace serialization and cancellation at safe boundaries;
- concurrent multi-session calls serialized by the native gate while each key
  snapshot remains atomic;
- handled and unhandled key-event results;
- commit consumption and empty raw input;
- single durable notification routing and decoupled managed live forwarding;
- multiple-subscriber fan-out, per-subscriber ordering, slow-subscriber and
  exception isolation, non-blocking unsubscribe, cross-lifecycle subscription
  retention, and subscriber-error tracing;
- completion without waiting for subscriber handlers or PowerShell event
  actions;
- workspace and single-config deployment success and failure;
- deployment rejection while the process lifecycle is `Faulted`;
- deployment cleanup followed by successful startup;
- scalar, fixed-map, list, `MapOf`, tiny projection, empty-container, invalid
  shape, and missing-required-scalar config reads;
- stable lifecycle and operation error IDs, ordered secondary cleanup
  diagnostics, and `ShouldProcess` behavior.
