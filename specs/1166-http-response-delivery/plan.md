# Implementation Plan: HTTP response delivery observations

**Branch**: `codex/http-response-delivery-warnings` | **Date**: 2026-10-30 | **Spec**: [spec.md](spec.md)
**Program goal**: none/free-flow; directly authorized UX/runtime improvement, issue #3450.

## Summary and technical context
.NET 10 Runtime Core gains immutable observation records on the existing activity inspection projection. In-memory and EF inspection adapters append observations idempotently and merge them into later checkpoint writes. Existing JSON projection storage owns retention/tenant scoping; no new persistence family, SQL table or continuation state is needed. Runtime API adds observations to exact and summary views. React/TypeScript render warning badges and delivery detail.

HTTP request payloads carry an optional opaque exchange ID. Transport records write/closure facts on exact endpoint/response inspection records. A drain observer reconciles late response completions against retained closed-origin evidence, conservatively requiring a unique HTTP endpoint invocation. Both close-time and drain-time reconciliation cover the completion/closure race. Observation failures are logged and never affect request/activity execution. A restart after a transport crash without a recorded fact remains Unknown.

## Constitution check
Framework Core/provider boundaries remain intact; no new heavy dependencies or projects. Runtime owns generic inspection observations; HTTP owns delivery semantics; Studio consumes server evidence. ADR 0045 request-affine isolation and committed typed response instruction remain unchanged. Observations never invoke incident strategies, retry completed activities or rewrite completion envelopes. Internal by default except cross-assembly contracts; track Runtime Core APIs. Draft/provisional Model X and activity-versioning material is not relied on.

## Changes and validation
Affected: Runtime Core, Runtime implementation and EF inspection store, Runtime API, Activities HTTP, Studio runtime inspection/canvas. Tests: pure/store observation behavior; SQLite append/reload/concurrent checkpoint preservation; HTTP real transport/wait/resume and legacy/ambiguous cases; Studio exact invocation warning/Unknown; owned browser fixture. Run affected whole suites, source mutation proof, production frontend budgets, architecture/maps checks and an isolated backend HTTP e2e before delivery. Inspect rendered light/dark geometry. Evidence records failures and unresolved limits honestly.

## Review gates
Check evidence attribution, idempotency, monotonic fact preservation, tenant isolation, observation failure isolation and that Written never means client acknowledgement. No manual merge or restoration of the merged UX branch.
