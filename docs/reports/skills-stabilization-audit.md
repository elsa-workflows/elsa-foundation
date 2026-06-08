# Skills Stabilization Audit

Status: point-in-time audit for making the Elsa brain skill layer stable before adding more domain implementation work.

## Purpose

This report identifies which repeatable workflows already exist, which ones are hidden inside architecture guidance, and which concepts should stay in glossary/reference docs instead of becoming skills.

The goal is to keep the Elsa brain provider-neutral: `docs/skills/catalog.md` is the canonical skill catalog, while provider-specific folders such as `.claude/skills/` are executable wrappers or adapters.

## Zoom-Out Check

Program milestone advanced:

- Operating model: clarifies the provider-neutral skill surface and wrapper boundary.
- Executable workflows: identifies skills agents and engineers can actually follow.
- Knowledge surfaces: separates glossary/reference explanations from task workflows.
- Workspace split readiness: keeps feature-development workflows explicit so they can later move to `elsa-workspace` with less coupling.

This is higher-value than starting another domain implementation unit because the original Elsa-brain intent requires any future agent to enter through efficient maps, glossary, reports, and skills rather than re-learning the repo from broad constitution prose.

Result type: report finding. Accepted workflow descriptions belong in `docs/skills/catalog.md`. Provider-specific wrappers belong under provider adapter folders after the neutral workflow is stable.

## Inputs Reviewed

- `AGENTS.md`
- `docs/skills/catalog.md`
- `docs/reports/knowledge-inventory.md`
- `docs/reports/unfinished-work.md`
- `.claude/skills/*/SKILL.md`
- `.specify/workflows/workflow-registry.json`
- `.specify/workflows/speckit/workflow.yml`
- `.specify/extensions/git/`
- `.specify/memory/constitution-framework.md`
- `.specify/memory/constitution.md`
- `docs/glossary/`
- `docs/reference/`
- `docs/maps/`

## Current Skill Surface

| Surface | Current role | Finding |
|---|---|---|
| `docs/skills/catalog.md` | Provider-neutral workflow catalog | Canonical but short; mixes stable skills with planned workflows that do not yet have executable wrappers. |
| `.claude/skills/*` | Claude-specific command wrappers | Executable wrappers now include Speckit commands and Elsa-brain skills. They should not become canonical architecture docs. |
| `.specify/integrations/claude.manifest.json` | Claude integration manifest | Tracks Speckit and Elsa-brain Claude wrapper files by hash. |
| `.specify/workflows/*` | Speckit workflow definitions | Tool workflow layer for specify -> plan -> tasks -> implement with review gates. |
| `.specify/extensions/git/*` | Speckit git extension and scripts | Supports official branch/commit flow; should be surfaced through Speckit guidance, not duplicated in architecture docs. |
| `AGENTS.md` task paths | Provider-neutral entrypoint | Correctly points to skills, maps, glossary, reports, and Speckit flow. |
| Constitution sections | Quality gates and sanctioned patterns | Contain several repeatable implementation workflows that should be referenced by skills rather than copied into them. |

## Claude Wrapper Validation

Status: validated 2026-06-08 against the expanded provider-neutral catalog and the Claude integration manifest. Lightweight drift validation now exists at `tools/skills/validate-claude-wrapper-drift.ps1`.

Validation scope:

- Reviewed all `.claude/skills/elsa-*/SKILL.md` wrappers.
- Compared wrapper trigger/outline intent against `docs/skills/catalog.md`.
- Checked that wrappers remain thin provider adapters rather than canonical architecture documents.
- Checked blocked/planned skills for guardrails against guessing event dispatcher failure-policy / subscriber failure-classification implementation details, feature-identity, or appsettings decisions.
- Verified `.specify/integrations/claude.manifest.json` hashes for the Elsa wrapper files against the current files.
- Added an executable check for the current Claude Elsa wrapper batch.

Findings:

