# Task paths

Moved from `AGENTS.md` (#2578). `AGENTS.md` keeps a task-to-entry-point table.

## Feature development

1. Read the relevant spec under `specs/`, if one exists.
2. Read [docs/skills/catalog.md](../skills/catalog.md#speckit-flow-guide) for the Speckit flow.
3. Follow specification -> plan -> tasks -> implementation, reviewing each artifact before the next step. When using an AI integration, use `speckit-specify` -> `speckit-plan` -> `speckit-tasks` -> `speckit-implement`. Otherwise, create and review the same artifacts manually using [the contributor guide](../../CONTRIBUTING.md#plan-feature-work). The specification, constitution, testing, and review gates apply to both paths.
4. Keep work on a feature/work-unit branch unless the user explicitly asks otherwise.
5. Use the constitution as a gate during planning; use glossary/docs for learning context.

## Architecture development

1. Start from the user intent and the relevant constitution section.
2. Use [Critical Constitution Review](../skills/catalog.md#critical-constitution-review) or [Work Unit Planner](../skills/catalog.md#work-unit-planner).
3. Record uncertain or unratified decisions in [docs/reports/unfinished-work.md](../reports/unfinished-work.md) or a linked work unit.
4. Keep development flows amendable; do not freeze a process before the architecture work proves it.

## Codebase verification

1. Use [Verify Codebase Against Constitution](../skills/catalog.md#verify-codebase-against-constitution).
2. Check project references, extension-point catalogs, tests, stubs, and external packages.
3. Produce a report before proposing code changes.

## Backend e2e tests

`e2e-tests/` holds backend, REST-driven end-to-end tests (PowerShell) that exercise a from-source
`Elsa.Workbench` through the real HTTP + persistence + runtime path — the black-box complement to the in-process
C# tests under `tests/`. See [e2e-tests/README.md](../../e2e-tests/README.md) for the runner, per-suite categorization,
and setup gotchas (fresh-DB-on-rebuild; opt-in `scheduling`/`DispatchWorkflow` features).

1. When you change design, publishing, runtime, activity, or stimulus/scheduling behavior, run the relevant
   `e2e-tests/` suite against a rebuilt server before considering the change done — it catches integration
   regressions unit tests miss.
2. Treat a failing e2e test as a **signal, not a verdict**: it is either a real regression or a **stale test**
   (the codebase moved — a contract/shape changed, a tracked bug was fixed, or a feature was renamed). Rebuild on
   current `main` with a fresh DB, re-run, and reconcile before "fixing" either side. `KNOWN ISSUE #NNNN` trackers
   pass green by design and auto-flip to strict once the bug is fixed.
3. On Windows use `powershell -NoProfile -ExecutionPolicy Bypass -File <script>` (no `pwsh`).
4. An e2e script must never stop a process it did not start. Give the server location once, as `-BaseUrl`; a
   script that restarts a server owns it through `e2e-tests/_ServerLifecycle.ps1` and fails, naming the port and
   pid, when the port is held by anything else. See "Running these tests" in the README.

## Glossary lookup

1. Read [docs/glossary/root.md](../glossary/root.md) for framework terms.
2. Read [docs/glossary/elsa.md](../glossary/elsa.md) for Elsa-specific terms.
3. Read worked references such as [docs/seams.md](../seams.md) or [docs/serialization.md](../serialization.md) only when the term needs deeper context.

## Feature composition

1. Use [Feature Composition Explorer](../skills/catalog.md#feature-composition-explorer) to decide which features belong in a shell.
2. Use [CShells Appsettings Generator](../skills/catalog.md#cshells-appsettings-generator) to generate a runnable composition file.
3. Use dependency maps to include required dependencies and external package compatibility signals.

## Architecture tour

Read [docs/architecture-tour.md](../architecture-tour.md) for a concise orientation before diving into constitutions or specs.
