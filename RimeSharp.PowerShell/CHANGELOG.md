# Changelog

## 0.2.0

- Separated the process lifecycle from explicit zero-to-many session
  management.
- Added a cancellation-aware process-wide native-operation gate and lifecycle
  fault recovery.
- Added strict session generation, identity, registry, and destroyed-state
  validation.
- Added atomic key-event and key-sequence results with raw input and routed
  notifications.
- Replaced Get-RimeCommit with the consume-oriented Receive-RimeCommit.
- Replaced manual native notification registration with automatic routing,
  queueing, and an asynchronous managed event source.
- Added non-coordinating workspace and single-config deployment operations.
- Added explicit-shape configuration materialization.
- Added stable PowerShell error identifiers and lifecycle/deployment
  diagnostics.
- Removed the 0.1 default-session, custom-module, RimeResponse, and manual
  notification-registration contracts.
