# Provider-Neutral Skill Catalog

This catalog defines the workflows agents and engineers should follow. Provider-specific skill folders may wrap these workflows, but this file is the canonical catalog.

## Skill Layer Rules

- Skills describe repeatable workflows: trigger, inputs, steps, and output.
- Skills link to constitutions for gates, glossary for meanings, maps for generated facts, reports for findings, and reference docs for rationale.
- Skills should not duplicate long concept explanations. If a workflow needs a term, use [Glossary Lookup](#glossary-lookup).
- Provider wrappers should stay thin and point back to this catalog.
- Plan first for decisions. Act as follow-through for obligations: before starting a new architecture-affecting unit, produce a plan/checklist and wait for approval; after an approved unit is underway, complete required tests, extension-point catalog updates, generated-map refreshes, and small docs follow-through without a second approval unless the follow-through changes architecture meaning.

The current skill audit lives in [skills-stabilization-audit.md](../reports/skills-stabilization-audit.md).

## Provider Wrapper Validation

Claude Elsa wrappers can be checked for drift with:

```powershell
tools/skills/validate-claude-wrapper-drift.ps1
```

The validation compares `.claude/skills/elsa-*/SKILL.md` against this catalog and `.specify/integrations/claude.manifest.json`, including manifest hashes and thin-wrapper metadata.

## Orientation And Review Skills

### Architecture Tour

**Use when:** a user asks for orientation, "what is this repo?", or a concise explanation of the architecture.

**Workflow:** read [../architecture-tour.md](../architecture-tour.md), then follow links only for terms or systems the user asks about.

**Output:** a short guided tour with next places to inspect.

### Glossary Lookup

**Use when:** a term must be understood before continuing.

**Workflow:** check [../glossary/root.md](../glossary/root.md), then [../glossary/elsa.md](../glossary/elsa.md), then worked references.

**Output:** the relevant meaning in this architecture, not a generic definition.

### Critical Constitution Review

**Use when:** a user wants to question, revise, ratify, or challenge constitution sections.

**Workflow:** identify the target section; separate enforceable gate from explanation/history; compare against glossary terms and current code reality; list ambiguity, contradictions, and missing exceptions; propose a work unit when changes are needed.

**Output:** findings first, then proposed revision path. Do not silently rewrite constitutional meaning.

### Verify Codebase Against Constitution

**Use when:** a user asks whether the codebase complies with the constitution.

**Workflow:** choose the relevant gates; inspect project references, extension-point catalogs, package references, tests, and docs; report mismatches with file references; classify each finding as code drift, doc drift, missing test, or unclear gate.

**Output:** a verification report that can become a work unit.

### What's Next / Unfinished Work

**Use when:** a user asks what is unfinished, unratified, weakly implemented, or ready for the next work unit.

**Workflow:** read the program goal and zoom-out rule in [../../AGENTS.md](../../AGENTS.md#program-goal-and-zoom-out-rule), then read [../reports/unfinished-work.md](../reports/unfinished-work.md); refresh with searches for `TODO`, `DEFERRED`, `pending`, `stub`, `placeholder`, and specs marked superseded or retained-for-intent. Rank candidates by which Elsa-brain milestone they advance, not only by which local file looks unfinished.

**Output:** prioritized list of next candidates, with whether each is architecture, docs, tests, or code, and the Elsa-brain milestone each candidate advances.

### Work Unit Planner

**Use when:** findings need to become Speckit-ready architecture or feature work.

**Workflow:** start with the zoom-out rule in [../../AGENTS.md](../../AGENTS.md#program-goal-and-zoom-out-rule); define goal, success criteria, in/out of scope, affected gates/docs/maps, tests, and review points; choose whether it is feature development or architecture development.

**Output:** a concise work-unit plan ready for `speckit-specify` or architecture review, including the program milestone the work advances and the reason it is not merely local polish.

### Source-of-Truth Audit

**Use when:** content may be in the wrong layer, duplicate concept explanations appear, constitution text needs thinning, or new knowledge/workflow/rule material needs a canonical home.

**Workflow:** read [../../AGENTS.md](../../AGENTS.md#source-of-truth-layers) and [../reports/knowledge-inventory.md](../reports/knowledge-inventory.md); classify content as gate, glossary term, reference explanation, report finding, generated map, skill workflow, spec, or code; identify duplication, drift, missing links, and proposed moves.

**Output:** findings and a proposed relocation/update plan. Do not change constitutional meaning while moving explanatory material.

### Create Agent Preference

**Use when:** a user states a stable personal workflow preference, asks to remember how they want agents to work, or wants a preference like the Git operating model without turning it into shared repo doctrine.

**Workflow:** read [../../AGENTS.md](../../AGENTS.md#personal-operating-preferences) and [../reference/agent-preferences.md](../reference/agent-preferences.md); classify the request as a personal selection, reusable preference catalog/template, or shared repo rule. If it is personal, create or update a short ignored file under `.agent-prefs/` and do not commit it. If the preference needs reusable options or examples, update the committed reference catalog/template first, then ask before recording the user's local selection when the value is ambiguous. Refuse to store secrets, credentials, environment variables, or project facts as agent preferences.

**Output:** local `.agent-prefs/<preference-name>.md` content or a proposed preference file, plus any committed reference/catalog updates needed to make the preference reusable. Never commit personal preference files.

### Initialize Agent Preferences

**Use when:** `.agent-prefs/` has no preference files other than `.gitkeep`, or a user asks to set up local agent preferences for the repository.

**Workflow:** read [../../AGENTS.md](../../AGENTS.md#personal-operating-preferences) and [../reference/agent-preferences.md](../reference/agent-preferences.md#expected-preference-files). Use the expected preference table to identify known local preference files and their ask triggers. Read [../reference/git-operating-models.md](../reference/git-operating-models.md) only if Git workflow setup is relevant. Ask a brief setup question for each missing preference needed by the current work, such as Git operating model before remote work or session execution model before substantial planning/multi-session work. Create only the selected ignored files under `.agent-prefs/`; leave skipped preferences unset. Use [Create Agent Preference](#create-agent-preference) for the actual file shape. Do not ask for preferences unrelated to the current task.

**Output:** created local `.agent-prefs/*.md` files, or a short note that the user chose to decide per session. Never commit personal preference files.

## Speckit And Work-Unit Skills

### Speckit Flow Guide

**Use when:** a user asks to create, plan, task, or implement a feature/work unit.

**Workflow:** use the official flow: create/switch feature branch through the Speckit git extension, run `speckit-specify`, review, run `speckit-plan`, review, run `speckit-tasks`, then `speckit-implement`.

**Output:** specs under `specs/NNN-feature-name/`, branch-aligned work, and constitution-gated implementation.

**Future extraction note:** this flow remains in `elsa-foundation` until feature-development workspace assets move to `elsa-workspace`.

### Promote Finding To Work Unit

**Use when:** a report finding, deferred decision, weak implementation, or review question needs to become planned work.

**Workflow:** read the source report and relevant constitution gates; classify the work as architecture development, codebase verification, feature development, docs/maps/skills work, or code; use [Work Unit Planner](#work-unit-planner) to define goal, success criteria, scope, source-of-truth layer, and review points; use [Speckit Flow Guide](#speckit-flow-guide) only after the user approves moving from plan to spec.

**Output:** a proposed work-unit plan with exact files or spec surfaces to create/update.

## Feature And Extension Implementation Skills

### Create Feature Or Module

**Use when:** a new feature/module is added, ported, or split from existing code.

**Workflow:** read framework gates for three-layer separation, naming, feature identity, provider decomposition, and unit tests; identify the owning domain and dependency envelope; choose `.Core`, helper, and implementation package shape; plan feature registration tests, implementation tests, docs, and extension-point catalog updates before coding. After the user approves the feature/module plan, treat required tests, catalog updates, and generated-map refreshes as normal completion work.

**Output:** feature/module implementation plan or Speckit-ready scope, including package placement, dependency rules, tests, and docs/catalog updates.

### Extend Feature By Inheritance

**Use when:** one feature must extend, decorate, or specialize another feature's registration pipeline.

**Workflow:** check framework feature-inheritance rules; verify the base feature is public, inheritable, and has virtual registration; plan the derived feature registration override, service replacement/addition behavior, dependency direction, and tests. After the user approves the inheritance plan, complete required tests and extension-point documentation as follow-through.

**Output:** inheritance-based extension plan or implementation checklist with registration and test expectations.

### Add Event Contribution

**Use when:** a feature contributes to a fan-in event or lifecycle contribution surface.

**Workflow:** check event/contribution gates and the owning domain's `EXTENSION_POINTS.md`; choose the correct contributor-interface kind (`Source`, `Contributor`, `PreProcessor`, `PostProcessor`, or action-named equivalent); ensure the owning feature has one aggregating event handler for that contribution purpose; verify the publisher-owned delivery strategy is Sequential when contributions are read back; check the publisher-owned dispatcher failure policy (`Throw immediately` or `Run all then throw aggregate` for failing gates); plan the contributor's subscriber-owned failure classification, noting current code gaps where dispatcher failure policies and handler failure classifications are not yet implemented. After the user approves the contribution plan, complete required tests, catalog updates, and map refreshes as follow-through.

**Output:** contribution plan or implementation checklist with contributor contract, handler, registration, docs, and tests.

### Add Independent Event Subscriber

**Use when:** a feature observes an event for auditing, cache invalidation, telemetry, notifications, or other non-fan-in behavior.

**Workflow:** confirm the behavior is not a fan-in contribution; inspect the event contract, publisher-owned delivery strategy, publisher-owned dispatcher failure policy, and subscriber-owned failure classification; plan the handler registration and tests. If the subscriber needs stronger delivery or dispatcher-failure semantics than the event provides, raise an architecture question and model a separate event phase rather than attaching the subscriber as-is. Note current code gaps where dispatcher failure policies and handler failure classifications are not yet implemented. After the user approves the subscriber plan, complete required tests and catalog updates as follow-through.

**Output:** subscriber plan or implementation checklist with event contract, handler behavior, registration, and tests.

### Add Replacement Contract Implementation

**Use when:** exactly one implementation should be active per application/runtime context.

**Workflow:** identify the replacement contract and its owning domain; ensure the implementation does not use contribution-style `IEnumerable<T>` semantics; plan conflict detection or prevention, registration behavior, extension-point catalog updates, and tests. After the user approves the replacement-contract plan, complete required tests, catalog updates, and map refreshes as follow-through.

**Output:** replacement implementation plan or checklist with conflict semantics and test expectations.

### Add Bridge Or Adapter

**Use when:** code must connect two seams, isolate a heavy/external dependency, or translate between phase-owned contracts.

**Workflow:** read the relevant glossary/reference material, especially [../seams.md](../seams.md); identify both sides' `.Core` contracts; keep the bridge/adapter outside the domains it connects; verify dependency direction; plan failure semantics, docs, extension-point catalog updates, and tests. After the user approves the bridge/adapter plan, complete required tests, catalog updates, and map refreshes as follow-through.

**Output:** bridge/adapter plan or checklist with owner package, dependencies, translated contracts, and tests.

### Add Extension-Point Catalog Entry

**Use when:** a project exposes, implements, or changes events, contributor interfaces, replacement contracts, feature inheritance points, or other extension points.

**Workflow:** update the owning project's `EXTENSION_POINTS.md`; tag known implementations as intra-domain default or cross-domain contribution where applicable; update README cross-domain contribution notes when the contributing feature lives elsewhere; refresh generated extension-point maps when inputs changed or freshness is uncertain.

**Output:** updated extension-point documentation and map-refresh recommendation.

## Testing Skills

### Add Feature Registration Tests

**Use when:** a feature class is created or its service registration changes.

**Workflow:** read framework unit-test gates; instantiate the feature registration path in a focused test; verify owned services are registered through contracts, required options/collaborators are present, and replacement/contribution registrations have the expected shape.

**Output:** focused registration tests that protect the feature's DI surface.

### Add Implementation Unit Tests

**Use when:** a logic-bearing implementation is created or changed.

**Workflow:** read framework unit-test gates; construct the class directly with stubbed dependencies; cover meaningful branches, failure paths, and infrastructure exception wrapping where applicable; do not rely on integration tests to satisfy unit-test obligations.

**Output:** branch-focused unit tests for the implementation's behavior.

## Maps And Composition Skills

### Feature/Dependency Map Builder

**Use when:** a user asks for project, package, feature, or external NuGet dependency maps.

**Workflow:** parse `.csproj` files for `ProjectReference` and `PackageReference`; record package ID/version; group by domain; flag direct version clusters and reference signals.

**Output:** generated map/report under `docs/maps/` or `docs/reports/`, depending on whether it is stable navigation or a point-in-time finding.

### Refresh Generated Maps

**Use when:** map inputs changed, map freshness is uncertain, or a workflow needs current navigation facts.

**Workflow:** check `docs/maps/manifest.json` if present; run the narrowest map generator that fits the task; use the split scripts from [../../AGENTS.md](../../AGENTS.md#refresh-generated-maps); record any findings in reports rather than hand-editing generated facts.

**Output:** refreshed map snapshots and any report-worthy findings.

### Feature Composition Explorer

**Use when:** a user wants to compose an API/shell from selected Elsa features.

**Workflow:** identify required capabilities; map them to projects/features; include dependencies; flag external package/version compatibility concerns; propose a minimal shell composition before generating config.

**Output:** selected feature set, dependency rationale, missing decisions, and config-generation inputs.

### CShells Appsettings Generator

**Use when:** a user has selected features and wants appsettings JSON for a CShells/Nuplane shell.

**Workflow:** scan feature activation patterns and dependencies; generate JSON from selected features; include required dependencies; mark optional features separately.

**Output:** appsettings JSON and a note on how it was derived. Do not guess feature IDs when the repo does not expose them.
