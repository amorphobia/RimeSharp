# RimeSharp.PowerShell — 模块设计计划

## Implementation Status

All six planned stages are complete. The module exports all 15 planned cmdlets.

## 目标与受众

**目标**：为 PowerShell 脚本提供 RIME 输入法引擎的 cmdlet 接口。前端开发者使用 PowerShell 脚本开发输入法前端时，通过本模块与 RIME 引擎交互。

**不是**面向终端用户的 CLI 工具。

## 设计原则

- **全 cmdlet 面**：每个操作是独立 cmdlet，提供完整的 parameter binding、error stream、`Get-Help`、tab completion
- **Pipeline 承载 session**：`Start-Rime` 输出 `[RimeSession]` 对象，所有操作 cmdlet 通过 `-Session` 参数接收
- **单次调用返回完整渲染所需数据**：输入法前端在每个按键后需要 commit text + status + context 来渲染 UI，`Send-RimeKey` 一次性返回全部
- **错误走 ErrorRecord**：不与 console 耦合，让调用方通过 `-ErrorAction` 决定如何处理
- **Verb 类名为复数**：PowerShell SDK 的动词常量类名均以 `s` 结尾，如 `VerbsCommunications`（而非 `VerbsCommunication`）、`VerbsCommon`、`VerbsLifecycle`。声明 `[Cmdlet]` 属性时注意类名不可省略末尾的 `s`。


## 对象模型

```
Start-Rime → [RimeSession]
                │  (pipeline binding: ValueFromPipeline)
                │
                ├── Send-RimeKey -Sequence "nihao" → [RimeResponse]
                │     ├── Commit : string?
                │     ├── Status  : RimeStatus (schema, ascii/trad/simp flags, composing...)
                │     └── Context : RimeContext (preedit, candidates, menu page info)
                │
                ├── Send-RimeKeyEvent -KeyCode <int> [-Mask <int>] → bool
                ├── Get-RimeCommit → [RimeCommit]
                ├── Get-RimeContext → [RimeContext]
                ├── Get-RimeStatus  → [RimeStatus]
                │
                ├── Select-RimeCandidate -Index <n> [-OnCurrentPage]
                ├── Remove-RimeCandidate -Index <n> [-OnCurrentPage]
                ├── Invoke-RimeHighlight -Index <n> [-OnCurrentPage]
                ├── Set-RimePage -Direction (Next | Previous)
                │
                ├── Get-RimeSchema [-Current] → schema list / current schema
                ├── Set-RimeSchema -SchemaId <id>
                │
                ├── Get-RimeOption -Name <option> → bool
                ├── Set-RimeOption -Name <option> -Value <bool>
                │
                └── Stop-Rime  (destroys session + finalizes engine)
```

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
    public string? Commit { get; init; }
    public RimeStatus Status { get; init; }
    public RimeContext Context { get; init; }
}
```

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

#### `Stop-Rime`

```
Stop-Rime [[-Session] <RimeSession>] [<CommonParameters>]
```

销毁 session；如果这是最后一个 session，调用 `Finalize` 清理引擎。接受 pipeline input。

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
│   └── RimeSchemaInfo.cs       # Managed schema metadata
├── Cmdlets/
│   ├── LifecycleCmdlets.cs     # Start-Rime, Stop-Rime
│   ├── KeyCmdlets.cs           # Send-RimeKey, Send-RimeKeyEvent
│   ├── QueryCmdlets.cs         # Get-RimeCommit, Get-RimeContext, Get-RimeStatus
│   ├── CandidateCmdlets.cs     # Select-, Remove-RimeCandidate, Invoke-RimeHighlight
│   ├── PageCmdlets.cs          # Set-RimePage
│   ├── SchemaCmdlets.cs        # Get-RimeSchema, Set-RimeSchema
│   ├── OptionCmdlets.cs        # Get-RimeOption, Set-RimeOption
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

## 错误处理约定

- 未初始化调用 cmdlet → `InvalidOperationException` 包装为 `ErrorRecord`，`ErrorCategory.InvalidOperation`
- session 无效 → `ArgumentException` 包装为 `ErrorRecord`，`ErrorCategory.InvalidArgument`
- 全部使用 `ThrowTerminatingError`（不吞错误，让调用方通过 try/catch 或 `-ErrorAction Stop` 处理）

## 典型脚本示例

```powershell
Import-Module RimeSharp.PowerShell

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

# Notification registration is planned for a later release.

# 使用
Invoke-KeyPress "nihao"
Select-RimeCandidate 2
Set-RimeOption ascii_mode $true

# 关闭
Stop-Rime
```

## Deferred Work

- `Get-RimeCandidate` — expose the complete candidate list across pages for diagnostics and API-console parity.
- `Register-RimeNotification` — requires managed delegate lifetime, native callback queueing, and PowerShell runspace-safe event delivery.
- Switcher schema queries — expose available and selected schema lists through `RimeLevers` and `RimeSwitcherSettings`.
- Direct `RimeConfig` operations — configuration editing remains outside the v0.1 cmdlet surface.
