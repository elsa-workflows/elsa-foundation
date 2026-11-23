# Implementation Plan: Standalone triggers and workflow subscriptions

**Branch**: `agent/codex/3582-standalone-triggers` | **Date**: 2026-11-06 | **Spec**: [spec.md](spec.md)

**Input**: #3582 and its recorded end-to-end owner authorization. Program-goal state: `none/free-flow`; milestones stay on that issue. Task-ordered stacked PRs of roughly 3,000 changed lines per spec slice, following the owner’s 9 November triage of #3912. Existing programs keep their priorities and pauses.

## Summary

Deliver independently managed live HTTP, Event and Timer/Cron resources with subscriptions editable from trigger details and the workflow editor. Fresh admission follows the selected serving publication slot, maps declared inputs and captures exact per-subscription responsibility. Accepted work survives later pause/unpublication; operators separately cancel work that has not committed to dispatch. Async HTTP offers the owner's per-trigger RequireAll/AcceptAvailable policy. Invocation grants belong to the shared resource, with creator audit attribution.

Use the existing Runtime delivery store family, exact-kind dispatcher and resumption pump. Add a real standalone source arm, provider-atomic admission, immutable recipient captures and a durable dispatch-commit seal. Preserve the old workflow-checkpoint intent/wire and embedded routing. Native targets enter ordinary roots. A bounded model adapter supplies genuine BPMN slot/input/start support through its ordinary model protocol. Studio reuses existing routes, resource controls and workflow designer panels.

## Technical Context

**Language/Version**: C#/.NET 10 (`global.json` pins SDK 10.0.300); TypeScript/React and pnpm 11.9.0 from the existing Studio workspace.

**Primary Dependencies**: Existing Runtime Core/engine/API, EF persistence and native in-memory composition; publication activation coordinator/switch; Expressions Core portable pure evaluator; HTTP, Primitives Event and recurring scheduling; existing Studio Hosting SDK, Workflows module and studio-ui. Cronos is already pinned at 0.13.0. No new root domain, framework, broker or queue.

**Storage**: Existing Runtime provider family with new resource/subscription/grant/source/recipient facts and additive outbox/source projections. Implement shared-context EF admission for SQLite, PostgreSQL, MySQL and SQL Server through their existing migrations; preserve provider-owned contexts. In-memory is process-local and advertises no restart/multi-node durability. Unsupported split/legacy composition refuses operations requiring the guarantee.

**Testing**: Risk-based behavior tests in affected complete projects; actual owned-host/API/HTTP/Event/schedule journeys; provider races and restart/multi-node witnesses; independently built module consumer; Studio unit/type/browser checks and extended composed UX acceptance with inspected artifacts.

**Target Platform**: Existing Design-only, Runtime-only and combined hosts; supported relational engines and normal Studio host. Execution/recovery does not read workflow Design state.

**Project Type**: Modular runtime feature with API, source/model adapters and integrated Studio management/operations.

**Performance Goals**: No performance program. Preserve normal bounded paging, claims, retry/backoff and deterministic identity; do not introduce an unbounded catch-up or per-subscriber polling loop.

**Constraints**: Accepted ADR0043 authority/exclusive endpoints, ADR0038 material identity, ADR0040 retention, existing input/expression/serializer semantics, Balanced host default, preserved legacy keyed starts and checkpoint wire, tenant/access scope and resource-owned grants. The proposed October ADR0043 convergence amendment stays proposed.

**Scale/Scope**: One live resource layer, one canonical association, four initial families, two HTTP response modes and two async admission policies. Native and genuine supported BPMN targets; optional module-provided types. Durable messaging, release promotion and background jobs remain separate products.

## Constitution Check

Loaded framework v6.1.0 and Elsa v6.3.0, both Gate indexes and the review checklist. The initial design uses existing root domains and packages. No constitution exception is requested.

