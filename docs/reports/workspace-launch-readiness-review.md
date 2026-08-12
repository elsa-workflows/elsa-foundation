# Workspace Launch Readiness Review

Status: point-in-time review for deciding whether `elsa-foundation` is ready to receive its first new architects and engineers.

## Purpose

Assess whether the workspace is ready for first-user handoff after the Elsa foundation workspace operating model was split into focused program-goal buckets.

This report does not create architecture rules and does not choose the next implementation unit. It records launch-readiness findings so handoff work can continue through [Workspace Launch Readiness](../program-goals/workspace-launch-readiness.md) and hard architecture/code work can start in focused buckets.

## Inputs Reviewed

- [AGENTS.md](../../AGENTS.md)
- [README](../../README.md)
- [Docs index](../README.md)
- [Architecture tour](../architecture-tour.md)
- [Skills catalog](../skills/catalog.md)
- [Program goals index](../program-goals/README.md)
- [Agent maturity audit](agent-maturity-audit.md)
- [Architecture tour review](architecture-tour-review.md)
- [Unfinished work](unfinished-work.md)
- [Maps manifest](../maps/manifest.json)

## Verdict

The workspace is ready to receive its first new users for constrained architecture work, especially if the first handoff names:

- the active program-goal bucket,
- the primary skill,
- the primary report or spec,
- the constitution gates to treat as quality gates,
- the map freshness stance,
- and the expected stop point.

It is not ready for an unconstrained prompt such as "understand the whole repo and start fixing things." That would still invite over-reading, local cleanup loops, and accidental treatment of draft material as ratified doctrine.

## Findings

| Finding | Classification | Launch impact | Route |
|---|---|---|---|
| The entry path is coherent: `README.md` -> `AGENTS.md` / docs index -> architecture tour -> skills / program goals / reports. | Strength | New users have a navigable start path. | Keep under [Workspace Launch Readiness](../program-goals/workspace-launch-readiness.md). |
| Program-goal buckets now separate launch readiness, runtime execution, constitution readiness, code/test reality, feature composition, and workspace split readiness. | Strength | New users can pick a focused bucket instead of continuing broad foundation-workspace polishing. | [Program goals index](../program-goals/README.md). |
| The Architecture Tour is short and correctly routes readers to deeper surfaces. | Strength | It can be the first orientation skill without becoming a second constitution. | [Architecture tour](../architecture-tour.md). |
| Reports are good evidence surfaces, especially runtime handoff, test maturity, CShells evidence, unfinished work, and agent maturity. | Strength | First workers can start from evidence rather than chat memory. | Reports index plus the relevant bucket. |
| Maps are not safe as fresh verification facts when freshness is uncertain. (As written, this cited the manifest's `relevant_inputs_dirty: true`; #1278 removed that field, since it recorded the working tree at generation time and meant nothing once committed. `Elsa.Maps.Generator -- check` is the freshness signal.) | Launch caution | When a thread invokes a map, run the check and refresh the relevant map layer if it is red, reviewing generated findings before continuing. | [Workspace Launch Readiness](../program-goals/workspace-launch-readiness.md) or the selected hard-work bucket. |
| Constitutions remain draft/provisional in places. | Launch caution | Warn users when draft status matters. If they want to focus on unratified items, start a targeted ratification work unit with the available skills and guardrails. | [Constitution Readiness](../program-goals/constitution-readiness.md). |
| The repo is ready for a hard next unit, not more broad operating-model grooming. | Drift risk | Launch prep should now route toward specific buckets such as Runtime Execution Seam or Code Reality And Test Maturity. | [Program goals index](../program-goals/README.md). |

## First-User Start Path

Recommended path for a new architect:

1. Read [README](../../README.md), then [AGENTS.md](../../AGENTS.md).
2. Read [Architecture tour](../architecture-tour.md).
3. Open [Program goals index](../program-goals/README.md) and select the relevant bucket.
4. Use [Skills catalog](../skills/catalog.md) to choose the workflow.
5. Read the one primary report/spec linked by the selected bucket.
6. Run `dotnet run --project tools/maps/Elsa.Maps.Generator -- check` before relying on generated maps; see the [maps index](../maps/README.md#freshness).
7. Stop at the requested artifact: report, work-unit plan, Speckit spec, or implementation.

## Launch Blockers

No hard blocker prevents a first architect from starting constrained work.

Soft blockers to name in handoffs:

- map freshness must be checked whenever maps are invoked; dirty or uncertain inputs mean refreshing the relevant map and reviewing generated findings before continuing;
- constitution text is still draft and users should be warned when that matters; ratification work should be targeted and skill-guided;
- large work needs a reviewed handoff prompt, not a vague repo-wide instruction.

## Recommended Next Launch Actions

1. Use [Workspace Launch Readiness](../program-goals/workspace-launch-readiness.md) for first-user handoff checks.
2. Use [Runtime Execution Seam](../program-goals/runtime-execution-seam.md) for tomorrow's incoming runtime architect.
3. Use [Constitution Readiness](../program-goals/constitution-readiness.md), [Code Reality And Test Maturity](../program-goals/code-reality-and-test-maturity.md), [Feature Composition Readiness](../program-goals/feature-composition-readiness.md), and [Workspace Split Readiness](../program-goals/workspace-split-readiness.md) for the remaining launch work.
4. Do not add more broad objectives to [Elsa Foundation Operating Model](../program-goals/elsa-foundation-operating-model.md) unless the routing layer itself breaks.
5. Offer new users simple prompt options from [First-user prompt options](../reference/first-user-prompts.md) so they can choose orientation, workspace mechanics, or a hard next unit without guessing.
