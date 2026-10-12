# Research: Runtime Value Evidence

## Decision: Reuse Existing Runtime Inspection Lanes

**Decision**: Extend Elsa foundation's existing `IRuntimePayloadCapturePolicy`, `RuntimePayloadCaptureMode`, `ActivityExecutionInspectionValueSnapshot`, and activity execution inspection API instead of creating a parallel evidence store.

**Rationale**: Codebase discovery found existing capture policy, value snapshot, projection store, and API view paths already wired into Studio's lazy activity inspection call. Reusing this lane keeps the evidence attached to activity execution inspection and avoids a second correlation model.

**Alternatives considered**: A new diagnostics endpoint and store was rejected because it would duplicate correlation, retention, and permission decisions already present in runtime inspection projections.

## Decision: Add `DiagnosticSnapshot` As A Capture Mode Between Metadata And Payload

**Decision**: Extend capture mode ordering from `None`, `MetadataOnly`, `Payload` to include `DiagnosticSnapshot` between metadata-only and full payload capture.

**Rationale**: The current default policy omits input/output snapshots, while `Payload` is too risky as a default. A distinct mode gives users useful diagnostic evidence without treating bounded snapshots as raw payloads.

**Alternatives considered**: Overloading `Payload` with sanitized data was rejected because it blurs security semantics and makes UI/permission behavior harder to reason about.

## Decision: Backend Owns Effective Policy Resolution

**Decision**: The backend computes effective runtime diagnostics by applying Host Policy caps to Studio-managed requested settings.

**Rationale**: Users can change Studio-managed settings through the UI, but deployments still need app-level maximums that cannot be bypassed by the client. Returning both requested and effective values lets Studio explain limitations without duplicating policy logic.

**Alternatives considered**: Client-side capping was rejected because it is not authoritative. Application configuration only was rejected because the user explicitly needs control through the UI.

## Decision: First Settings Scope Is Tenant/Module-Wide

**Decision**: The first slice exposes a module-wide Workflows runtime diagnostics setting, with per-workflow and per-executable overrides deferred.

**Rationale**: Module-wide settings satisfy the immediate user need and keep policy, UI, and tests tractable. The model reserves subject overrides, so narrower scopes can be added without changing the basic evidence model.

**Alternatives considered**: Per-workflow overrides first were rejected because they add precedence, lifecycle, and migration complexity before the default diagnostic behavior is proven.

## Decision: Snapshot Generation Must Be Bounded And Marker-Preserving

**Decision**: Snapshot generation must enforce depth, property count, array item count, string preview length, and total size limits, while producing redaction/truncation/unsupported/error markers.

**Rationale**: A safe diagnostic default needs predictable storage cost and safe failure modes. Preserving shape with markers gives users enough context to diagnose without leaking full values.

**Alternatives considered**: Serializing full JSON and truncating afterward was rejected because it can allocate or expose too much before limits are enforced.

## Decision: Runtime Payload Reference Shape Is Reserved Only

**Decision**: Document and render Runtime Payload Reference leaves, but do not emit them from production capture until provider-backed reference creation, resolution endpoints, permissions, and audit exist.

**Rationale**: References are the right direction for secrets and large artifacts, but emitting unresolved references before authorization and audit semantics exist would create misleading UI and incomplete security behavior.

**Alternatives considered**: Emitting ad hoc provider paths or storage keys was rejected because it leaks internals and would be hard to migrate safely.

## Decision: Studio Renders Inputs And Outputs With A Snapshot Tree

**Decision**: Replace the current Activity-input-only payload preview with input and output evidence sections backed by a generic Diagnostic Snapshot tree renderer.

**Rationale**: The existing Studio code filters only `ActivityInput` and only displays payloads when capture mode is `Payload`. Diagnostic Snapshot mode needs first-class rendering of object, array, redacted, truncated, permission-hidden, unsupported, error, and future unknown nodes.

**Alternatives considered**: Continuing with JSON string previews was rejected because it creates noise for nested data, hides markers, and cannot represent future reference leaves cleanly.
