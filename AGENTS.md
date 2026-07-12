# Elsa Foundation Agent Entrypoint

This file is the AI-provider-neutral front door for AI agents and engineers working in `elsa-foundation`.

## Repository intent

`elsa-foundation` is the transitional Elsa foundation workspace. It contains the main Elsa domain core libraries, default foundation implementations, Speckit specs, and architecture knowledge needed to guide the Elsa modular refactor.

The repo still supports feature development, but the operating model is shaped so feature-workspace assets can later move to a dedicated `elsa-workspace` repository.

## Source-of-truth layers

Use the narrowest source that answers the task.

| Need | Canonical home |
|---|---|
| Quality gates, invariants, allowed exceptions, ratification | `.specify/memory/constitution-framework.md` and `.specify/memory/constitution.md` |
| Domain and architecture terms | `docs/glossary/` |
| Task workflows and skill descriptions | `docs/skills/catalog.md` |
| Personal workflow/model preferences | `.agent-prefs/` for local selections; `docs/reference/` for committed catalogs/templates |
| Shared backlog, program-goal bucket registry, stewardship, active objectives, roadmap notes | `docs/program-goals/` |
| Repo navigation, extension points, dependency/test maps | `docs/maps/` and `EXTENSION_POINTS.md` |
| Current gaps, draft decisions, draft history, inventory findings | `docs/reports/` |
| Feature/work-unit specifications | `specs/` |
| AI-provider-specific adapters | `.claude/`, `.specify/integrations/`, and provider shim files |

Do not duplicate concept explanations in new docs. Link to the canonical glossary entry or map instead.

## Program goals and drift guard

`docs/program-goals/` is the shared backlog and planner for durable work in this workspace. Program goals are mid-term buckets of related short-term objectives, not mandatory doctrine for every session.

Use a named program-goal bucket when work forms part of a mid-term coordination surface. Use `none/free-flow` when the user is exploring, developing, researching, or planning without a named bucket. Free-flow work can still produce reports, specs, docs, maps, or code; it just should not pretend to belong to a bucket.

Reports such as `docs/reports/unfinished-work.md` are inventories of findings and concerns, not the active queue. When a report finding becomes planned durable work, add or move it to the relevant program-goal bucket before implementation, or explicitly mark the state as `none/free-flow` for work that should remain outside a bucket.

Use a lightweight drift guard instead of a mandatory zoom-out at every session start. Trigger a program-goal check when one of these is true:

- The user asks "what next", asks whether the work is drifting, or asks to revisit priorities.
- The same session or handoff chain is moving into a third consecutive plan/work unit under the same topic.
- Recent work is repeatedly deepening one local area while other tracked work is waiting.
- A proposed task would create a new durable tracking item, move work between buckets, split a bucket, or retire a bucket.
- A fresh session resumes from context that already says drift, priority, or roadmap alignment is in question.

When triggered, keep the check concise:

- Which program-goal state applies: a named bucket, `none/free-flow`, or temporarily `unknown/not-assessed`?
- If it is planned durable work, which bucket owns it, or why should it remain `none/free-flow`?
- Is this still the highest-value next step, or are we staying in a local area because it is nearby?
- Does the work preserve the source-of-truth layers above?
- Should the result be a gate, glossary term, reference explanation, report finding, generated map, skill workflow, spec, or code change?

For ordinary fresh sessions with a clear task, do not announce a zoom-out check. Apply the guard quietly unless a trigger is present. If the check suggests drift, surface it briefly so the user can continue knowingly, redirect, or update the relevant program-goal bucket.

## Refresh generated maps

Windows / PowerShell:

```powershell
tools/maps/generate-maps.ps1
tools/maps/generate-domain-map.ps1
tools/maps/generate-extension-point-map.ps1
tools/maps/generate-architecture-reference-map.ps1
tools/maps/generate-feature-dependency-map.ps1
```

macOS / Linux / Bash:

```bash
bash tools/maps/generate-maps.sh
bash tools/maps/generate-domain-map.sh
bash tools/maps/generate-extension-point-map.sh
bash tools/maps/generate-architecture-reference-map.sh
bash tools/maps/generate-feature-dependency-map.sh
```

If the local shell is unavailable, try the other script if that shell is installed. If neither
PowerShell nor Bash is available, ask the user to install one of them before refreshing maps.

Generated maps are committed snapshots. Before relying on any map for navigation or verification,
check `docs/maps/manifest.json`. If the map is invoked and relevant inputs are dirty, changed, or
freshness is uncertain, refresh the narrowest relevant map layer first, then review any generated
findings report before continuing. If the refreshed report exposes drift that makes the current work
unsafe, stop and tell the user before proceeding. Maps v2 scripts are split so agents can refresh
only the layer they need.

## Personal operating preferences

Personal workflow choices are local implementation details, not shared repo facts.

