# RimeSharp.PowerShell — 模块设计计划

## Implementation Status

All seven planned stages are complete. The module exports all 20 planned cmdlets
and covers the interactive API-console surface demonstrated by `RimeSharp.Test`.

PowerShell 7.4 on .NET 8 is the primary implementation target and has been
validated against a native librime deployment, including schema and status
queries, composition, candidate enumeration, commit retrieval, and
notifications.

The `net472` target is retained for future Windows PowerShell 5.1 compatibility.
It is not currently considered production-ready because native UTF-8 strings
returned in librime structures are corrupted by the .NET Framework interop
path. The `net8.0` implementation does not exhibit this problem. Supporting
Windows PowerShell 5.1 requires replacing affected automatic `LPUTF8Str`
structure-field marshaling with explicit native pointer conversion.

## 目标与受众

**目标**：为 PowerShell 脚本提供 RIME 输入法引擎的 cmdlet 接口。前端开发者使用 PowerShell 脚本开发输入法前端时，通过本模块与 RIME 引擎交互。

**不是**面向终端用户的 CLI 工具。

## 设计原则

- **全 cmdlet 面**：每个操作是独立 cmdlet，提供完整的 parameter binding、error stream、`Get-Help`、tab completion
- **Pipeline 承载 session**：`Start-Rime` 输出 `[RimeSession]` 对象，所有操作 cmdlet 通过 `-Session` 参数接收
- **单次调用返回完整渲染所需数据**：输入法前端在每个按键后需要 commit text + status + context 来渲染 UI，`Send-RimeKey` 一次性返回全部
- **错误走 ErrorRecord**：不与 console 耦合，让调用方通过 `-ErrorAction` 决定如何处理
- **Verb 类名为复数**：PowerShell SDK 的动词常量类名均以 `s` 结尾，如 `VerbsCommunications`（而非 `VerbsCommunication`）、`VerbsCommon`、`VerbsLifecycle`。声明 `[Cmdlet]` 属性时注意类名不可省略末尾的 `s`。
- **Managed pipeline snapshots**: cmdlets copy native results into managed objects and release native allocations before writing to the pipeline.
- **Runspace-safe notifications**: native callbacks only enqueue managed notification objects; `Receive-RimeNotification` writes them from the PowerShell thread.


## 对象模型

```
Start-Rime → [RimeSession]
                │  (pipeline binding: ValueFromPipeline)
                │
                ├── Send-RimeKey -Sequence "nihao" → [RimeResponse]
                │     ├── Commit : string?
                │     ├── Status  : RimeStatusSnapshot (schema and mode flags)
                │     └── Context : RimeContextSnapshot (preedit, candidates, menu page info)
                │
                ├── Send-RimeKeyEvent -KeyCode <int> [-Mask <int>] → bool
                ├── Get-RimeCommit → string
                ├── Get-RimeContext → [RimeContextSnapshot]
                ├── Get-RimeStatus  → [RimeStatusSnapshot]
                ├── Get-RimeCandidate [-Start <n>] [-Count <n>] → [RimeCandidateInfo]
                │
                ├── Select-RimeCandidate -Index <n> [-OnCurrentPage]
                ├── Remove-RimeCandidate -Index <n> [-OnCurrentPage]
                ├── Invoke-RimeHighlight -Index <n> [-OnCurrentPage]
                ├── Set-RimePage -Direction (Next | Previous)
                │
                ├── Get-RimeSchema → schema list with IsCurrent
                ├── Set-RimeSchema -SchemaId <id>
                ├── Get-RimeSwitcherSchema (-Available | -Selected)
                │
                ├── Get-RimeOption -Name <option> → bool
                ├── Set-RimeOption -Name <option> -Value <bool>
                ├── Get-RimeStateLabel -Name <option> -State <bool>
                ├── Receive-RimeNotification → [RimeNotification]
                │
                └── Stop-Rime  (destroys session + finalizes engine)
```

`Register-RimeNotification` is process-wide and should run before `Start-Rime`
when initialization and deployment messages are required.

