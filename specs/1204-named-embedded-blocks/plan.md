# Implementation Plan: Named embedded blocks

**Branch**: `agent/codex/3748-embedded-blocks-plan` | **Date**: 2026-11-03 | **Spec**: [spec.md](spec.md)
**Program**: [#3610](https://github.com/elsa-workflows/elsa-foundation/issues/3610) | **Kanban**: [Project 67](https://github.com/orgs/elsa-workflows/projects/67)

## Summary

Deliver **Block** creation, bounded extraction, nested editing, safe Ungroup and promotion to ordinary reusable Activity Graphs. Use existing Sequence, Flowchart and BPMN process composites. Introduce no Block runtime activity, private definition catalog or implicit isolation/retry/transaction/compensation boundary.

The owner approved this scope and autonomous end-to-end delivery on 3 November. Establishment persists the plan and issue hierarchy; a real-host prototype must prove the composition before production completion. The program remains open until every acceptance row has merged, exact-head and resulting-main evidence.

## Technical Context

- **Language/platform**: existing TypeScript/React Studio, .NET 10 authoring/runtime and current integrated BPMN editor.
- **Dependencies**: existing graph-authoring adapters, UI/SDK, authorized activity catalog, activity-owned structure contracts and Activity Graph lifecycle. No new third-party package or dependency on #3597.
- **Storage**: native authored structures plus existing layout and typed presentation sidecars; extend Activity Definition presentation and frozen-source handling additively. Supporting runtime causality/interchange changes require prototype evidence.
- **Testing**: Vitest/mounted components, affected complete .NET suites, relevant backend source journeys and actual composed Studio/Workbench browser runs.
- **Constraints**: preserve exact catalog pins, closed value scopes, causal ambiguity/iteration/branch checks, permissions, source authority and immutable historical material. Existing host save/autosave/conflict behavior remains authoritative.
- **Performance**: retain current asset budgets/modular loading; no benchmark program. Respect the build-slot queue and check machine load before interpreting timing failures.

## Durable delivery breakdown

Keep #3610 as the canonical request and **Program**, preserving its original success criteria. Native parent/sub-issue relationships own the hierarchy; Project 67 schedules it. The current organization has no native Spike type: S001 uses native Task plus `type:spike`. File Bugs only for reproduced defects.

| Epic | Features | Leaves, in dependency order |
|---|---|---|
| Design/prototype #3738 | Design/prototype #3742 | Establishment #3748 → prototype Spike #3749 |
| Authoring/extraction #3739 | Presentation #3743; native refactoring #3744; BPMN #3745 | Presentation #3750 and native transformations #3751 → shared commands #3752 → T004 BPMN in Feature #3745 |
| Reusable promotion #3740 | Promotion #3746 | Analysis #3754 → review/publish/replace #3755 |
| Integrated qualification #3741 | Acceptance #3747 | T007 composed/runtime qualification and delivery in Program #3610 |

[Tasks](tasks.md) links every issue, dependency and acceptance row. Each leaf has one winning claim and one scoped PR. Execute one implementation leaf at a time. New issues start needs-triage; board readiness never silently promotes an issue or bypasses the 20-ready cap. Reports/checkpoints stay on the existing issue/PR. The program record links the scheduling surface without duplicating requirements.

Run beside active work, displace none, retain consolidation's priority one and reactivate no parked backlog. Consume current runtime/composition contracts rather than start another execution-model program. Releases/deployment are excluded. Record owner activation in ADR 0080 D0 and the registry.

## Implementation changes

### Native organization and refactoring

- Add Create block, Extract into block, Open block, Ungroup and Promote actions through shared graph authoring; bind both resource hosts to existing lifecycle controls.
- Create asks for a non-empty name and body structure, defaulting to the enclosing supported structure. Resolve an exact authorized catalog version before applying. Names use existing presentation-field rules.
- Use a compact named node with body kind and child count; ordinary breadcrumbs, inspector and execution navigation expose the body. No Block marker or scheduler is required.
- Sequence extraction accepts actual contiguous selected siblings, preserving order and child identities. Add accessible range/multiple selection to Sequence.
- Flowchart extraction accepts a linear region with one entry, one normal exit, unconditional Done routing and no crossing join/branch policy. Redirect boundary edges and a selected start node to the composite; preserve surviving connection metadata. Refuse unsupported outcomes/control targets before mutation.
- BPMN extraction accepts a same-lane linear region of supported native bound tasks/ordinary subprocesses with unconditional flows. Bind a real nested BPMN process; retain internal elements/flows/ActivityNodes and synthesize plain internal start/end events. Refuse gateways, special events/boundaries/listeners, transactions, event subprocesses, compensation, unsupported loop/direct-model bindings and cross-lane regions.
- Preserve parent declarations, exact child versions, authorized references, settings and layout. Necessary reference changes use typed traversal, never arbitrary string replacement. Preserve inner layout and position the outer node at the selected region; inverse translation moves restored children relative to the current block position.
- Apply structure/layout/presentation as one history edit with coherent selection/scope/focus on Undo/Redo. Ungroup requires the inverse profile and no local declarations or boundary settings that would be lost.

### Presentation and lifecycle

- Extend typed node presentation to Activity Definition graph draft/version APIs, persistence and SDK implementation state, shared adapters/history and frozen execution-source presentation.
- Older documents default to an empty collection. Create, replace, clone, conflict-copy, publication and source-freeze paths retain it. Presentation stays outside provider manifests, executable material and behavior hashes, following the accepted presentation ADRs.
- Mutations honor resource authority/permissions. Read-only, source-owned, historical and unavailable-module contexts explain unavailable commands. Draft edits never mutate published/running versions.

### Safe reusable promotion

- Add an internal shared Studio planner over explicit references/expression parameters. Return source fingerprint, candidate schema-2 graph/public contract, complete call-site rewrite and located diagnostics. Existing backend validation remains authoritative; no parallel publication engine.
- Propose inputs for supported external reads, outputs for externally consumed child results, supported local declarations and explicit outcome mappings. Refuse opaque scripts, unsafe external writes, unpreservable evaluation timing, unsupported activities and changed Break/Complete targets.
- Show complete contract/binding review before creation. Use ordinary reusable draft creation, validation and publication; publication remains an explicit normal lifecycle action.
- Keep the source until a reviewed exact published target is available. Resume by selecting the target and recomputing the preview. Changed source/catalog/target requires renewed analysis/review. Replacement is one undoable source edit; Undo never deletes shared resources. No cross-resource transaction or automatic cleanup is assumed.

## Prototype and evidence gates

[Research](research.md) identifies two unproved prerequisites: composite completion may omit the child-result causal bridge, and BPMN XML does not retain arbitrary native task arguments/versions today. S001 must establish paired runtime results; T004 owns supported binding retention/refusal within the existing interchange boundary. A successful render is not proof.

The prototype uses an owned rebuilt composed host to demonstrate extraction, nested editing, rename, save/reload, Undo/Redo and execution. Compare parent variable reads/writes, outside-to-inside and inside-to-outside result references, repeated/loop occurrences, waits/resumption, cancellation and supported fault recovery. Preserve nearest-loop Break and nearest-graph Complete. If the design fails, revise the affected plan before shipping; never weaken causality, closed scopes or retention to make the example pass.

Production qualification repeats supported journeys on both resource hosts, light/dark themes, keyboard and narrow layouts; covers missing modules, permissions, source-owned/read-only/history, stale previews and failed publication; proves two independently bound promoted call sites and unchanged historical published/running versions. Audit BPMN native behavior and XML/DI/binding fidelity separately.

Run whole affected frontend/backend suites, relevant source end-to-end journeys, architecture guards and generated-map checks. Behavioral changes need red/green bite proofs and executed counts compared against inventory. Review exact PR head and required CI, then qualify resulting main before closing the leaf/eligible parents. Preserve skipped/failed/quarantined/unproved evidence distinctions.

## Constitution Check

Loaded framework v6.1.0 and Elsa v6.3.0 Gate indexes plus the contributing review checklist. Keep activity-specific children/connections/scheduling in owning modules, Design/Runtime separation and exact-version behavior. Implementations remain internal by default; public/API changes are limited to genuine additive presentation contracts and evidence-required supporting seams. No new project/package or kernel structural primitive is planned.

Framework §§2.1, 2.6, 2.21.1, 2.22 and 2.23.2 govern dependencies, preserved behavior, catalog updates and risk-based regressions. Existing public API tracking, architecture and map/tier gates apply when touched. Elsa §§E2.8/E2.9 and framework §2.24 remain draft/provisional; this work does not ratify them. Post-design review requests no exception.

## Project Structure

Work in existing Studio Workflows graph-authoring and Hosting SDK; existing Activities Design/Graph/Sequence/Flowchart/BPMN and Workflows value/runtime modules own relevant backend support. Use existing test projects/e2e hosts. [Data boundaries](data-model.md), [tasks](tasks.md) and [journeys](quickstart.md) accompany this plan; evidence-required supporting contracts are reviewed with their owning task before production acceptance.
