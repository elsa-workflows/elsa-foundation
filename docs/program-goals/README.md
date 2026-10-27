# Program Goals

Program goals are mid-term buckets of related short-term objectives. They are coordination surfaces for architects, engineers, and agents; they are not quality gates, glossary definitions, generated facts, or task-runner instructions.

Use this directory to see which larger efforts are active, who is stewarding them, what work is currently in scope, and where the supporting reports/specs/branches live. Keep one file per goal bucket to reduce merge conflicts.

## Planner Role

This directory is the shared backlog and planner for durable work in `elsa-foundation`. Reports may surface findings, concerns, and candidates, but planned durable work should be added to, moved between, completed in, or dropped from the relevant program-goal bucket.

Use reports as evidence and inventory. When a report finding becomes planned work, select or create the right bucket, add the item there, and update the bucket when the work is done. If the work should not become part of a named mid-term goal, mark the program-goal state as `none/free-flow` instead of inventing a bucket.

## Program Goal State

Before substantial planning, "what next" ranking, roadmap/drift review, or multi-session handoff, identify the current program goal state when it is unclear.

Valid states:

- A named program-goal bucket from this directory.
- `none/free-flow`: no active program goal; the user is exploring, developing, researching, or planning without a mid-term coordination bucket.
- `unknown/not-assessed`: temporary state before the agent has enough context to decide whether a named bucket or `none/free-flow` applies.

Do not invent a named program-goal bucket just because one is missing. Propose creating or selecting a bucket only when the work is forming a mid-term coordination surface that would help future agents, architects, or engineers. Otherwise keep the state `none/free-flow` and let the normal source-of-truth layer carry the result.

## Registry