### RimeSession

```csharp
public sealed class RimeSession
{
    internal UIntPtr Id { get; }
    // IDisposable? Or leave cleanup to Stop-Rime.
    // Internal only — not constructed by user code.
}
```

### RimeResponse

```csharp
public sealed class RimeResponse
{
    public string? Commit { get; }
    public RimeStatusSnapshot Status { get; }
    public RimeContextSnapshot Context { get; }
}
```

`RimeResponse` and its nested snapshots contain no native handles and do not require disposal.

## Cmdlet 清单

### 生命周期

#### `Start-Rime`

```
Start-Rime [-AppName <string>] [-SharedDataDir <string>] [-UserDataDir <string>]
           [-PassThru] [<CommonParameters>]
```

初始化 RIME 引擎，运行维护（如需要），创建 session。
输出 `[RimeSession]`。

`-PassThru`：将 session 写入 `$global:RimeDefaultSession`，后续 cmdlet 在未指定 `-Session` 时自动使用。方便长脚本不用到处传参。

A second `Start-Rime` before the active lifecycle is stopped produces the
`RimeAlreadyStarted` terminating error.

#### `Stop-Rime`

```
Stop-Rime [[-Session] <RimeSession>] [<CommonParameters>]
```

Destroys the session and finalizes the engine. The current module lifecycle
models one active `Start-Rime` / `Stop-Rime` pair; callers must not keep other
sessions active when stopping the engine. Accepts pipeline input.

### 输入

#### `Send-RimeKey`

```
Send-RimeKey [-Sequence] <string> [[-Session] <RimeSession>] [<CommonParameters>]
```

模拟按键序列（如 `"nihao"` 或 `"Down"`）。内部调用 `SimulateKeySequence`。
返回 `[RimeResponse]`（commit + status + context），前端据此渲染 UI。

`-Sequence` 支持 position 0，所以可以直接 `$session | Send-RimeKey nihao`。

#### `Send-RimeKeyEvent`

```
Send-RimeKeyEvent [-Session] <RimeSession> [-KeyCode] <int> [[-Mask] <int>] [<CommonParameters>]
```

发送单个原始按键事件。内部调用 `ProcessKey`（keyCode + 修饰键 mask）。
返回 `bool` 表示该键是否被引擎处理。
与 `Send-RimeKey` 不同，此 cmdlet 不读取 commit/status/context，适用于调用方自行管理渲染。

`-Mask` 为修饰键掩码（如 0 = 无修饰，可按位组合），默认 0。

### 查询状态

#### `Get-RimeCommit`

```
Get-RimeCommit [[-Session] <RimeSession>] [<CommonParameters>]
```

获取上次提交的文字。

#### `Get-RimeContext`

```
Get-RimeContext [[-Session] <RimeSession>] [<CommonParameters>]
```

获取当前输入上下文（组合态 preedit、候选列表 candidates、menu 分页信息）。

#### `Get-RimeStatus`

```
Get-RimeStatus [[-Session] <RimeSession>] [<CommonParameters>]
```

获取引擎状态（schema、ascii_mode、composing 等标志位）。

### 候选操作

#### `Get-RimeCandidate`

```
Get-RimeCandidate [[-Start] <int>] [[-Count] <int>] [[-Session] <RimeSession>]
                  [<CommonParameters>]
```

Enumerates the complete candidate list across pages. Each managed
`RimeCandidateInfo` contains its zero-based global `Index`, `Text`, and `Comment`.
`Start` defaults to `0`; `Count` defaults to all remaining candidates.

#### `Select-RimeCandidate`

```
Select-RimeCandidate [-Index] <int> [-OnCurrentPage] [[-Session] <RimeSession>]
                     [<CommonParameters>]
```

按序号选取候选词。默认序号是全局候选序号；`-OnCurrentPage` 表示当前页内序号。

#### `Remove-RimeCandidate`

```
Remove-RimeCandidate [-Index] <int> [-OnCurrentPage] [[-Session] <RimeSession>]
                     [<CommonParameters>]
```

