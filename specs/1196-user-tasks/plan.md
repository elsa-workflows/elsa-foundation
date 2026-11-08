# Implementation Plan: User Tasks

**Branch**: `agent/codex/3577-user-tasks` | **Date**: 2026-11-02 | **Spec**: [spec.md](spec.md)
**Input**: #3577 under owner-authorized Program #3688.

## Summary

Extend the existing Runtime domain with a durable human-task journal and form snapshots, a typed Create User Task activity, authorized participant/manager APIs and an optional Studio inbox/form authoring module. Existing runtime waits and queue/checkpoint authority own continuation; journal reconciliation confirms committed completion and repairs missed notifications. Native workflows and published native workers used by BPMN share the capability.

## Technical Context

**Language/Version**: existing .NET 10/C#, TypeScript/React and pnpm toolchain.
**Dependencies**: Runtime Core/wait/queue/external-payload contracts; existing Runtime EF family and provider registrations; Identity Core current subject/user/role/membership contracts; NativeEndpoints; Studio SDK/UI.
**Storage**: existing Runtime relational provider/context, task aggregate CAS and immutable form-version records; memory adapter is explicit development/testing only. No quiet mixed durable/runtime-memory composition.
**Testing**: risk-based xUnit lifecycle/validation/recovery/concurrency/persistence/HTTP tests, Vitest module/adapter tests, mounted/browser journeys and owned real hosts. Each changed behavioral claim has a causal negative control. Compare whole-project test/list counts; required gates and resulting main.
**Target**: existing server/worker/custom hosts and Studio browser; no release/deployment.
**Project type**: modular library/API/browser feature.
**Performance goals**: bounded indexed authorized paging/recovery; no timing benchmark program.
**Constraints**: exact wait identity, tenant scope/partition, trusted current membership/permissions, immutable accepted payload, UTC instants, API casing preservation, no design dependency in Runtime, shared machine build slots.
**Scale/scope**: one assignee or one role group per task; common forms and one response. Existing runtime persistence modes remain authoritative.

## Constitution Check

Framework v6.1.0 and current Elsa constitution, both gate indexes and `docs/contributing/review-checklist.md` loaded. Runtime remains independent of Design and concrete providers. Core contains contracts/models only; implementations internal unless host/integration surface. New public surface is tracked. Services scoped except immutable options and fresh-scope scheduling pumps. Store and form-provider ownership diagnosed at composition. Named Sequential resume-validation event is reused; no new handler loop/event system. Extension-point catalogs and root index updated in the same unit. Forms are domain resources, never mutable workflow definitions required to run a published artifact. Feature classes public/nonsealed with virtual ConfigureServices. Supported provider migrations, architecture/map/tier checks and project reference locked restore apply. Design-only/runtime-only/combined composition preserved.

Provisional activity versioning/Model X and workflow-state scope are not ratified by this work; existing activity catalog/publication behavior is consumed without changing their meaning. No new root domain/constitutional exception. Greenfield test cadence is undeclared by the derived constitution: use risk-based tests with causal failure probes under ADR 0080 and record that choice, without introducing a repository-wide TDD rule.

## Design and project structure

- `Essentials/src/Workflows/Runtime/Core/UserTasks/`: task/form/response/history/query models, journal/lifecycle/form-provider replacement and integration contracts, stable protocol identifiers.
- `Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/UserTasks/`: transition/validation, default basic forms, memory adapter, reconciliation and resume guard; registration via runtime composition.
- `Essentials/src/Workflows/Runtime/Persistence/EntityFrameworkCore/`: task/form entities/configuration, scoped conditional journal, provider wiring and four-provider migrations in existing family.
- `Essentials/src/Activities/Primitives/`: typed CreateUserTask, invocation-local suspension/response, dynamic action outcomes and secret-binding policy.
- `Essentials/src/Workflows/Runtime/Api/`: normalized current identity/eligibility, participant/manage/form/target endpoints, capability relations and safe errors.
- `Extensions/UserTasks/studio/Elsa.Studio.UserTasks/`: capability-gated inbox/detail/history/form authoring and property editors; existing Studio host composition/solution registrations.
- `Studio/src/essentials/Elsa.Studio.Workflows/`: generic literal collection dynamic outcome consumption.
- Existing affected test projects and new Studio module tests/browser fixtures; a new test project only if existing domain suites cannot isolate the necessary behavior.
- `website/docs/`, affected READMEs/catalogs and this packet for guidance.

Authoring forms publish immutable versions. Activity input references an exact version/provider or embeds a snapshot, and receives prefilled JSON from workflow inputs. Task creation freezes resolved schema/actions/prefill. Default group IDs are tenant role IDs; integrations replace eligibility/form behavior through explicit contracts. Workflow authors configure action labels as stable outcome names and optional per-action required-field waiver.

## Delivery/review sequence

Review specification → research/design/data-model/contracts/acceptance → tasks before source changes. Establishment/docs and implementation remain in the #3577 delivery PR, with Program transitions on #3688. Read claims/open PRs before commits. Run affected projects through the build wrapper; no whole solution local run unless justified by actual cross-cutting failure. Keep PR draft through local review, then required CI, review and permitted merge. Only qualify as complete after rebuilding and rerunning decisive rows on resulting main. Program checkboxes record evidence, never intention.

## Complexity tracking

A separate task journal introduces a second durability boundary. Its necessity is human assignment/form/history independence from scheduler state. The design retains explicit pending responsibility and proves completion from canonical runtime state; it does not claim a cross-store atomic commit. Same-context EF task mutation commits one aggregate CAS, not unrelated pending runtime mutations. Recovery always uses fresh partition scopes. No extra scheduler, token engine or general form-layout system.

## Pre-implementation review, 2 November

Independent runtime and identity/persistence review found two correctness gaps and an API ambiguity. Resolved design: capture the complete physical bookmark recipient and enforce its generation atomically at consumption; refuse unsupported exact-resume providers. Add a provider-level workflow deletion/retention fence for nonterminal task evidence, racing atomically with task creation. Before durable acceptance, unavailable dependencies return a retryable error; after acceptance, preserve/return the pending receipt under runtime outage. These refinements are implementation tasks and regression scenarios, not waived acceptance. No new checkpoint participant is needed.