- The 18 Elsa Claude wrappers are accepted as the current Claude adapter batch.
- All reviewed wrappers point back to catalog entries through `metadata.source`.
- The wrappers preserve the provider-neutral catalog as canonical and do not duplicate long architecture explanations.
- The plan/act rule is reflected where it changes behavior: architecture-affecting implementation skills stop for a plan, while tests, catalog updates, generated-map refreshes, and small docs follow-through are treated as obligations after approval.
- Planned or blocked workflows keep the needed guardrails: event contribution/subscriber work follows the documented publisher-delivery / publisher-dispatcher-failure / subscriber-failure-classification split while deferring code-level mechanics for the latter two, feature composition does not guess feature IDs, and CShells appsettings generation does not guess feature IDs, appsettings keys, or configuration semantics.
- `tools/skills/validate-claude-wrapper-drift.ps1` passes for all 18 Elsa wrapper files and manifest entries.

No wrapper edits were required during this validation pass.

## Classification Rules

Use these rules before promoting anything into the skill catalog:

- A skill is a repeatable workflow with clear trigger, inputs, steps, and output.
- A glossary item is a term meaning that should be looked up and reused.
- A reference doc explains rationale, examples, and architectural context.
- A report records point-in-time findings, gaps, and unfinished work.
- A constitution section defines gates, allowed exceptions, and governance.
- A map records generated or cataloged facts for navigation.

Skills may link to glossary, reference docs, maps, reports, specs, and constitution gates. Skills must not duplicate long concept explanations from those layers.

## Ready Skills

These workflows are already clear enough to remain in the catalog and can later receive provider wrappers.

| Skill | Canonical source | Wrapper status | Notes |
|---|---|---|---|
| Architecture Tour | `docs/architecture-tour.md` | Claude wrapper: `.claude/skills/elsa-architecture-tour/` | Stable orientation workflow. |
| Glossary Lookup | `docs/glossary/root.md`; `docs/glossary/elsa.md` | Claude wrapper: `.claude/skills/elsa-glossary-lookup/` | Stable lookup workflow. |
| Critical Constitution Review | `docs/skills/catalog.md`; constitution files | Claude wrapper: `.claude/skills/elsa-critical-constitution-review/` | Needs non-Claude wrappers only after wrapper shape is accepted. |
| Verify Codebase Against Constitution | `docs/skills/catalog.md`; maps/reports | Claude wrapper: `.claude/skills/elsa-verify-codebase/` | First report shape exists in `test-maturity-and-weak-implementation-report.md`. |
| What's Next / Unfinished Work | `AGENTS.md`; `unfinished-work.md` | Claude wrapper: `.claude/skills/elsa-whats-next/` | Should remain the default selection skill before new work units. |
| Work Unit Planner | `docs/skills/catalog.md` | Claude wrapper: `.claude/skills/elsa-work-unit-planner/` | Good bridge from findings to architecture/Speckit work. |
| Source-of-Truth Audit | `AGENTS.md`; `knowledge-inventory.md`; `docs/skills/catalog.md` | Claude wrapper: `.claude/skills/elsa-source-of-truth-audit/` | Needed for constitution thinning and layer drift prevention. |
| Create Agent Preference | `AGENTS.md`; `docs/reference/agent-preferences.md`; `docs/skills/catalog.md` | Claude wrapper: `.claude/skills/elsa-create-agent-preference/` | Keeps personal workflow choices in ignored `.agent-prefs/` files instead of shared doctrine. |
| Initialize Agent Preferences | `AGENTS.md`; `docs/reference/agent-preferences.md`; `docs/reference/git-operating-models.md`; `docs/skills/catalog.md` | Claude wrapper: `.claude/skills/elsa-initialize-agent-preferences/` | Bootstraps local preferences only when none exist, without making one user's workflow the repo default. |
| Speckit Flow Guide | `.specify/workflows`; `.claude/skills/speckit-*` | Claude wrappers exist | Existing provider wrappers are Speckit command-level, not Elsa-brain-level. |
| Feature/Dependency Map Builder | `tools/maps/*`; `docs/maps/*` | Claude wrapper: `.claude/skills/elsa-feature-dependency-map/` | Map scripts already exist; workflow should point to the split map refresh commands. |

## Planned Skills That Need More Input

These are in the catalog but cannot be fully executable until underlying identifiers or policy choices are stable.