删除候选词。

#### `Invoke-RimeHighlight`

```
Invoke-RimeHighlight [-Index] <int> [-OnCurrentPage] [[-Session] <RimeSession>]
                     [<CommonParameters>]
```

高亮指定候选词（部分 schema 支持此功能）。

#### `Set-RimePage`

```
Set-RimePage [-Direction] <PageDirection> [[-Session] <RimeSession>] [<CommonParameters>]
```

翻页。`-Direction` 枚举：`Next`、`Previous`。

### Schema 配置

#### `Get-RimeSchema`

```
Get-RimeSchema [[-Session] <RimeSession>] [<CommonParameters>]
```

列出所有可用 schema，标记当前使用的那一个。

#### `Set-RimeSchema`

```
Set-RimeSchema [-SchemaId] <string> [[-Session] <RimeSession>] [<CommonParameters>]
```

切换输入方案。

#### `Get-RimeSwitcherSchema`

```
Get-RimeSwitcherSchema -Available [<CommonParameters>]
Get-RimeSwitcherSchema -Selected [<CommonParameters>]
```

Loads switcher settings and returns either all available schemas or the schemas
selected in the switcher configuration.

### 选项配置

#### `Get-RimeOption`

```
Get-RimeOption [-Name] <string> [[-Session] <RimeSession>] [<CommonParameters>]
```

读取选项值。常用选项：`ascii_mode`、`ascii_punct`、`full_shape`、`simplification`、`soft_cursor` 等。

#### `Set-RimeOption`

```
Set-RimeOption [-Name] <string> [-Value] <bool> [[-Session] <RimeSession>]
               [<CommonParameters>]
```

设置开关选项。

#### `Get-RimeStateLabel`

```
Get-RimeStateLabel [-Name] <string> [-State] <bool> [-Abbreviated]
                   [[-Session] <RimeSession>] [<CommonParameters>]
```

Returns the display label associated with an option state. Option notifications
can bind `OptionName`, `OptionState`, and `Session` by property name.

### Notifications

#### `Register-RimeNotification`

```
Register-RimeNotification [<CommonParameters>]
```

Enables the process-wide native notification handler. Call it before `Start-Rime`
to capture initialization and deployment notifications. The native delegate is
strongly rooted for the engine lifetime. Starting a new engine clears unread
notifications left by the preceding engine generation.

#### `Receive-RimeNotification`

```
Receive-RimeNotification [<CommonParameters>]
```

Drains pending notifications from a thread-safe queue. Native callback threads
never execute PowerShell code or write directly to the pipeline.

## 文件组织

```
RimeSharp.PowerShell/
├── RimeSharp.PowerShell.csproj
├── RimeSharp.PowerShell.psd1
├── RimeSharp.PowerShell.psm1
├── build.ps1
├── PLAN.md
├── Types/
│   ├── RimeSession.cs          # 会话对象
│   ├── RimeResponse.cs         # Send-RimeKey 的返回对象
│   ├── RimeSchemaInfo.cs       # Managed schema metadata
│   ├── RimeSnapshots.cs        # Managed status and context snapshots
│   ├── RimeCandidateInfo.cs    # Complete-list candidate item
│   ├── RimeSwitcherSchemaInfo.cs
│   └── RimeNotification.cs     # Managed native notification
├── Cmdlets/
│   ├── LifecycleCmdlets.cs     # Start-Rime, Stop-Rime
│   ├── KeyCmdlets.cs           # Send-RimeKey, Send-RimeKeyEvent
│   ├── QueryCmdlets.cs         # Get-RimeCommit, Get-RimeContext, Get-RimeStatus
│   ├── CandidateCmdlets.cs     # Candidate query and manipulation
│   ├── PageCmdlets.cs          # Set-RimePage
│   ├── SchemaCmdlets.cs        # Get-RimeSchema, Set-RimeSchema
│   ├── SwitcherSchemaCmdlets.cs
│   ├── OptionCmdlets.cs        # Get-RimeOption, Set-RimeOption
│   ├── StateLabelCmdlets.cs
│   ├── NotificationCmdlets.cs
│   └── SessionValidation.cs    # Shared session validation
```