- Keep committed preference catalogs/templates in `docs/reference/`.
- Keep committed operating-model options in [docs/reference/git-operating-models.md](docs/reference/git-operating-models.md).
- Keep per-user selections in `.agent-prefs/`; only `.agent-prefs/.gitkeep` is committed.
- If `.agent-prefs/` has no preference files other than `.gitkeep`, use [Initialize Agent Preferences](docs/skills/catalog.md#initialize-agent-preferences) to run a quick setup before substantial planning, pushing, opening PRs, changing remotes, or starting a multi-session workflow.
- If `.agent-prefs/git-operating-model.md` exists, follow it for Git workflow.
- If `.agent-prefs/session-execution-model.md` exists, follow it for control-room vs fresh-agent/thread workflow.
- If a user states a stable personal workflow preference, use [Create Agent Preference](docs/skills/catalog.md#create-agent-preference) to decide whether to record it locally under `.agent-prefs/` or propose a committed preference catalog/template.
- If no personal Git preference exists, read the committed Git operating-model catalog and ask the user which model they prefer before pushing, opening PRs, or changing remotes.
- After completing an approved work unit that changes files, make a local commit with a useful message describing the work unless the user explicitly asks to leave the changes uncommitted or the work is intentionally paused for review.
- Treat the current session as a lightweight control room when the user prefers fresh-agent execution: before substantial planning, ask whether to plan/execute here or prepare a reviewed handoff prompt and start a fresh agent/thread; after worker execution, summarize the result, ensure completed file-changing work is committed locally, and ask whether to continue here or prepare the next handoff.
- Do not commit personal preference files from `.agent-prefs/`.

## Task paths

### Feature development

1. Read the relevant spec under `specs/`, if one exists.
2. Read [docs/skills/catalog.md](docs/skills/catalog.md#speckit-flow-guide) for the Speckit flow.
3. Use the official sequence: `speckit-specify` -> `speckit-plan` -> `speckit-tasks` -> `speckit-implement`.
4. Keep work on a feature/work-unit branch unless the user explicitly asks otherwise.
5. Use the constitution as a gate during planning; use glossary/docs for learning context.

### Architecture development

1. Start from the user intent and the relevant constitution section.
2. Use [Critical Constitution Review](docs/skills/catalog.md#critical-constitution-review) or [Work Unit Planner](docs/skills/catalog.md#work-unit-planner).
3. Record uncertain or unratified decisions in [docs/reports/unfinished-work.md](docs/reports/unfinished-work.md) or a linked work unit.
4. Keep development flows amendable; do not freeze a process before the architecture work proves it.

### Codebase verification

1. Use [Verify Codebase Against Constitution](docs/skills/catalog.md#verify-codebase-against-constitution).
2. Check project references, extension-point catalogs, tests, stubs, and external packages.
3. Produce a report before proposing code changes.

### Glossary lookup

1. Read [docs/glossary/root.md](docs/glossary/root.md) for framework terms.
2. Read [docs/glossary/elsa.md](docs/glossary/elsa.md) for Elsa-specific terms.
3. Read worked references such as [docs/seams.md](docs/seams.md) or [docs/serialization.md](docs/serialization.md) only when the term needs deeper context.

### Feature composition

1. Use [Feature Composition Explorer](docs/skills/catalog.md#feature-composition-explorer) to decide which features belong in a shell.
2. Use [CShells Appsettings Generator](docs/skills/catalog.md#cshells-appsettings-generator) to generate a runnable composition file.
3. Use dependency maps to include required dependencies and external package compatibility signals.

### Architecture tour

Read [docs/architecture-tour.md](docs/architecture-tour.md) for a concise orientation before diving into constitutions or specs.

## Constitution boundary

The constitutions are draft quality-gate documents. Warn users when constitution draft/provisional
status matters to their task. If they want to focus on unratified material, route that through
[Constitution Readiness](docs/program-goals/constitution-readiness.md) and use
[Critical Constitution Review](docs/skills/catalog.md#critical-constitution-review) or
[Work Unit Planner](docs/skills/catalog.md#work-unit-planner). Draft history belongs in
[docs/reports/constitution-draft-history.md](docs/reports/constitution-draft-history.md); current
gaps belong in [docs/reports/knowledge-inventory.md](docs/reports/knowledge-inventory.md) and
[docs/reports/unfinished-work.md](docs/reports/unfinished-work.md).

New work should move toward this rule:

- Constitution: gates and governance.
- Glossary: meanings.
- Skills: executable workflows.
- Maps/catalogs: navigation and generated facts.
- Reports: current findings and unfinished work.

<!-- SPECKIT START -->
For additional context about technologies to be used, project structure,
shell commands, and other important information, read the current plan
at specs/091-structured-logs-replay-cursors/plan.md
<!-- SPECKIT END -->
