# Use diagnostic snapshots and payload references for runtime value evidence

Runtime value evidence is captured as bounded Diagnostic Snapshots by default rather than raw payloads. A Diagnostic Snapshot may include Runtime Payload Reference leaf nodes for secrets, blobs, artifacts, or large values, but resolving those references requires separate authorization and audit. Studio-managed Workflow Runtime Diagnostics Settings choose what evidence is requested for future runs, while Host Policy caps the maximum allowed capture level and access policy controls reveal/download behavior at read time.

The first user-controlled setting is tenant/module-wide under Workflows runtime diagnostics. Studio stores the requested Runtime Value Evidence Level, while the backend computes the effective level by applying Host Policy caps. Settings changes apply only to future capture events. Per-workflow overrides, reference resolution, and retention policy are deferred follow-up work.

Deferred work to seed follow-up issues:

- Define Runtime Payload Reference resolution endpoints for reveal/download flows.
- Define permissions for viewing snapshots, revealing protected values, downloading referenced blobs, and revealing secret-backed references.
- Design audit records for every Runtime Payload Reference resolution.
- Design secret-provider integration for protected secret references.
- Design blob/artifact storage integration for large payload references.
- Decide retention and deletion behavior for Diagnostic Snapshots and referenced payload material.
- Decide whether per-workflow or per-executable diagnostics overrides are needed after the tenant/module-wide setting ships.
- Decide how Full Payload capture is exposed, if at all, beyond development and explicit allow-list scenarios.