## 实现顺序

| 阶段 | Cmdlet | 原因 |
|---|---|---|
| 1 | `Start-Rime` + `Stop-Rime` + `RimeSession` + `RimeResponse` | 基础设施，没有它们其他 cmdlet 无法存在 |
| 2 | `Send-RimeKey` + `Send-RimeKeyEvent` + `Get-RimeContext` + `Get-RimeStatus` + `Get-RimeCommit` | 核心输入循环，前端最基本需求 |
| 3 | `Select-RimeCandidate` + `Set-RimePage` | 选词翻页闭环 |
| 4 | `Get-RimeSchema` + `Set-RimeSchema` | 方案切换 |
| 5 | `Get-RimeOption` + `Set-RimeOption` | 开关控制 |
| 6 | `Remove-RimeCandidate` + `Invoke-RimeHighlight` | 低频操作 |
| 7 | `Get-RimeCandidate` + `Get-RimeSwitcherSchema` + `Get-RimeStateLabel` + `Register-RimeNotification` + `Receive-RimeNotification` | `RimeSharp.Test` API-console parity |

## 错误处理约定

- 未初始化调用 cmdlet → `InvalidOperationException` 包装为 `ErrorRecord`，`ErrorCategory.InvalidOperation`
- session 无效 → `ArgumentException` 包装为 `ErrorRecord`，`ErrorCategory.InvalidArgument`
- 全部使用 `ThrowTerminatingError`（不吞错误，让调用方通过 try/catch 或 `-ErrorAction Stop` 处理）

## 典型脚本示例

```powershell
Import-Module RimeSharp.PowerShell

# Register before initialization so deployment messages are captured.
Register-RimeNotification

# 启动引擎
$session = Start-Rime -SharedDataDir ./shared -UserDataDir ./user -PassThru

# 输入循环
function Invoke-KeyPress {
    param([string]$Key)

    $response = Send-RimeKey -Sequence $Key

    # 处理提交文字
    if ($response.Commit) {
        Write-Host "COMMIT: $($response.Commit)"
    }

    # 渲染预编辑字符串
    if ($response.Context.Composition.Preedit) {
        Write-Host "PREEDIT: $($response.Context.Composition.Preedit)"
    }

    # 渲染候选窗
    $labels = $response.Context.SelectLabels
    $candidates = $response.Context.Menu.Candidates
    for ($i = 0; $i -lt $candidates.Count; $i++) {
        $mark = if ($i -eq $response.Context.Menu.HighlightedCandidateIndex) { ">" } else { " " }
        Write-Host "$mark$($labels[$i]). $($candidates[$i].Text) $($candidates[$i].Comment)"
    }
}

# 使用
Invoke-KeyPress "nihao"
Select-RimeCandidate 2
Set-RimeOption ascii_mode $true

# API-console queries
Get-RimeCandidate | Format-Table Index, Text, Comment
Get-RimeSwitcherSchema -Available
Get-RimeSwitcherSchema -Selected

# Native callbacks are consumed safely on the PowerShell thread.
foreach ($notification in Receive-RimeNotification) {
    Write-Host ("MESSAGE: [$($notification.SessionId)] " +
        "[$($notification.MessageType)] [$($notification.MessageValue)]")

    if ($notification.OptionName) {
        $label = Get-RimeStateLabel `
            -Name $notification.OptionName `
            -State $notification.OptionState `
            -Session $notification.Session
        Write-Host ("OPTION: $($notification.OptionName) = " +
            "$($notification.OptionState) // $label")
    }
}

# 关闭
Stop-Rime
```

## Deferred Work

- Switcher schema selection changes — `RimeSwitcherSettings.SelectSchemas()` is not used by the API console and remains outside the current cmdlet surface.
- Direct `RimeConfig` operations — configuration editing remains outside the v0.1 cmdlet surface.