| Skill | Blocking gap | Next action |
|---|---|---|
| Feature Composition Explorer | Feature identifiers, dependency rules, and compatibility signals need a stable discovery path. | Use existing maps and extension-point catalogs to define the minimal feature-selection evidence set. |
| CShells Appsettings Generator | Generation should not guess feature IDs or appsettings schema. Configuration policy is still deferred. | Plan after feature identifiers and configuration/appsettings conventions are discoverable. |

## Hidden Skills To Promote

These workflows are currently buried in constitution sections, reference examples, reports, or source conventions. They should become first-class provider-neutral skills after review.

| Candidate skill | Trigger | Canonical references | Expected output | Priority |
|---|---|---|---|---|
| Create Feature or Module | A new feature/module needs to be added or ported. | Framework `§2.1`, `§2.2`, `§2.19`, `§2.20`, `§2.23`; Speckit flow. | Feature/module shape, project placement, dependency envelope, tests, docs, extension-point updates. | High; Claude wrapper exists at `.claude/skills/elsa-create-feature/` |
| Extend Feature by Inheritance | A feature must extend, decorate, or specialize another feature. | Framework `§2.5`; worked examples. | Inheriting feature shape with registration override and tests. | High; Claude wrapper exists at `.claude/skills/elsa-extend-feature-inheritance/` |
| Add Event Contribution | A feature contributes to a fan-in event. | Framework `§2.6.1` / `§2.6.6`; extension-point catalogs; event failure-strategy implementation gap. | Contributor interface implementation plus single owning handler update where applicable; record dispatcher failure-policy and subscriber failure-classification needs until code support exists. | High; Claude wrapper exists at `.claude/skills/elsa-add-event-contribution/` |
| Add Independent Event Subscriber | A feature observes an event for audit, cache, telemetry, or side effects unrelated to fan-in aggregation. | Framework `§2.6.1` / `§2.6.6`; event failure-strategy implementation gap. | Event handler registration with publisher-owned delivery-strategy / dispatcher-failure check and subscriber-owned failure-classification rationale. | High; covered by `.claude/skills/elsa-add-event-contribution/` for now |
| Add Replacement Contract Implementation | A single implementation is selected per app/runtime context. | Framework `§2.6.2`; extension-point catalogs. | Replacement implementation and conflict-detection evidence. | Medium; Claude wrapper exists at `.claude/skills/elsa-add-replacement-contract/` |
| Add Bridge or Adapter | A workflow crosses seams or wraps an external/heavy dependency. | Framework `§2.7`; `docs/seams.md`; runtime pre-spec handoff. | Bridge/adapter contract placement, dependency direction, and tests. | High; Claude wrapper exists at `.claude/skills/elsa-add-bridge-adapter/` |
| Add Extension-Point Catalog Entry | A feature exposes or implements extension points. | Framework `§2.22`; `EXTENSION_POINTS.md`; `docs/maps/extension-point-map.md`. | Updated project catalog and generated map refresh if needed. | High; Claude wrapper exists at `.claude/skills/elsa-extension-point-catalog/` |
| Add Feature Registration Tests | A new feature class is created or changed. | Framework `§2.23.1`; test maturity report. | Unit test proving service registration and collaborator wiring. | High; covered by `.claude/skills/elsa-add-unit-tests/` |
| Add Implementation Unit Tests | Logic-bearing class is created or changed. | Framework `§2.23.2`; test maturity report. | Branch-covered unit tests with stubbed dependencies. | High; covered by `.claude/skills/elsa-add-unit-tests/` |
| Promote Finding to Work Unit | A report finding needs architecture or feature work. | Work Unit Planner; Speckit Flow Guide. | Work-unit plan and, when approved, Speckit inputs. | High |
| Refresh Generated Maps | Inputs changed or map freshness is uncertain. | `AGENTS.md`; `tools/maps/*`; `docs/maps/manifest.json` when present. | Refreshed map snapshots and any findings. | Medium; Claude wrapper exists at `.claude/skills/elsa-refresh-generated-maps/` |

## Not Skills

These topics should stay out of the skill catalog except as linked references:

| Topic | Proper layer | Reason |
|---|---|---|
| Meaning of seam, bridge, contribution, replacement contract, activity catalog, workflow state | Glossary | Terms should have one canonical meaning. |
| Why Workflows.Design and Workflows.Runtime split exists | Reference docs and constitution gate | The gate is constitutional; rationale belongs in reference docs. |
| Constitution draft history | Reports | Provenance should not drive task execution. |
| Runtime execution pre-spec risks | Report until converted to an approved work unit/spec | It is input, not final workflow. |
| Full event-system rationale | Reference/constitution after review | Implementation steps can become skills, but strategy role rules belong in gates/reference. |

## Wrapper Strategy

Provider-neutral skill definitions should be accepted before wrappers are created.

Recommended order:

1. Stabilize `docs/skills/catalog.md` headings and workflow boundaries.
2. Add provider wrappers only for high-frequency workflows.
3. Keep wrappers thin: they should load the catalog entry, required gates, maps, and glossary links, then execute the workflow.
4. Do not duplicate architecture explanations inside wrappers.

Execution rule:

- Plan first for decisions: new architecture-affecting work units, boundaries, gates, naming, contracts, bridges, feature shape, and event semantics require a plan/checklist before implementation.
- Act as follow-through for obligations: after the user approves a unit, required tests, extension-point catalog updates, generated-map refreshes, and small docs/index updates are part of completing that unit unless they introduce a new architecture decision.

First wrapper candidates after catalog review:

- What's Next / Unfinished Work: created for Claude at `.claude/skills/elsa-whats-next/`.
- Work Unit Planner: created for Claude at `.claude/skills/elsa-work-unit-planner/`.
- Critical Constitution Review: created for Claude at `.claude/skills/elsa-critical-constitution-review/`.
- Verify Codebase Against Constitution: created for Claude at `.claude/skills/elsa-verify-codebase/`.
- Create Agent Preference: created for Claude at `.claude/skills/elsa-create-agent-preference/`.
- Initialize Agent Preferences: created for Claude at `.claude/skills/elsa-initialize-agent-preferences/`.
- Create Feature or Module: created for Claude at `.claude/skills/elsa-create-feature/`.
- Add Event Contribution: created for Claude at `.claude/skills/elsa-add-event-contribution/`.
- Add Bridge or Adapter: created for Claude at `.claude/skills/elsa-add-bridge-adapter/`.

Second wrapper batch:

- Architecture Tour: created for Claude at `.claude/skills/elsa-architecture-tour/`.
- Glossary Lookup: created for Claude at `.claude/skills/elsa-glossary-lookup/`.
- Source-of-Truth Audit: created for Claude at `.claude/skills/elsa-source-of-truth-audit/`.
- Extend Feature by Inheritance: created for Claude at `.claude/skills/elsa-extend-feature-inheritance/`.
- Add Replacement Contract Implementation: created for Claude at `.claude/skills/elsa-add-replacement-contract/`.
- Add Extension-Point Catalog Entry: created for Claude at `.claude/skills/elsa-extension-point-catalog/`.
- Add Unit Tests: created for Claude at `.claude/skills/elsa-add-unit-tests/`.
- Refresh Generated Maps: created for Claude at `.claude/skills/elsa-refresh-generated-maps/`.
- Feature/Dependency Map Builder: created for Claude at `.claude/skills/elsa-feature-dependency-map/`.
- Feature Composition Explorer: created for Claude at `.claude/skills/elsa-feature-composition/`.
- CShells Appsettings Generator: created for Claude at `.claude/skills/elsa-cshells-appsettings/`.

## Follow-Up Work Units

1. Mirror the accepted Claude wrapper batch into other provider adapter surfaces when those surfaces exist.
2. Run `tools/skills/validate-claude-wrapper-drift.ps1` after catalog, Claude wrapper, or Claude manifest changes.
3. Implement event dispatcher failure-policy and subscriber failure-classification support before treating those mechanics as executable code.
4. Define feature identity/appsettings evidence before implementing the CShells Appsettings Generator.
5. Expand generated maps only where a skill needs stable navigation facts.

## Recommendation

Adopt the expanded `docs/skills/catalog.md` as the next stable provider-neutral skill surface. Keep this report as the audit trail and use it to drive wrapper creation in a later unit.