- Core contains public consumable models/contracts only, with existing Core references; policies, mapping, authority and persistence implementations remain outside Core (§2.1, §2.18.2). New types default internal; expose only contracts a third-party module/provider actually needs, with PublicAPI deltas and consumer qualification (§2.23.3).
- Runtime takes no new Design dependency and needs no drafts/history to execute (§E2.2, §E2.6). Publishing adapters provide trusted authored provenance; Runtime compares captured slot/material/source facts.
- Separate authoring/type descriptors from runtime intake/target contracts (§2.6.4). Use normal named registry events/aggregators and existing exact-kind delivery registration. Single replacements diagnose duplicates (§2.6.1–3, §2.11).
- Keep normal provider composition; no required ElsaDbContextBase and no provider implementation dependency from an unsuffixed feature (§2.9, §2.20, §E2.5). The acceptance contract verifies actual physical participants.
- Maintain affected valid behavior and causal regressions for risky new paths, with registration/independent consumer tests (§2.21.1, §2.23.1–4). Tests do not assert on documentation text.
- Update owning extension-point catalogs/feature READMEs and root index where required (§2.22); use normal computed versions, compare public delta and leave old wire/version meanings intact (§4, §E5).
- Live trigger configuration is not added to WorkflowDefinitionState. Its provisional scope policy, activity-versioning/Model X and the framework's draft pattern catalog are not ratified here. Existing approved input/expression meanings are consumed, not changed. A discovered requirement to amend a #3052 decision is isolated into its human agreement process before dependent implementation.

Post-design check repeats after contract review. Required authority amendment is a bounded addition to ADR0043, separate from its unaccepted convergence amendment. It covers independent ingress claims, captured accepted-delivery authority and model-neutral target slot participation; it does not create a second slot ledger.

## Project Structure

Feature records: spec, research, architecture, data model, runtime/target/API/transport contracts, UX acceptance, quickstart and tasks in this directory. Issue/PR comments carry state/evidence; no checkpoint issue or duplicate program is added.

Implementation stays in these existing packages unless reviewed consumer evidence proves another boundary necessary:

| Owner | Intended work |
|---|---|
| `Essentials/src/Workflows/Runtime/Core/` | Public resource/subscription/source/capture/grant/store/admission/control contracts and additive retained authority; existing models/wire stay valid. |
| `Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/` | Optional standalone resource feature, validation/mapping, common source admission/delivery, dispatch commitment and root adapter; existing delivery processor/recovery updated for real source kinds. |
| `Essentials/src/Workflows/Runtime/Persistence/EntityFrameworkCore/` | Scoped entities/configuration/store composition, conditional resource/recipient changes, source-neutral outbox projections, exact retention, atomic admission and four provider migrations. |
| `Essentials/src/Workflows/Runtime/Api/` | Existing endpoint-class/capability/permission composition; shared association, impact/preview/control/delivery APIs. |
| `Essentials/src/Workflows/Runtime/Http/` and `Essentials/src/Activities/Http/` | Common exclusive claims/route generations and request adapter, explicit mode/policy, durable acknowledgement and request-owned response observation. |
| `Essentials/src/Workflows/Runtime/Scheduling/` | Resource-owned schedule arm and existing pump submission; stable due keys, revision fences, timezone/clock/misfire policy. |
| `Essentials/src/Activities/Primitives/` | External Event adapter and typed subscription recipients in canonical committed Event cohorts; preserve embedded/wait/local behavior. |
| `Essentials/src/Workflows/Publishing/` and `Essentials/src/Activities/Bpmn/` | Trusted logical source/slot/input profile bridge and ordinary model target adapter. No fake native workflow/material for genuine BPMN. |
| `Studio/src/essentials/Elsa.Studio.Workflows/` | Trigger routes/list/details/edit, shared association/mapping editor, workflow Triggers panel and delivery inspection. Hosting SDK changes only for a demonstrated public customization need. |
| Existing domain tests, `Studio/tests/acceptance/`, `tools/ux-acceptance/` | Behavioral/provider/consumer regressions, real composed journeys and finite artifact inventory. |

No new test project is planned initially. If a separate consumer/host project is needed, assign its proper CI tier and solution-filter entry; generated maps remain uncommitted.

## Staged critical path

