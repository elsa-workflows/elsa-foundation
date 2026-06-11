# Implementation Plan: Runtime Activity Input Resolution

**Branch**: `codex/runtime-activity-input-resolution` | **Date**: 2026-06-11 | **Spec**: [spec.md](./spec.md)

## Summary

Extend runtime activity input materialization so invocation consumes compiled runtime bindings through the existing resolver, covering literals, active activity outputs, and durable values.

## Technical Context

- `RuntimeInputBindingResolver` already resolves literal, durable value, active activity output, expression declarations, and references.
- The invocation handler has workflow execution ID and activity execution ID from scheduler work.
- Active output and durable value stores are runtime continuation/value stores, not Design models.

## Constitution Check

| Gate | Status | Notes |
| --- | --- | --- |
| Runtime must not depend on Design | PASS | Uses executable node bindings and runtime stores only. |
| Activity outputs are scoped/ephemeral | PASS | Activity output reads go through active output reader only. |
| Durable capture is explicit | PASS | Durable reads use declared durable values, not raw output history. |
| History outside continuation state | PASS | No history/audit output reads are introduced. |

## Implementation Steps

1. Add Speckit slice artifacts and update active pointers.
2. Mark previous PR-loop task complete.
3. Extend input materializer contract with resolution context.
4. Resolve literal, active output, and durable value inputs through `RuntimeInputBindingResolver`.
5. Build invocation resolution context from active output and durable value stores.
6. Register the default runtime input binding resolver.
7. Add focused invocation/materializer tests.
8. Run focused validation and self-review.

## Risks

- Expression and reference inputs remain declarations until dedicated provider/evaluator slices implement them.
