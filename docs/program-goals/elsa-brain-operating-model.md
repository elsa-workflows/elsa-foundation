# Elsa Brain Operating Model

Status: active.

Area: repository operating model / AI workspace.

Steward(s): Joey plus active architects/agents.

## Purpose

Turn `elsa-foundation` into the transitional Elsa brain: a provider-neutral, navigable, amendable workspace that carries the main Elsa domain core libraries, foundation implementations, Speckit specs, maps, glossary, skills, reports, and architecture knowledge needed to guide the modular refactor.

This goal does not mean every session must repeat the full program overview. Use the drift guard in [AGENTS.md](../../AGENTS.md#program-goals-and-drift-guard) only when a trigger is present.

## In Scope

- Provider-neutral entrypoint and source-of-truth layering.
- Glossary and reference surfaces for reusable architecture knowledge.
- Executable skills that agents and engineers can follow.
- Generated maps and extension-point catalogs for repo navigation.
- Reports for unfinished work, findings, draft decisions, and current gaps.
- Codebase reality checks for constitution compliance, test maturity, weak/stub implementations, dependency maps, and extension points.
- Feature composition readiness, including Feature Composition Explorer and future CShells appsettings generation.
- Workspace split readiness for future extraction of feature-development assets into `elsa-workspace`.

## Out Of Scope

- Using `AGENTS.md` as a mutable roadmap or goal registry.
- Treating provisional reports as ratified constitution gates.
- Implementing the CShells Appsettings Generator before feature/dependency/settings conventions are approved.
- Treating `src/Server` as canonical shell composition policy.

## Active Objectives

1. Keep `AGENTS.md` as the provider-neutral front door and stable routing layer.
2. Preserve source-of-truth boundaries: constitution for gates, glossary for meanings, skills for workflows, maps for facts, reports for findings/open work, specs for feature/work-unit detail.
3. Keep workflows amendable until architecture work proves them.
4. Make unfinished work and next-step selection visible without forcing every session through a large zoom-out ritual.
5. Keep feature composition work useful but subordinate to the broader Elsa-brain operating model until alignment is reviewed.

## Linked Surfaces

- [AGENTS.md](../../AGENTS.md)
- [Skills catalog](../skills/catalog.md)
- [Unfinished work](../reports/unfinished-work.md)
- [Knowledge inventory](../reports/knowledge-inventory.md)
- [Skills stabilization audit](../reports/skills-stabilization-audit.md)
- [CShells composition evidence](../reports/cshells-composition-evidence.md)
- [Maps index](../maps/README.md)
- [Architecture tour](../architecture-tour.md)
- [Root glossary](../glossary/root.md)
- [Elsa glossary](../glossary/elsa.md)

## Current Roadmap Note

Temporary correction: avoid drifting too deeply into CShells composition work before the broader Elsa-brain program is balanced. Prioritize these next units unless the user explicitly redirects:

1. Program Alignment Review: compare current repo surfaces against the original Elsa-brain intent; identify over-invested, under-invested, and appropriately provisional areas; recommend the next 3-5 work units.
2. Architecture Tour Review: verify that `docs/architecture-tour.md` gives a concise orientation to the repo, core systems, and where to look next without duplicating glossary or constitution material.
3. Glossary Coverage Audit: check whether key architecture terms are centralized in `docs/glossary/` and not re-explained across skills, reports, maps, or constitutions.
4. What's Next / Unfinished Work Re-ranking: re-rank unfinished work by Elsa-brain milestone rather than recent-topic momentum.
5. Return to CShells composition only after the broader alignment is checked: use the provisional classification report as input, but do not keep refining generator-specific taxonomy until the overall brain surfaces are balanced.

Remove this roadmap note after the alignment steps are completed and captured in the normal source-of-truth layers.

## Drift / Review Notes

- Program goals are buckets, not permanent doctrine.
- Additional architects or domain experts may create separate goal files for their active areas.
- If a thread reaches a third consecutive plan/work unit in one topic, use the Program Goal Drift Review skill before continuing.
- If work no longer fits this bucket, update or split the program goal instead of silently stretching it.
