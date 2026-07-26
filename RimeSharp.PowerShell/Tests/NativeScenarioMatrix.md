# Native integration scenario matrix

The ordinary native runner is NativeIntegration.ps1. Deterministic lifecycle
failure and cancellation scenarios require a purpose-built librime-compatible
native shim because the production API does not expose failure injection.

## Ordinary librime scenarios

- Default and full maintenance startup with zero sessions.
- Two or more sessions in one lifecycle.
- Composition, schema, option, raw-input, and commit isolation.
- Alternating key events and exact-session notification capture.
- Removal of one session without affecting another.
- Idempotent repeated removal of a genuine destroyed wrapper.
- Cleanup and invalidation by Stop-Rime.
- Stale-session rejection after lifecycle restart.
- Handled result shape, commit consumption, and empty raw input.
- Startup notification queueing and managed event forwarding.
- Workspace deployment followed by successful startup.
- Deployment rejection while active and WhatIf behavior.
- Scalar and missing-required-scalar config reads.

Additional data-specific fixtures must cover fixed maps, lists, MapOf, tiny
projections, and authoritative empty containers.

## Automated native shim scenarios

NativeShimIntegration.ps1 uses the minimal librime-compatible DLL under
Tests/NativeShim. The initial version 0.2 shim records call order and maximum
native concurrency and provides safe delays and false return values for these
release-critical scenarios:

- Cancel Start-Rime while Initialize is in native code. Verify rollback calls
  Finalize, the callback remains usable through Finalize, the lifecycle returns
  to Inactive, and a later startup succeeds.
- Cancel New-RimeSession after CreateSession returns a nonzero ID, make the
  first rollback DestroySession return false, verify Faulted rejection, and
  recover through Stop-Rime.
- Run session operations concurrently from two runspaces and verify maximum
  native concurrency is one. Remove the module from one runspace and verify the
  process-wide RimeConfigShape accelerator remains available in the other.
- Make workspace deployment return false, verify the RimeDeploymentFailed
  diagnostic, retain the Finalize notification, complete cleanup, and permit a
  later startup.

## Deferred shim scenarios

The following scenarios remain specified for later expansion:

- Failure or cancellation at every startup safe boundary.
- Finalize failure during startup rollback, Stop-Rime, and deployment cleanup.
- Combined primary and cleanup failures with ordered secondary diagnostics.
- Native ID reuse and every forged or mismatched session form.
- Multiple individual DestroySession failures during Stop-Rime.
- Cancellation at every post-native safe boundary.
- Config-file deployment cancellation and cleanup failure.
- Every remaining stable error ID under injected native failure.

The upstream RimeConfig.GetList and RimeConfig.GetMap exception-safety issue is
an accepted version 0.2 risk documented in PLAN.md. Iterator failure injection
is deferred with its upstream try/finally fix and does not block this release.
