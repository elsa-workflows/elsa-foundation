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
- Treating `src/Apps/Elsa.Server` as canonical shell composition policy.

## Active Objectives

1. Keep `AGENTS.md` as the AI-provider-neutral front door and stable routing layer.
2. Preserve source-of-truth boundaries: constitution for gates, glossary for meanings, skills for workflows, maps for facts, reports for findings/open work, specs for feature/work-unit detail.
3. Keep workflows amendable until architecture work proves them.
4. Keep this bucket's active planned work aligned with the shared program-goals planner while using reports as evidence and inventory.
5. Redirect new hard work to focused program-goal buckets instead of continuing broad Elsa Brain polishing.

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
- Current loose findings and concerns live in [unfinished work](../reports/unfinished-work.md) as inventory, not as the active queue.

Use this goal file for planned work in the Elsa Brain Operating Model bucket. Use [unfinished work](../reports/unfinished-work.md) as an inventory of findings and concerns; when an inventory item becomes planned durable work for this operating-model effort, add or move it here before implementation.

## Bucket Items

- Active: keep the shared routing layer stable and prevent broad operating-model polishing from becoming the default next-work bucket.
- Completed: earlier cleanup reframed `unfinished-work.md` as inventory rather than an active queue.
- Completed: targeted constitution thinning removed safe examples, rationale pointers, provenance notes, and stale boundary wording from the gate files; further thinning should wait for targeted ratification/runtime-design work.
- Moved out: first-user handoff and launch-readiness work lives in [Workspace Launch Readiness](workspace-launch-readiness.md).
- Moved out: runtime execution planning lives in [Runtime Execution Seam](runtime-execution-seam.md).
- Moved out: targeted constitution review lives in [Constitution Readiness](constitution-readiness.md).
- Moved out: code/test reality work lives in [Code Reality And Test Maturity](code-reality-and-test-maturity.md).
- Moved out: feature composition and CShells readiness lives in [Feature Composition Readiness](feature-composition-readiness.md).
- Moved out: future `elsa-workspace` extraction readiness lives in [Workspace Split Readiness](workspace-split-readiness.md).

## Drift / Review Notes

- Program goals are buckets, not permanent doctrine.
- Additional architects or domain experts may create separate goal files for their active areas.
- If a thread reaches a third consecutive plan/work unit in one topic, use the Program Goal Drift Review skill before continuing.
- If work no longer fits this bucket, update or split the program goal instead of silently stretching it.
- Treat this bucket as the stable routing substrate. New hard work should normally select one of the focused buckets above.
