# Minimal native test shim

This directory contains the initial Windows x64 failure-injection shim used by
RimeSharp.PowerShell 0.2. It implements only the deterministic scenarios listed
as automated in ../NativeScenarioMatrix.md.

The shim deliberately uses the installed librime rime_api.h header rather than
vendoring an ABI copy. Build-NativeShim.ps1 locates that header from
LIBRIME_LIB_DIR by default, or accepts an explicit IncludeDir.

From the repository root, build it with:

    pwsh -NoProfile -File RimeSharp.PowerShell/Tests/NativeShim/Build-NativeShim.ps1

Then run the integration harness in a fresh PowerShell process:

    pwsh -NoProfile -File RimeSharp.PowerShell/Tests/NativeShimIntegration.ps1

The shim injects only safe native delays and false return values. It does not
throw managed or native exceptions across librime ABI boundaries. Additional
failure points remain deferred as documented in NativeScenarioMatrix.md.