1. **M0 — review contract before production.** Reconcile the confirmed product decisions across artifacts; settle concrete source/cancel/model/API/transport contracts; add the bounded proposed ADR0043 amendment. Independently review specification, correctness and standards, fix surviving findings, record the reviewed design and update readiness. This is preparation, not feature acceptance.
2. **M1 — management and immutable preparation.** Implement scoped revisioned resources, subscriptions and resource grants through normal provider composition/API. Add descriptor discovery, serving-slot input contract selection and pure mapping/preflight. Enable only supported compositions. Prove both API directions address one association, failed edits preserve live state and unpublished rows remain disabled.
3. **M2 — durable responsibility.** Extend the existing delivery family with an explicit standalone source; implement provider-atomic intake, immutable captures/retention, duplicate/conflict lookup and recipient transitions. Resolve cancel versus dispatch through the same durable recipient CAS. Add native retained-root starts and uncertainty reconciliation. Prove crash/race/partial recovery before transports acknowledge durable acceptance.
4. **M3 — transport and model integration.** Join common HTTP claim reservation and route generations; implement both modes and async policies. Extend the existing schedule owner/pump and canonical Event cohort. Add the genuine BPMN authored slot/input profile and ordinary model starts. Run real backend and provider/consumer witnesses for the capabilities each adapter advertises.
5. **M4 — complete Studio journeys.** Build trigger management and the workflow panel using one association editor. Show live/draft/current/captured distinctions, mapping and policy, impact/conflict recovery, permissions/source ownership and separate delivery/execution/transport facts. Extend actual composed acceptance and documentation/examples.
6. **M5 — integrate and deliver.** Commit the candidate; qualify actual host/Studio and required suites; inspect current-commit downloadable visual artifacts independently; run clean review rounds on the draft PR. Mark ready/request review after convergence, satisfy current-head checks and normal merge rules, then qualify resulting main before issue/spec closeout.

Independent implementation edits may run concurrently only after their contracts converge and file ownership is explicit. Build/test heavy work runs serially through the machine wrapper. The canonical branch/claim is rechecked before commits and publication; push only when opening its scoped draft PR in the same session.

## Qualification and evidence

Every changed behavior has an appropriate failure control and post-change proof. Record test inventory versus executed counts for whole affected projects. Use the smallest affected projects while developing; the cross-cutting final change earns required broader CI, architecture, maps and provider gates.

| Gate | Required evidence |
|---|---|
| Resource/API | Whole Runtime/API suites plus scoped resource/association/grant lifecycle and endpoint permission tests; same association from both APIs; stale edits/refused activation unchanged. |
| Admission/recovery | Whole Runtime and affected EF projects; real duplicate/conflict, publication/resource races, retention/collection, seal/cancel, source commit/queue crash gaps and partial restart/multi-node controls. |
| HTTP/Event/schedules | Whole affected HTTP/Primitives/Scheduling suites and real composed requests/events/due firings; both policy results, no false acceptance, route conflicts both ways, no competing response delivery, due/clock/timezone/update semantics. |
| Genuine BPMN | Whole affected BPMN/Interchange/Hosting suites; model publication/slot/input consumption/restart and native wrapper controls separately; unsupported profiles/modes refuse. |
| Modules/providers | Independent public-contract consumer and selected physical composition; split or legacy provider refusal; unchanged old source/checkpoint/start formats and material identity. |
| Studio | Workflows/Hosting impacted suites, typecheck/build and actual composed UX plan; inspect all changed surfaces and required captures. Skips/retries/fixtures do not become successful product acceptance. |
| Repository gates | Architecture graph restore + full Architecture test project, generated-maps check and test tiers; locked restore if ProjectReferences change. Exact-head required CI and resulting-main evidence. |

The [quickstart](quickstart.md) records commands and the [UX plan](ux-acceptance.md) records the finite inspected journey inventory. Use owned temporary hosts/stores/ports only. CI artifacts must identify source and PR/merge SHA and be downloadable at review time. No execution of a gate is implied by this plan.

## Complexity Tracking

No constitutional violation is proposed. The added source/recipient/grant/control layer earns its cost through independent resource operations, reverse discovery, slot-following and durable transport responsibility. Existing delivery/scheduling infrastructure is extended; no general broker, hidden orchestration workflow or second serving ledger is introduced.