| Goal | Status | Area | Steward(s) | Current focus |
|---|---|---|---|---|
| [Foundation/Studio Consolidation](https://github.com/elsa-workflows/elsa-foundation/issues/2501) | Active; priority one | Essentials, Extensions and Studio in Foundation | Sipke | Isolated rehearsal and qualification under #2510 from captured copies; live cutover remains gated |
| [Elsa 4 Website and Documentation](https://github.com/elsa-workflows/elsa-foundation/issues/2495) | Active in parallel | Foundation-owned documentation and GitBook | Sipke | #2498 source setup and hosted qualification; preserve Elsa 3 publishing |
| [Elsa 4 Simplification](elsa-4-simplification.md) | Active | Whole product: tests, API, language, structure, docs | Sipke | Program #2559, ADR 0080 |
| [Runtime Efficiency](runtime-efficiency.md) | Active | Runtime database access, persistence modes, concurrent correctness | Sipke | Program #2531; persistence-mode epic #2564 |
| [Unified Execution](unified-execution.md) | Active; owner authorization, 18 October | Native workflows, BPMN, ETL, hosting and agent collaboration | Sipke | Program #2892; lifecycle compatibility under #2905, released BPMN adoption under #2735 |
| [Native Compensation and Saga Recovery](native-compensation.md) | Active; owner authorization, 19 October | Native workflow compensation and integrated Studio authoring/inspection | Sipke | Program #3035; design packet #3043, then the M1 journey #3044 |
| [BPMN Engine](bpmn-engine.md) | Active; owner exception, 14 October | BPMN composite, interchange, shared `valence-works/bpmn` core | Sipke | #2735 switch to `Bpmn.Semantics`; #2736 refuse degraded imports |
| [Elsa 3.8/3.9 Parity](elsa3-parity.md) | Active; owner exception, 15 October | Identity administration, then tenancy, Elsa 3 import, operations, modules | Sipke | Program #2771, ADR 0085; Phase 1 users and roles |
| [Studio Expression Developer Experience](studio-expression-developer-experience.md) | Active; owner authorization, 23 October | Studio activity-input code authoring and Foundation expression tooling | Sipke | Program #2310; plan #2367 and the permission-hash fix first; milestones 2 and 3 wait on the #3204 read-model ADR |
| [Studio Hosting and Package Reuse](studio-hosting-package-reuse.md) | Active; owner authorization, 25 October | Reusable Studio hosting, optional capabilities and independent NuGet consumption | Sipke | Program #3340 and Project 63; settle reuse and browser-asset delivery before extraction; runs beside the active programs |
| [Unified Workflow Authoring and Execution](unified-workflow-workspace.md) | Delivered; completion receipts on #3392 | Studio workflow editor, execution inspection and Test Run interaction | Sipke | Program #3392; attached Test Runs, shared exact inspection, following, confirmed motion and integrated qualification |

The Active table above is the complete list of active programs. The
[19 October owner amendment to ADR 0080 D0](../adr/0080-elsa-4-simplification-decisions.md#d0--focus) replaced the
former two-program cap and its run of named exceptions (11, 14, 15 and 18 October). A program is active only
when an owner amendment there names it and states its sequencing. Each authorization covers only its named
scope and reactivates nothing else; linked issues remain the charters and decision records. The goals below
retain their own status and decisions, including the 2026-11-21 review for parked work unless separately
superseded.

| Goal | Status |
|---|---|
| [Elsa 4 Contributor Experience](workspace-launch-readiness.md) | Parked |
| [Elsa Foundation Operating Model](elsa-foundation-operating-model.md) | Parked |
| [Runtime Execution Seam](runtime-execution-seam.md) | Historical objectives parked; named #2892 continuation active |
| [Runtime Database Access](runtime-db-access.md) | Merged into Runtime Efficiency |
| [Runtime Throughput](runtime-throughput.md) | Merged into Runtime Efficiency |
| [Runtime Execution Evidence](runtime-execution-evidence.md) | Parked |
| [Workflow Incident Troubleshooting](incident-troubleshooting.md) | Delivered |
| [Runtime Alterations](runtime-alterations.md) | Parked |
| [Groundwork Persistence Readiness](groundwork-persistence-readiness.md) | Completed |
| [EF Core Persistence](ef-core-persistence.md) | Completed |
| [Zero-EF Persistence](zero-ef-persistence.md) | Superseded |
| [Constitution Readiness](constitution-readiness.md) | Parked |
| [Code Reality And Test Maturity](code-reality-and-test-maturity.md) | Folded into Elsa 4 Simplification |
| [Modular Hosting Upstream Delivery](modular-hosting-upstream-delivery.md) | Parked |
| [Runtime Composition & Configuration](feature-composition-readiness.md) | Parked |
| [First-party REST API Consolidation](first-party-rest-api-consolidation.md) | Parked |
| [Workspace Split Readiness](workspace-split-readiness.md) | Parked |
| [Diagnostics Observability Readiness](diagnostics-observability-readiness.md) | Parked |
| [Elsa 4 Architecture Review Remediation](elsa-4-review-remediation.md) | Folded into Elsa 4 Simplification |
| [First-Request / Cold-Start Readiness](first-request-cold-start-readiness.md) | Superseded |

## Goal File Rules

- Keep program-goal files concise and amendable.
- Record active objectives and roadmap notes here instead of in `AGENTS.md`.
- Treat short-term roadmap notes as temporary coordination aids. When the units they name are implemented or captured in their normal source-of-truth layers, check the result against the program goal and remove, replace, or mark completed short-term objectives.
- Move items between buckets when a better owner emerges. Drop items when carrying them no longer helps; if the concern becomes important later, it can be rediscovered from reports, source evidence, or user intent.
- Link to reports, specs, maps, branches, and PRs instead of copying their content.
- If a goal becomes a ratified gate, move the gate to the constitution and leave a link here.
- If a goal becomes a repeatable workflow, move the workflow to `docs/skills/catalog.md` and leave a link here.
- If a goal is completed, paused, or superseded, update its status rather than deleting history immediately.

## Suggested Goal File Shape

- Status
- Area
- Steward(s)
- Purpose
- In scope
- Out of scope
- Active objectives
- Linked surfaces
- Current roadmap notes
- Drift / review notes
- Removal or completion conditions
