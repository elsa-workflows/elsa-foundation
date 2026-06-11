# Implementation Plan: Runtime Wait Registration And Post-Commit Intent Contract

**Branch**: `codex/runtime-wait-intent-contract` | **Date**: 2026-06-11 | **Spec**: [spec.md](./spec.md)

## Summary

Add the next Runtime Execution Seam addendum slice after runtime control-plane contracts. This slice codifies durable wait registration/correlation state for waits that depend on Elsa-caused outbound side effects and are paired with post-commit intents.

## Technical Context

- `RuntimePostCommitIntent` already carries `DependsOnWaitRegistrationId` and a wait failure policy.
- `RuntimePostCommitOutboxItem` already models post-commit delivery state.
- `BookmarkState` remains the durable resume handle; this slice does not add a new checkpoint state category.
- This slice adds the missing wait registration/correlation contract used by post-commit intents and future stores.

## Constitution Check

| Gate | Status | Notes |
|---|---|---|
| Runtime must not depend on Design | PASS | This slice changes Runtime.Core models and runtime tests only. |
| Runtime-owned state boundaries | PASS | Wait registration references runtime workflow/activity/correlation IDs. |
| Focused tests for logic-bearing contracts | PASS | Add dedicated wait registration contract tests. |
| Scope control | PASS | Stores, matching algorithms, inbox retention, and outbox processors are out of scope. |

## Scope

- Add durable wait registration/correlation contract.
- Add wait registration statuses and early-signal policy.
- Extend wait-dependent intent failure policy with compensation.
- Add focused runtime wait-intent contract tests.

## Out of Scope

- Full wait/bookmark store.
- Global unmatched inbox.
- Signal matching engine.
- Outbox delivery processor.

## Validation

- `dotnet test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj`
- `dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj`
- `dotnet build src/Elsa/Workflows/Runtime/Core/Elsa.Workflows.Runtime.Core.csproj`
- `git diff --check`
