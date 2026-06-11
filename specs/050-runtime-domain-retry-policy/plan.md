# Implementation Plan: Runtime Domain Retry Policy

**Branch**: `codex/runtime-domain-retry-policy` | **Date**: 2026-06-11 | **Spec**: [spec.md](./spec.md)

## Summary

Add a conservative default domain retry policy that makes the runtime boundary executable without adding retry behavior. The policy returns an explicit `DoNotRetry` decision and is registered as an overridable runtime service. Operational recovery remains a separate candidate/requeue concern.

## Technical Context

- `IRuntimeDomainRetryPolicy`, `RuntimeDomainRetryRequest`, and `RuntimeDomainRetryDecision` already exist as contracts.
- Runtime API composition currently registers recovery scanner and post-commit outbox services, but not a domain retry policy.
- Recovery scanner emits candidates only and must not consult domain retry policy.

## Constitution Check

| Gate | Status | Notes |
| --- | --- | --- |
| Runtime must not depend on Design | PASS | Policy uses Runtime.Core contracts and models only. |
| Operational recovery is not domain retry | PASS | Default policy is separate from recovery scanner and recovery candidates. |
| Provider neutrality | PASS | Registration uses `TryAddSingleton`; provider/shell policy replacement remains possible. |
| Scope control | PASS | No scheduler retry work, retry counters, incident handling, or backoff strategy. |

## Implementation Steps

1. Implement a default no-retry `IRuntimeDomainRetryPolicy` in Runtime.Core services.
2. Register the policy in `WorkflowsRuntimeApiFeature` with `TryAddSingleton`.
3. Document the default policy in Runtime.Core extension points.
4. Add focused policy, DI, and operational separation tests.
5. Run validation and self-review.

## Risks

- A no-retry default may look like final retry behavior. Naming and metadata should make clear that it is the baseline boundary and shells/providers can replace it.
- Future activity-level retry policy work must not reuse operational recovery candidates as retry attempts.
