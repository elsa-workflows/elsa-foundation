# Workspace Split Readiness

Status: active.

Area: future `elsa-workspace` extraction / portable feature-development flow.

Steward(s): Joey plus active architects/agents.

## Purpose

Keep `elsa-foundation` ready for the future extraction of feature-development workspace assets into a dedicated `elsa-workspace` repository.

This bucket exists so feature-development flows remain explicit and portable while foundation architecture work continues.

## In Scope

- Identifying which workflows, specs, maps, skills, and setup assumptions must remain portable.
- Keeping feature-development flow guidance explicit rather than buried in foundation-specific docs.
- Tracking extraction blockers and coupling risks between `elsa-foundation` and future `elsa-workspace`.
- Preserving Speckit feature/work-unit flow in a way that can move or be mirrored later.
- Documenting boundaries between Elsa brain architecture work and feature-development workspace work.

## Out Of Scope

- Performing the repository extraction now.
- Duplicating canonical architecture explanations in future workspace docs before the split exists.
- Rewriting feature specs only for hypothetical extraction.
- Moving foundation-only architecture reports into workspace-owned surfaces prematurely.

## Active Objectives

1. Keep feature-development and architecture-development task paths distinguishable.
2. Ensure Speckit flow guidance remains portable.
3. Track future extraction blockers as findings before turning them into work units.
4. Avoid making `elsa-foundation` launch readiness depend on the split happening first.

## Linked Surfaces

- [AGENTS.md](../../AGENTS.md)
- [Skills catalog](../skills/catalog.md)
- [Knowledge inventory](../reports/knowledge-inventory.md)
- [Unfinished work](../reports/unfinished-work.md)
- [Speckit specs](../../specs)
- [Maps index](../maps/README.md)

## Current Roadmap Notes

- The split is a future move, not a launch blocker for the first architect handoff.
- When feature-development docs or skills are updated, note whether the content is foundation-owned, workspace-portable, or likely workspace-owned after extraction.

## Drift / Review Notes

- Do not overfit current docs to a future repository shape before the split is planned.
- If extraction becomes active work, create a focused spec or work unit rather than stretching this bucket.

## Removal or Completion Conditions

Complete or pause this bucket when extraction readiness is either proven sufficient for current launch or moved into a dedicated extraction work unit.
