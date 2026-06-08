# Elsa Brain Operating Model

Status: active.

Area: repository operating model / AI workspace.

Steward(s): Joey plus active architects/agents.

## Purpose

Turn `elsa-foundation` into the transitional Elsa brain: an AI-provider-neutral, navigable, amendable workspace that carries the main Elsa domain core libraries, foundation implementations, Speckit specs, maps, glossary, skills, reports, and architecture knowledge needed to guide the modular refactor.

This goal does not mean every session must repeat the full program overview. Use the drift guard in [AGENTS.md](../../AGENTS.md#program-goals-and-drift-guard) only when a trigger is present.

## In Scope

- AI-provider-neutral entrypoint and source-of-truth layering.
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

1. Keep `AGENTS.md` as the AI-provider-neutral front door and stable routing layer.
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

The temporary alignment sequence has been completed and captured in the normal source-of-truth layers:

- Program alignment is reflected in this goal file and [unfinished-work re-ranking](../reports/unfinished-work-reranking.md).
- Architecture-tour routing is captured in [architecture tour review](../reports/architecture-tour-review.md).
- Glossary coverage is captured in [glossary coverage audit](../reports/glossary-coverage-audit.md).
- The active "what next" priority view now lives in [unfinished work](../reports/unfinished-work.md).

Use [unfinished work](../reports/unfinished-work.md) for current next-step ranking. Do not keep completed short-term objectives in this goal file once their outputs are captured elsewhere.

## Drift / Review Notes

- Program goals are buckets, not permanent doctrine.
- Additional architects or domain experts may create separate goal files for their active areas.
- If a thread reaches a third consecutive plan/work unit in one topic, use the Program Goal Drift Review skill before continuing.
- If work no longer fits this bucket, update or split the program goal instead of silently stretching it.
