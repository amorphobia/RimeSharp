# AGENTS.md

This file provides guidance to Codex (Codex.ai/code) when working with code in this repository.

## Agent Working Rules

- Communicate with the user and explain project content in Chinese, including when discussing English-language project material. Keep all repository content in English: code, comments, documentation, and any other text written to files must not contain Chinese.
- Limit Agent operations to reading and writing files and running read-only Git queries. Do not build, test, restore, publish, install dependencies, or run the project. Do not make Git state changes, including staging, committing, pushing, pulling, creating branches, or switching branches. When any prohibited operation is needed, provide the exact command for the user to run and wait for the user to return its output before continuing.
- Except when adding new librime bindings, do not introduce changes under `RimeSharp/` or `RimeSharp.Test/`; the upstream maintainer does not want unrelated modifications in those directories. This restriction does not apply on the `test` branch, where changes outside `RimeSharp.PowerShell/` are allowed.

## User-Run Build & Test Commands

The Agent must not execute the commands in this section. Provide the appropriate command to the user when one is needed, then wait for the user to return the result.

```bash
# Restore dependencies
dotnet restore

# Build the solution (multi-target: net8.0, net472)
dotnet build

# Build without restore
dotnet build --no-restore

# Run the test/demo console app
dotnet run --project RimeSharp.Test
```

CI runs `dotnet restore` + `dotnet build` on Ubuntu via GitHub Actions (`.github/workflows/dotnet.yml`). Tests are not wired into CI yet.

## Architecture

RimeSharp is a **C# P/Invoke binding** for the native [librime](https://github.com/rime/librime) (RIME Input Method Engine). It targets `net8.0` and `net472`.

### Interop layering (bottom-up)

1. **`Native.GetRimeAPI()`** — imports `rime_get_api` from the native `rime` DLL and returns a pointer to librime's function table.

2. **`RimeAPI` / `RimeLeversAPI` structs** — C# structs with `[StructLayout(LayoutKind.Sequential)]` that mirror librime's C `RimeApi` struct exactly. Each field is a function pointer marshaled with `[MarshalAs(UnmanagedType.FunctionPtr)]`. Unused slots are `IntPtr` placeholders — field ordering and alignment must match the C header precisely.

3. **`Rime` and `RimeLevers` singletons** — thread-safe (`Lazy<T>`) facades. `Rime` calls `Native.GetRimeAPI()` and marshals the result into `RimeAPI`. `RimeLevers` loads via `Rime.FindModule("levers")` and marshals its own function table. All librime functions are exposed as C# methods through these classes.

4. **Managed wrappers** — `RimeConfig`, `RimeCustomSettings`, `RimeSwitcherSettings` wrap native handles and implement `IDisposable` / `SafeHandle`. Return structs (`RimeCommit`, `RimeContext`, `RimeStatus`) self-free via `Dispose()`.

### Key patterns

- **Versioning**: structs with native counterparts (`RimeTraits`, `RimeCommit`, `RimeContext`, `RimeStatus`, `RimeModule`) include a `_dataSize` field set to `Marshal.SizeOf<T>() - sizeof(int)` in their constructor. This is librime's ABI versioning mechanism — never remove or reorder it.

- **String marshaling**: All strings crossing the native boundary use `[MarshalAs(UnmanagedType.LPUTF8Str)]`. The internal `UTF8Marshal` helper handles manual `IntPtr` ↔ UTF-8 string conversion for cases where the marshaler can't be used (e.g., reading strings from nested struct pointers).

- **Nullable returns**: Config getters (`ConfigGetBool`, `ConfigGetInt`, etc.) return `null` when the native call returns `false` (key not found), rather than throwing.

- **`ref` for native pointers**: Methods pass structs by `ref` when the C API takes a pointer-to-struct parameter.

### File map

| File | Purpose |
|---|---|
| `Rime.cs` | Main API singleton + all delegate types + `RimeAPI` struct + `Native` import |
| `RimeStructs.cs` | All data structs: `RimeTraits`, `RimeCommit`, `RimeContext`, `RimeStatus`, `RimeMenu`, `RimeCandidate`, etc. |
| `RimeConfig.cs` | `RimeConfig` struct with typed getters and list/map iteration |
| `RimeLevers.cs` | `RimeLeversAPI` struct + `RimeLevers` singleton for the "levers" module |
| `RimeCustomSettings.cs` | `RimeCustomSettings` (SafeHandle) and `RimeSwitcherSettings` wrappers |
| `UTF8Marshal.cs` | Internal UTF-8 string conversion utilities |
| `RimeSharp.Test/Program.cs` | Console test harness — also serves as API usage reference |

### The test project

The test project (`RimeSharp.Test`) is an interactive console app, not unit tests. It initializes RIME with `./shared` and `./user` directories, creates a session, and reads key sequences from stdin. Special commands (`print schema list`, `select schema X`, `set option X`, etc.) exercise different API surfaces. It requires a native `rime` library and RIME data files to run — the `shared`/`user` directories must be populated with RIME schema data (not included in the repo).
