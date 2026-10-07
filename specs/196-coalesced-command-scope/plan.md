# Implementation Plan: Coalesced Command Scope

**Branch**: `claude/runtime-db-coalesced-scope` | **Date**: 2026-10-06 | **Spec**: [spec.md](spec.md)

**Input**: [T19/#2450](https://github.com/elsa-workflows/elsa-foundation/issues/2450), Program #2382. Planning draft; runtime implementation awaits independent spec/plan/tasks review.

## Summary

Replace the default Coalesced drain factory's singleton registration with a scoped registration. Its constructor retains scoped `RuntimeCheckpointCommitter` and `CoalescingInner<IWorkflowSchedulerWorkQueue>` / `CoalescingInner<IRuntimePostCommitOutboxStore>` services; the singleton currently resolves these from shell root. Preserve `TryAdd` semantics, shared AsyncLocal session accessor, singleton options/policy, decorators, custom registrations and all drain/checkpoint logic. Do not change the global shell container's validation policy.

The contract defect is source confirmed. It is a candidate mechanism for C4 query overlap, not attribution of each prior query error/HTTP outcome. [research.md](research.md) records the separate shell-provider evidence and missing joins.

## Technical Context

**Language/Version**: Existing C# / .NET10 repository configuration.

**Primary Dependencies**: Existing Microsoft DI and EF runtime registrations, pinned CShells composition. No package/reference changes.

**Storage**: No schema/model/persistence format change. The DI contract controls resolve actual EF registrations without opening a database. Rebuilt normal-host validation owns fresh PostgreSQL runtime and SQLite diagnostic resources only.

**Testing**: Existing xUnit runtime and EF provider test projects; existing HttpEndpoint/REST control fixtures; existing architecture and Maps tools. Deterministic before-fix and revert mutation proof, then affected suites.

**Target Platform**: Existing supported runtime platforms. Scoped lifetime is provider independent; supported provider CI legs remain required where applicable.

**Project Type**: Existing runtime library registration correction.

**Performance Goals**: None for this unit. Report observed correctness separately from bounded final timing; no global benchmark/gate/default changes.

**Constraints**: Preserve all current behavioral assertions and guarantees. Root and independent Sol own review/verification; serialized builds/hosts through queued dotnet. Publication/deployment excluded, subsequent merges held pending the existing owner question.

**Scale/Scope**: One default registration plus focused shared-fixture contract tests and spec/evidence artifacts. No second DI container, shared-context locking, provider rewrite or feature redesign.

## Constitution Check

Root reviewed both ratified constitutions. Framework §2.1/§2.6: correction stays in domain-owned runtime registration; no new coupling or feature. §2.21.1: existing test subjects/assertions remain. §2.23.1: registration resolves under scope validation; §2.23.2: no logic-bearing implementation change, existing behavior suite retained. §2.16: reuse existing projects, no new package. Elsa §E2.2/§E2.6: runtime remains artifact-only and gains no design dependency. No reliance on draft framework §2.24 or Elsa §E2.9 is required. Pre-design and post-design checks pass; no constitutional exception or test deletion is proposed.

## Project Structure

```text
specs/196-coalesced-command-scope/
  spec.md, plan.md, research.md, data-model.md, quickstart.md, tasks.md
  contracts/drain-scope-lifetime.md
  evidence/ (reviewed controls and final provenance)
src/essentials/Workflows/Runtime/Api/Coalescing/
  CoalescingRuntimeCheckpointPersistenceExtensions.cs
tests/essentials/Workflows/Runtime/Tests/
  RuntimeCheckpointCoalescingTests.cs
tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/ProviderTests/
  RuntimeCoalescingScopeRegistrationTests.cs (proposed focused test file)
```

**Structure Decision**: Keep the single registration correction at its current feature seam. Place real EF composition identity/validation controls in its existing provider test project, without attaching container fixture collections or performing queries. Reuse shared setup within the new test file; avoid reflection into primary-constructor fields or implementation-only mirror assertions.

## Validation Design

1. Compose existing runtime + EF aggregate registrations with Coalesced selection. Use two async service scopes and `ValidateScopes` true/false controls. Resolve factory, committer, inner queue/outbox and RuntimeDbContext; assert same factory/collaborators within a scope and distinct scoped instances across scopes. ValidateScopes=true rejects the before-fix captive graph; false exposes the shared factory. Do not validate unrelated hosted-pump graph or open a DB.
2. Before editing runtime registration, run the new deterministic controls against failed-base source932 and retain exact source/test diff, command/TRX and expected failure. Tests must fail for the lifetime contract, not missing serializer/logging setup.
3. Use `TryAddScoped` for the default factory. Preserve pre-registered custom factory and registration-marker idempotency; add focused controls in the existing runtime registration fixture if missing.
4. Run the targeted controls green, then revert only that registration in a temporary mutation working diff and require the controls fail again. Restore it and record source hashes; mutation is not a second implementation or a performance run.
5. Run existing affected runtime suite and provider correctness gates as appropriate, rebuilt relevant backend HTTP e2e and primary/valid REST representative controls. The final C4 case belongs to downstream T17/T18 integrated acceptance, must retain every attempt and verify terminal state/incidents, and is not a T19 completion criterion. Root reviews all delegated changes and integrated evidence.
6. Run architecture and generated-maps freshness checks. Refresh only genuinely changed generated snapshots as part of this authorized correction after reviewing the generated findings; stage each changed path including manifest. No stale map is used as navigation proof.
7. Publish coherent scoped draft PR, exact-head gates/review rounds and issue/Project transitions. Green PR is distinct from resulting-main CI/Maps. Publication boundary hold remains until owner answers; never treat skipped/unavailable checks or unchanged retries as repairs.

## Complexity Tracking

No violations or new abstractions. A global scope-validation change is rejected because it changes unrelated shell composition; serialization is rejected because it hides scoped capture while retaining incorrect ownership. A new factory/session type is unnecessary: existing logic already accepts command-scoped collaborators.
