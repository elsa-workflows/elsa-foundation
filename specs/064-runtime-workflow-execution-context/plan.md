# Implementation Plan: Runtime Workflow Execution Context

**Branch**: `codex/runtime-workflow-execution-context` | **Date**: 2026-06-11 | **Spec**: [spec.md](./spec.md)

## Summary

Replace `WorkflowExecutionContext`'s `NotImplementedException` members with a narrow runtime-owned context backed by `WorkflowExecutionState`, explicit runtime inputs, runtime variables, and active activity-output mappings.

## Technical Context

- JavaScript runtime preprocessors depend on `IWorkflowExecutionContext`.
- Runtime state already carries pinned executable identity and workflow execution correlation ID.
- Activity outputs are active-scope runtime data and must not use authored activity IDs as durable lookup keys.

## Constitution Check

| Gate | Status | Notes |
| --- | --- | --- |
| Runtime executes pinned artifact | PASS | Definition/version values come from pinned executable identity. |
| Runtime state is continuation state | PASS | Context is runtime-owned in-memory surface; no history or authored document state is introduced. |
| Activity outputs scoped/ephemeral | PASS | Output lookup uses explicit active context mappings only. |
| Runtime must not depend on Design | PASS | Uses Runtime.Core and Expressions contracts only. |

## Implementation Steps

1. Add slice artifacts and update active Speckit pointers.
2. Mark previous PR-loop task complete.
3. Replace `WorkflowExecutionContext` stub with state-backed runtime context.
4. Add focused context tests.
5. Run focused runtime and architecture validation.
6. Self-review.

## Risks

- The existing context interface still carries legacy property names like workflow definition/version. This slice maps them to pinned executable identity only and does not reintroduce authored document loading.
