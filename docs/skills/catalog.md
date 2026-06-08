# Provider-Neutral Skill Catalog

This catalog defines the workflows agents and engineers should follow. Provider-specific skill folders may wrap these workflows, but this file is the canonical catalog.

## Architecture Tour

**Use when:** a user asks for orientation, "what is this repo?", or a concise explanation of the architecture.

**Workflow:** read [../architecture-tour.md](../architecture-tour.md), then follow links only for terms or systems the user asks about.

**Output:** a short guided tour with next places to inspect.

## Critical Constitution Review

**Use when:** a user wants to question, revise, ratify, or challenge constitution sections.

**Workflow:** identify the target section; separate enforceable gate from explanation/history; compare against glossary terms and current code reality; list ambiguity, contradictions, and missing exceptions; propose a work unit when changes are needed.

**Output:** findings first, then proposed revision path. Do not silently rewrite constitutional meaning.

## Verify Codebase Against Constitution

**Use when:** a user asks whether the codebase complies with the constitution.

**Workflow:** choose the relevant gates; inspect project references, extension-point catalogs, package references, tests, and docs; report mismatches with file references; classify each finding as code drift, doc drift, missing test, or unclear gate.

**Output:** a verification report that can become a work unit.

## What's Next / Unfinished Work

**Use when:** a user asks what is unfinished, unratified, weakly implemented, or ready for the next work unit.

**Workflow:** read the program goal and zoom-out rule in [../../AGENTS.md](../../AGENTS.md#program-goal-and-zoom-out-rule), then read [../reports/unfinished-work.md](../reports/unfinished-work.md); refresh with searches for `TODO`, `DEFERRED`, `pending`, `stub`, `placeholder`, and specs marked superseded or retained-for-intent. Rank candidates by which Elsa-brain milestone they advance, not only by which local file looks unfinished.

**Output:** prioritized list of next candidates, with whether each is architecture, docs, tests, or code, and the Elsa-brain milestone each candidate advances.

## Feature Composition Explorer

**Use when:** a user wants to compose an API/shell from selected Elsa features.

**Workflow:** identify required capabilities; map them to projects/features; include dependencies; flag external package/version compatibility concerns; propose a minimal shell composition before generating config.

**Output:** selected feature set, dependency rationale, missing decisions, and config-generation inputs.

## CShells Appsettings Generator

**Use when:** a user has selected features and wants appsettings JSON for a CShells/Nuplane shell.

**Workflow:** scan feature activation patterns and dependencies; generate JSON from selected features; include required dependencies; mark optional features separately.

**Output:** appsettings JSON and a note on how it was derived. Do not guess feature IDs when the repo does not expose them.

## Feature/Dependency Map Builder

**Use when:** a user asks for project, package, feature, or external NuGet dependency maps.

**Workflow:** parse `.csproj` files for `ProjectReference` and `PackageReference`; record package ID/version; group by domain; flag direct version clusters and reference signals.

**Output:** generated map/report under `docs/maps/` or `docs/reports/`, depending on whether it is stable navigation or a point-in-time finding.

## Glossary Lookup

**Use when:** a term must be understood before continuing.

**Workflow:** check [../glossary/root.md](../glossary/root.md), then [../glossary/elsa.md](../glossary/elsa.md), then worked references.

**Output:** the relevant meaning in this architecture, not a generic definition.

## Work Unit Planner

**Use when:** findings need to become Speckit-ready architecture or feature work.

**Workflow:** start with the zoom-out rule in [../../AGENTS.md](../../AGENTS.md#program-goal-and-zoom-out-rule); define goal, success criteria, in/out of scope, affected gates/docs/maps, tests, and review points; choose whether it is feature development or architecture development.

**Output:** a concise work-unit plan ready for `speckit-specify` or architecture review, including the program milestone the work advances and the reason it is not merely local polish.

## Speckit Flow Guide

**Use when:** a user asks to create, plan, task, or implement a feature/work unit.

**Workflow:** use the official flow: create/switch feature branch through the Speckit git extension, run `speckit-specify`, review, run `speckit-plan`, review, run `speckit-tasks`, then `speckit-implement`.

**Output:** specs under `specs/NNN-feature-name/`, branch-aligned work, and constitution-gated implementation.

**Future extraction note:** this flow remains in `elsa-foundation` until feature-development workspace assets move to `elsa-workspace`.
