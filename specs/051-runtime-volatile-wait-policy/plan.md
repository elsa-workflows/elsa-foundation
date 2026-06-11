# Implementation Plan: Runtime Volatile Wait Policy

**Branch**: `codex/runtime-volatile-wait-policy` | **Date**: 2026-06-11 | **Spec**: [spec.md](./spec.md)

## Summary

Add a conservative default volatile wait policy. The policy allows in-memory volatile waits only when the host explicitly reports support, preserves requested guardrails in the decision, and denies unsupported hosts with a reason instead of silently degrading into durable suspension.

## Technical Context

- `IRuntimeVolatileWaitPolicy`, `RuntimeVolatileWaitPolicyRequest`, and `RuntimeVolatileWaitPolicyDecision` already exist.
- Runtime API composition does not currently register a volatile wait policy.
- Volatile waits are scheduler continuation state; durable bookmark resume remains separate.

## Constitution Check

| Gate | Status | Notes |
| --- | --- | --- |
| Runtime must not depend on Design | PASS | Policy uses Runtime.Core contracts and models only. |
| Volatile wait is not durable suspension | PASS | Policy decisions do not carry bookmark or resume target identifiers. |
| Provider neutrality | PASS | Registration uses `TryAddSingleton`; shells/providers can replace the default. |
| Scope control | PASS | No wait execution, timer implementation, durable fallback execution, or control-plane behavior. |

## Implementation Steps

1. Implement a default `IRuntimeVolatileWaitPolicy` in Runtime.Core services.
2. Register the policy in `WorkflowsRuntimeApiFeature` with `TryAddSingleton`.
3. Document the default policy in Runtime.Core extension points.
4. Add focused policy, DI, and separation tests.
5. Run validation and self-review.

## Risks

- A default allow/deny policy may be mistaken for host-specific runtime support. Metadata and denial reason should make clear that host support is an input to the decision.
- Future durable fallback work must remain explicit and must not reinterpret volatile waits as bookmarks by default.
