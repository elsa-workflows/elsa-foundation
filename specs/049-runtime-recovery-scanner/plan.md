# Implementation Plan: Runtime Recovery Scanner

**Branch**: `codex/runtime-recovery-scanner` | **Date**: 2026-06-11 | **Spec**: [spec.md](./spec.md)

## Summary

Add a default in-memory recovery scanner that reads operational coordination state and produces bounded recovery candidates for expired leases, stale heartbeats, and already-detected interrupted executions. The slice keeps scanning separate from requeue execution, actor placement, outbox delivery, and domain retry.

## Technical Context

- `OperationalState` already models execution leases, heartbeats, drains, interrupted executions, and pending post-commit intents.
- `IRuntimeRecoveryScanner` and `RuntimeRecoveryCandidate` already exist as contracts.
- `IOperationalStateStore` currently lists by workflow execution only, so this slice adds a minimal `ListAllAsync` operation needed by scanner implementations.

## Constitution Check

| Gate | Status | Notes |
| --- | --- | --- |
| Runtime must not depend on Design | PASS | Scanner uses Runtime.Core contracts and models only. |
| Operational recovery is not domain retry | PASS | Scanner emits recovery candidates only; it does not call domain retry policy. |
| Actor/provider neutrality | PASS | No ownership claiming or actor framework dependency. |
| Scope control | PASS | No requeue execution, distributed lease enforcement, or durable provider. |

## Implementation Steps

1. Add all-state listing to `IOperationalStateStore` and the in-memory implementation.
2. Implement `InMemoryRuntimeRecoveryScanner`.
3. Register `IRuntimeRecoveryScanner` in the runtime API feature.
4. Add focused scanner, store, DI, and architecture tests.
5. Run validation and self-review.

## Risks

- All-state scans are suitable for the in-memory default only; durable providers should implement indexed recovery queries in later provider-specific slices.
- Recovery candidates identify work to recover but do not acquire ownership. Claiming remains a later distributed/provider concern.
