# Elsa Agents

- **Status:** Active; establishment and end-to-end M0–M7 delivery authorized, 31 October 2026. Product acceptance remains open.
- **Area:** User-defined specialists, immutable publication, model execution, native/BPMN composition, Studio and bounded teams.
- **Steward:** Sipke Schoorstra; one active implementation leaf with independent review support.
- **Charter, complete plan and acceptance:** [Program #3519](https://github.com/elsa-workflows/elsa-foundation/issues/3519).
- **Project:** [Elsa Agents #66 — Kanban](https://github.com/orgs/elsa-workflows/projects/66/views/1), [complete hierarchy](https://github.com/orgs/elsa-workflows/projects/66/views/3).
- **Activation and sequencing:** [ADR 0080 D0](../adr/0080-elsa-4-simplification-decisions.md#d0--focus), 31 October Elsa Agents amendment.

## Purpose

Users define reusable specialists such as Copywriter and Proofreader, test them, publish immutable
versions and use them as discoverable activities in native Elsa workflows and Elsa-powered BPMN.
Explicit input/output contracts make their results usable by the enclosing process. Fresh calls are
the default; conversation continuity is an explicit optional capability, distinct from cross-run memory.

Microsoft Agent Framework is the preferred integration candidate for agent execution, tools, sessions
and bounded collaboration, subject to pinned executable research. Foundry inspires configuration,
playground, evaluation, publishing and inspection. Neither source establishes an implemented Elsa feature
or requires Azure hosting. SDK-specific objects and credentials do not belong in runtime artifacts.

## Ownership and boundaries

Run beside the active programs, displace none, preserve consolidation's first priority and reactivate no
parked objectives. Consume delivered hosting, publication, activity-provider, native and BPMN contracts.
Coordinate shared changes through owning issues and valid claims.

- [Unified Execution #2892](unified-execution.md) and [#2912](https://github.com/elsa-workflows/elsa-foundation/issues/2912)
  retain production collaboration, durable decision/approval, budget and recovery obligations. Integrate
  that work without reparenting it, changing its current queue or treating completed research #2913 as
  production acceptance. Independent specialist catalog/design work can proceed; shared runtime work
  waits for or is delivered through the owning leaf.
- [Weaver #3428](weaver-across-studio.md) owns the assistant helping users work in Studio. This Program
  owns specialists participating in authored processes. Preserve existing assistant behavior and the
  single-active-provider default; justify reuse at the contract boundary.
- Connections, secrets, identity, knowledge, persistence, diagnostics and BPMN retain their domain
  authority. Elsa owns the enclosing process; a selected team runtime owns bounded internal coordination.
  Checkpoint, retry and external-effect ownership must be explicit before integration.

Releases, deployment, provider purchases and publisher activation need separate authority. Use an
authorized connection for live qualification; report missing access specifically. Deterministic,
provider-protocol and live-model evidence remain distinct.

## Delivery plan

The Program issue owns the full task/checklist and finite A1–A15 acceptance matrix. These milestones
are the roadmap; each next implementation leaf is elaborated when its inputs are concrete.

| Milestone | Outcome | Gate |
|---|---|---|
| [M0 #3520](https://github.com/elsa-workflows/elsa-foundation/issues/3520) | Establishment and pinned research spikes | Reproducible SDK probes and current-source integration matrix; explicit limitations |
| [M1 #3521](https://github.com/elsa-workflows/elsa-foundation/issues/3521) | Product, UX and architecture design | Reviewed PRD, journeys/wireframes, state transitions, decisions and implementation specs/tasks |
| [M2 #3522](https://github.com/elsa-workflows/elsa-foundation/issues/3522) | Catalog and immutable publication | Authorized drafts/versions, generated activity contracts and packaged-consumer proof |
| [M3 #3523](https://github.com/elsa-workflows/elsa-foundation/issues/3523) | Specialist execution | Validated results, capability-aware model connections, optional continuity and recovery |
| [M4 #3524](https://github.com/elsa-workflows/elsa-foundation/issues/3524) | Native and BPMN composition | Editorial process on both surfaces, exact references and correct parent continuation |
| [M5 #3525](https://github.com/elsa-workflows/elsa-foundation/issues/3525) | Studio and evaluations | Real create/test/publish/use/inspect journeys, saved cases and version comparison |
| [M6 #3526](https://github.com/elsa-workflows/elsa-foundation/issues/3526) | Tools, knowledge and bounded teams | Authorized tool effects, scoped context, exact approvals, budgets and team lifecycle |
| [M7 #3527](https://github.com/elsa-workflows/elsa-foundation/issues/3527) | Integrated qualification | Complete acceptance, supported provider/store/host evidence, docs, reviewed CI and resulting main |

M0 precedes M1 decisions and production contract freeze. The default delivery order is M0–M7, with
native blocking dependencies between milestones. M1 wireframes may cover M5 before its implementation;
M6 shared collaboration work also respects #2912's prerequisites and owning lane. Pattern-based team
configuration is the initial designer; a bespoke free-form graph needs evidence of necessity.

## Active objectives

1. M0 establishment and research are complete on the recorded main `45b5347` receipts.
2. M1 design is complete through [PR #3540](https://github.com/elsa-workflows/elsa-foundation/pull/3540)
   and resulting-main core/Studio qualification on `e26c11bd9`.
3. M2 publication/runtime kernel [#3548](https://github.com/elsa-workflows/elsa-foundation/issues/3548)
   is complete through [PR #3573](https://github.com/elsa-workflows/elsa-foundation/pull/3573), with
   exact-head CI and resulting-main source/package/core/Studio qualification on `0d6db68a3`.
   Its bounded profile establishes no production durability or live-model acceptance. Draft discovery
   #3541 merged through #3543; central MEAI enabler #3544/#3546 merged at `fd304319c`.
4. M3 [#3523](https://github.com/elsa-workflows/elsa-foundation/issues/3523) is the active implementation
   unit for T016–T023. Provider research #3592 is complete; concrete providers and durable continuity
   remain implementation obligations. Independent durable Connections prerequisite
   [#3578](https://github.com/elsa-workflows/elsa-foundation/issues/3578) implements T014–T015 under
   existing owner #2854 is complete through PR #3591 and source/package/core/Studio qualification
   on resulting main `5be59cebc`. M3 draft PR #3594 integrates that main and adds concrete model
   access, protected turn/conversation journals, physical request reservations, explicit native
   continuation, finite generation-fenced leases and authenticated caller-lowered execution bounds. These are draft increments with separate source,
   provider and bounded package receipts on the owning issue; whole-M3 delivery still requires stable
   operation/new-delivery binding, native cancellation/applied settlement, retention/compatibility,
   streaming and complete support qualification.

The initial umbrella research is split into the SDK/source-compatibility spike #3529 and
[publication/lifecycle witness #3533](https://github.com/elsa-workflows/elsa-foundation/issues/3533).
The latter preserves the remaining M0 gates; SDK success alone cannot advance M1 or freeze contracts.

[M0 publication/lifecycle PR #3537](https://github.com/elsa-workflows/elsa-foundation/pull/3537)
merged with the bounded research disposition: specialist/native packages are Design-free; existing
BPMN package coupling remains a production obligation. M1 must resolve the package split before
contract freeze, and M4 must implement/qualify it. Resulting-main qualification remains distinct from
the merged research receipt.

M0 is complete after fresh SDK/package/lifecycle receipts and core/Studio CI on main `45b5347`.
[Dependency enabler #3544](https://github.com/elsa-workflows/elsa-foundation/issues/3544) promotes the
normal MEAI 10.10.1 graph while the remaining specialist runtime/package work stays open.

[M1 design leaf #3538](https://github.com/elsa-workflows/elsa-foundation/issues/3538) delivers
[spec 1179](../../specs/1179-specialist-agents/spec.md), its reviewed architecture/state/support/UX
packet and ordered M2–M7 tasks through [PR #3540](https://github.com/elsa-workflows/elsa-foundation/pull/3540).
The design is approved to implement; neither the packet nor the illustrative wireframe establishes
implemented product acceptance. Public signatures remain proposed until their first implementing slice.
[M2 draft discovery #3541](https://github.com/elsa-workflows/elsa-foundation/issues/3541) merged through
[PR #3543](https://github.com/elsa-workflows/elsa-foundation/pull/3543). Native specialist kernel #3548
is complete; #3523 is the sole active implementation unit and #3578 is complete.

## Completion

Published Copywriter and Proofreader execute a bounded editorial process with human approval through
native and BPMN workflows using an authorized real LLM. Studio supports authoring, testing, evaluation,
publication and linked run inspection. Explicit continuity, scoped tools/knowledge and bounded teams have
the accepted lifecycle/recovery evidence. Supported stores and normal packaged/custom hosts are qualified.
Documentation, meaningful tests, independent reviews, exact-head CI and resulting-main verification close
the finite acceptance matrix. Setup, prototypes, doubles and issue status alone cannot close the Program.
