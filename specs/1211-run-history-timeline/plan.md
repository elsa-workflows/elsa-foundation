# Implementation Plan: Timeline workspace and durable run history

**Branch**: `agent/codex/3849-history-slice-*` | **Updated**: 2026-11-12 | **Spec**: [spec.md](spec.md)

**Authority**: End-to-end owner instruction on [#3849](https://github.com/elsa-workflows/elsa-foundation/issues/3849#issuecomment-6645438505), amended by the [9 November re-slicing decision](https://github.com/elsa-workflows/elsa-foundation/pull/3850#issuecomment-6739905865). #3850 is closed without merge and remains a prototype/evidence reference. Deliver small stacked PRs in the [slice order](slices.md), each with its own qualification and review. This explicit owner instruction supersedes the former single-PR plan; it does not reduce the parent acceptance criteria.

Review/merge follows the current organization [standing self-merge protocol](https://github.com/elsa-workflows/.github/blob/main/AGENTS.md#standing-self-merge-codex-and-claude). Independent correctness, repository-standards and acceptance lenses review the exact slice head without seeing each other's output; record numbered rounds and inherited findings, fix in additive commits and repeat within the governing cap. Preserve the closed prototype's four recorded rounds rather than presenting them as acceptance of the new stack. Required checks, actual independent UI inspection and all holds/protections remain gates. No paused program scope changes.

## Resumption baseline

Start from `main` `dff7c490d0e5501b22d4ba89beb389c956d4f423`. Compact Inputs (#4108 and its #4174 journey correction), run-canvas visibility work, the Decisions inventory correction, and newer checkpoint/read-path changes are present. Preserve them. The activity Data view must reuse the landed input-row/value-evidence implementation and tests rather than restore the old #3881 files. Historical source or passing receipts are not current-source acceptance. See [landed work and slice checkpoints](slices.md).

## Summary

Deliver the selected Timeline workspace with one Runtime-owned, production history queried at run, execution and bookmark scope. Capture typed immutable candidates before coalescing into a RuntimeCheckpointCommit write sidecar, outside continuation state, and persist queryable history, its sequence head and bookmark lifecycle projections inside the existing checkpoint transaction. Do not use the volatile QA collector, ordinary per-step outbox intents, latest-state inference or another domain. Preserve persistence modes and current command/value authority.

Research decisions and current-source evidence: [research.md](research.md). Shared model: [data-model.md](data-model.md). Contracts: [atomic capture](contracts/atomic-history.md), [queries and Studio](contracts/history-inspection.md). Qualification: [quickstart.md](quickstart.md).

## Technical Context

**Language/Version**: C# / .NET 10 (repository SDK 10.0.300); TypeScript / React 19; pnpm 11.9.0 and repository Node selection.

**Primary Dependencies**: Existing Runtime Core/default/EF/API projects; existing EF module and migrations; existing runtime continuation codec and structure/value authorization; Studio Workflows module, React Query and shared Studio UI/tokens. No new external runtime dependency.

**Storage**: Existing RuntimeDbContext, one Runtime migration history, additive SQLite/PostgreSQL/SQL Server/MySQL migrations. In-memory implementation shares the existing atomic checkpoint store state; it makes no restart guarantee.

**Testing**: Existing xUnit Runtime, Activities Runtime, Runtime EF/provider, migrations, API and Architecture projects; Workflows Vitest/typecheck/build; focused browser regressions and an added normal composed acceptance journey. Registration and risky behavior need tests; use causal failure/mutation controls. ADR 0080 D1 governs test selection. No timing benchmark.

**Target Platform**: Existing supported server/Studio hosts; Runtime-only, Design-only and combined compositions remain valid.

**Project Type**: Additive runtime library/provider/API capability and existing Studio module UI.

**Performance Goals**: Deterministic bounded work. One sequence allocation per physical commit, batched candidate inserts, bounded lifecycle projection staging; keyset reads default 100/max existing shared limit. Exact ID lookup is indexed. Free-text matching uses bounded ordered search windows with explicit searched-through coverage, not unbounded SQL substring/count scans. Qualify 10,000-event histories with pages at most 100.

**Constraints**: No new domain/provider family, no per-activity checkpoint forced by history, no cross-store ACID, no guessed historical transitions, no raw payload/exception metadata capture, no browser full-history download, no history-reading side effects. Existing hosts/stores are preserved; all proof hosts/stores are owned/disposable or frozen copies.

**Scale/Scope**: All seven spec journeys and 47 FRs. Native/BPMN/ETL runtime semantic owners remain independent; optional module coverage is declared honestly. Baseline direct atomic storage has no separate materialization queue: its persisted boundary is settled, while unsupported/late/disabled/purged/invalid ranges remain explicit.

## Constitution Check

The resumption baseline contains framework v6.1.0 and Elsa v6.5.0. Apply their Gate indexes and docs/contributing/review-checklist.md: §2.1/2.6 ownership, §2.9 provider-independent ports, §2.22 catalogs, §2.23 risky behavior/registration/internal visibility/public API tracking, §E2.2 Runtime/Design boundary and all deployment shapes, §E5 computed versions and §E7 test tiers. Revalidate each slice against the current constitution; the original review used an earlier Elsa version.

New types stay in existing Workflows.Runtime ownership. Provider authors need the public atomic participant/query contracts; implementations remain internal unless an existing public contract requires otherwise. Reuse the existing RuntimeHistoryEvent observability envelope compatibly where suitable rather than removing its legacy constructors. Record additions in public API baselines and extension-point catalogs. No project/domain/feature identity is retired, no new project is planned and no constitution exception is requested.

CI boundary correction: the existing continuation/history separation test remains unchanged. History batches move off RuntimeCheckpointStateChangeSet into a separate checkpoint write sidecar; coalescing preserves them independently. Writer/reader qualification is lazy at actual admission, preserving strict scoped pairing without making ordinary service resolution depend on an execution scope. See research R11. Corrective reruns and independent review remain required.

The original design review supported this boundary; it is not a pass for the resumed implementation. The direct participant avoids the outbox/Ephemeral conflict; shared transactions, scoped readers and writer capability avoid another authority. Draft/provisional framework §2.24 and Elsa §E2.8/§E2.9 are not ratified or used to authorize an exception.

Greenfield cadence is not separately mandated by the derived constitution; use the accepted risk-based behavior test discipline, with failing controls for changed risky behavior. Do not invent a mandatory TDD or permission gate.

## Project Structure

### Documentation

- spec.md, plan.md, research.md, data-model.md, quickstart.md, contracts/, tasks.md and the retained design/reference/checklist.
- Evidence/receipts remain with the feature and existing issue/PR; no receipt issues or generated-map commits.

### Runtime and provider

- Essentials/src/Workflows/Runtime/Core/{Models,Contracts,Exceptions}: history batch/event/query/head/lifecycle contracts and provider handshake; checkpoint participant/fingerprint additions.
- Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/{History,Checkpoints,Coalescing,Bookmarks}: deterministic capture, safe semantic catalog, fold preservation, in-memory query/atomic storage and actual ingress observations.
- Existing Runtime feature/registration/options and Workbench composition: explicit production history support, independent from QA sessions.
- Essentials/src/Workflows/Runtime/Persistence/EntityFrameworkCore/{Entities,Configuration,Stores,DependencyInjection,Migrations}: scoped head/events/bookmark projections, atomic staging/deletion, schema-family declaration and additive four-provider migration chain.
- Essentials/src/Workflows/Runtime/Api/{Models,Requests,Handlers,Endpoints,Capabilities}: authorized history/event/bookmark queries and capability relations.

### Studio

- Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowInstances.tsx: primary Timeline/Diagram and separate activity/event/bookmark selection.
- New deferred run-history timeline, query observation, details, journal/bookmark components beside existing workflow-editor components.
- Existing api/runtime.ts, api/capabilities.ts, workflowTypes.ts, runSelectionUrl.ts, pages.tsx, useRunDetailLayout.ts and WorkflowActivityExecutionDetails.tsx; reuse exact execution browsing, policy-aware values and compact Inputs delivered by #4108.
- Existing module CSS with public --studio-* tokens, shared tab/focus/disclosure/copy/status primitives.

### Qualification

- Existing Essentials/tests/Workflows/Runtime, Activities/Runtime, Runtime/API and Runtime EF/provider/migration suites.
- Existing Workflows __tests__, Studio/tests/browser and Studio/tests/acceptance; tools/ux-acceptance/run.py feature journey/artifact inventory.

## Implementation sequence

The [slice ledger](slices.md) decomposes tasks into review units in task order. A supporting contract may land before consumers, but no slice may enable an incomplete capture/provider path. Every intermediate head must compile and preserve existing behavior. Keep schema models, targets, writer qualification and migration tests consistent when choosing provider boundaries.

1. Review/freeze the contracts and task dependency map, then implement the Core participant/fingerprint/capability and deterministic capture with real semantic tests.
2. Implement atomic in-memory and EF rows/heads/lifecycle projections, migrations, replay/rollback/restart/scope tests and retention/deletion.
3. Implement independently authorized bounded history queries/continuations and actual durable ingress/disposition recording; avoid marking consumption as successful continuation.
4. Integrate the Timeline workspace and journal/bookmark/event drill-downs over the same contract while preserving exact occurrence navigation and existing value access.
5. Complete semantic coverage, failure/race/capture-mode/authorization/long-history cases and composed user-task restart qualification.
6. Update catalogs/API baselines, run affected whole suites and deterministic budgets, inspect current-head downloadable composed evidence independently, complete review/merge and resulting-main qualification before closing #3849.

MVP is a sequencing checkpoint, not permission to close this feature early. End-to-end goal remains all required journeys and gates.

## Complexity Tracking

No constitutional exception. Five related persistence projections (head, immutable events, bookmark lifecycle, scoped counts and ancestry) share one existing transaction/schema family: indexed lifecycle/count/ancestry data avoids unbounded historical grouping for an activity's inventory or descendants. No new materialization service or domain is introduced.
