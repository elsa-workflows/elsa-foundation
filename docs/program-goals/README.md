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
| [Workspace Launch Readiness](workspace-launch-readiness.md) | Active | First-user handoff / repository launch preparation | Joey plus active architects/agents | Verify the repo can receive first users through tour, skills, reports, and buckets |
| [Elsa Foundation Operating Model](elsa-foundation-operating-model.md) | Active | Repository operating model / AI workspace | Joey plus active architects/agents | Keep the shared routing layer stable; do not use this as the default next-work bucket |
| [Runtime Execution Seam](runtime-execution-seam.md) | Active | Workflows Runtime architecture / executable artifact seam | Joey plus the incoming runtime architect | Prepare the Runtime execution seam for architect-owned Speckit planning |
| [Groundwork Persistence Readiness](groundwork-persistence-readiness.md) | Completed | Provider-neutral persistence framework / Elsa validation bridge | Joey plus active architects/agents | Foundation extracted and validated; remaining adoption work moved to the Zero-EF successor goal |
| [Zero-EF Persistence](zero-ef-persistence.md) | Active | Elsa persistence-provider consolidation / Groundwork adoption | Sipke plus active architects/agents | Close Groundwork dependencies, migrate every Elsa persistence family, and remove EF Core from this repository |
| [Constitution Readiness](constitution-readiness.md) | Active | Targeted constitution review / ratification readiness | Joey plus active architects/agents | Review only launch-blocking or work-unit-specific gates |
| [Code Reality And Test Maturity](code-reality-and-test-maturity.md) | Active | Codebase verification / tests / weak implementations | Joey plus active engineers/agents | Route hard code/test verification findings into focused units |
| [Feature Composition Readiness](feature-composition-readiness.md) | Active | Feature composition / CShells and Nuplane shell readiness | Joey plus active architects/agents | Classify bounded feature/settings slices before generator work |
| [Workspace Split Readiness](workspace-split-readiness.md) | Active | Future `elsa-workspace` extraction / portable feature-development flow | Joey plus active architects/agents | Keep feature-development flows portable without blocking launch |
| [Diagnostics Observability Readiness](diagnostics-observability-readiness.md) | Active | Diagnostics observability port (structured logs + OpenTelemetry) across foundation + studio | Joey plus active architects/agents | Port structured logs and OTEL to foundation architecture (EFCore persistence, studio bottom-panel tabs) |
| [Elsa 4 Architecture Review Remediation](elsa-4-review-remediation.md) | Active | Cross-domain remediation of the 2026-07 review findings (W1–W21) | Sipke plus active architects/agents | Phase 0 first wave (W2/W3/W4/W6); W1/W5 held for specs/083 Move 2 |
| [BPMN Engine](bpmn-engine.md) | Active | Executable BPMN 2.0 engine + interchange + the runtime seams it consumes | Sipke plus the BPMN control-room session | Phase 3: specs 123–128 merged; next #989 runtime fix (un-gates error event subprocesses), then call activity / collaborations |
| [First-Request / Cold-Start Readiness](first-request-cold-start-readiness.md) | Active | Host boot / first-request latency (engine perf phase 4, track 2) | Sipke plus active performance/runtime agents | Unit 1 instrument (spec 129) landing; then R2R, schema skip-if-current, eager activation, warmups |

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
