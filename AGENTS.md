# Elsa Foundation Agent Entrypoint

This file is the AI-provider-neutral front door for AI agents and engineers working in `elsa-foundation`. It routes; the detail lives in [docs/contributing/](docs/contributing/).

## Agent protocol

The org-wide [AGENTS.md][org-agents] is the protocol for finding, claiming, branching, opening PRs, review and merge ([Issue Bus Lite][org-agents-ibl]). If this file or a doc it links conflicts with the org file, the org file wins, unless the repo note names an explicit owner exception. Everything below is a repo-specific note, such as structure, builds and the build-slot wrapper, generated maps, the constitution and the evidence a PR must carry here.

## Repository intent

`elsa-foundation` is the transitional Elsa foundation workspace. It contains the main Elsa domain core libraries, default foundation implementations, Speckit specs, and architecture knowledge needed to guide the Elsa modular refactor.

The repo still supports feature development, but the operating model is shaped so feature-workspace assets can later move to a dedicated `elsa-workspace` repository.

## Current focus

[ADR 0080](docs/adr/0080-elsa-4-simplification-decisions.md) sets the current direction: risk-based tests, internal-by-default types, Balanced persistence by default, pre-GA cleanup, plain-language renames and a smaller meta footprint. The owner's 11 October amendment makes Foundation/Studio consolidation the first priority and reactivates Foundation-owned GitBook documentation in parallel; see [program goals](docs/program-goals/README.md). Work that contradicts an ADR 0080 decision needs the owner's explicit go-ahead.

## Source-of-truth layers

Use the narrowest source that answers the task.

| Need | Canonical home |
|---|---|
| Quality gates, invariants, allowed exceptions, ratification | `.specify/memory/constitution-framework.md` and `.specify/memory/constitution.md` |
| Domain and architecture terms | `docs/glossary/` |
| Task workflows and skill descriptions | `docs/skills/catalog.md` |
| Agent protocol: finding, claiming, branches, PRs, review and merge | [org AGENTS.md][org-agents] |
| Repo contributor notes: claim detail, PR gate evidence, generated maps, task paths, review checklist | `docs/contributing/` |
| Personal workflow/model preferences | `.agent-prefs/` for local selections; `docs/reference/` for committed catalogs/templates |
| Shared backlog, program-goal bucket registry, stewardship, active objectives, roadmap notes | `docs/program-goals/` |
| Repo navigation, extension points, dependency/test maps | `docs/maps/` and `EXTENSION_POINTS.md` |
| Current gaps, draft decisions, draft history, inventory findings | `docs/reports/` |
| Feature/work-unit specifications | `specs/` ([lifecycle and numbering](docs/reference/spec-lifecycle.md)) |
| AI-provider-specific adapters | `.claude/`, `.specify/integrations/`, and provider shim files |

Do not duplicate concept explanations in new docs. Link to the canonical glossary entry or map instead.

## Program goals and drift guard

`docs/program-goals/` is the shared backlog and planner for durable work. Use a named program-goal bucket when work belongs to one, and `none/free-flow` when the user is exploring, developing, researching or planning without one. Reports such as `docs/reports/unfinished-work.md` are inventories of findings, not the active queue. Apply the drift guard quietly unless one of its triggers is present; the triggers and questions are in [Program goals and drift guard](docs/contributing/program-goals-and-drift-guard.md).

## Concurrent work claims

Claim, branch and hand off as [Issue Bus Lite][org-agents-ibl] says: comment `claim: <worker> starting`, re-read, and if the earliest unabandoned claim is yours, assign yourself and remove `ready-for-agent`. Claude sessions post as `elsa-workflows-claude[bot]` and Codex sessions as `elsa-workflows-codex[bot]`, with no role prefix, so a Claude claim is `claim: claude starting`. Only when you post through the owner's account does your role tag come first, for example `[Claude] claim: claude starting`. Branch `agent/<worker>/<issue>-<slug>`. Several sessions run here at once in separate worktrees, so re-check the issue and its open PRs before committing. Repo detail: [Claims](docs/contributing/repository-hygiene.md#claims); commands: [Issue tracker](docs/agents/issue-tracker.md).

## Branch and issue hygiene

Follow [Repository hygiene](docs/contributing/repository-hygiene.md). In short:

- Push a branch only when opening its PR (draft is fine) in the same session; otherwise keep the work local.
- Never restore a branch deleted on merge; its work is in `main`. Restore a swept branch from its `archive/` tag only when a maintainer asks to resume that work, and open its PR in the same session.
- File new issues as `needs-triage` only. The one exception is the [UX QA routine](docs/skills/catalog.md#ux-qa-routine), which files reproduced defects as `ready-for-agent`. Put checkpoints, receipts and evidence on the PR or the issue being worked, never in a new issue.
- Respect the caps and named owner exceptions in [Repository hygiene](docs/contributing/repository-hygiene.md#issues): normally at most 2 active programs and 20 `ready-for-agent` issues. The 11 October exception reactivates existing #2501 and #2495; it does not authorize more programs. At the cap, extend the existing parent instead of filing more.
- Before closing an issue as not planned, run `git log origin/main -E --grep '(^|[^[:alnum:]_./-])#<number>([^0-9]|$)'`. If its work landed, close it as completed and name the commit or PR.

## Builds on a shared machine

Parallel sessions on one machine compete for the same CPU. Four concurrent solution builds on an 8-core machine have pushed the load average past 400, which makes every build slower and makes timeout-based tests fail when nothing is wrong.

- Build and test only the projects your change touches. Run a whole-solution test run only when the change is cross-cutting, and leave the rest to CI.
- A machine may queue `dotnet` build, test, restore, pack, publish, msbuild and clean commands through a build-slot wrapper earlier on `PATH`. When a command prints `[dotnet-build-slots] waiting for a build slot`, it is queued behind another session, not hung. Do not bypass the queue, and allow for the wait in command timeouts.
- Check `uptime` before trusting a timing-shaped failure. The load average counts runnable tasks, so compare it with the core count, and rerun at lower load before calling the failure real.
- A process started as a background job of a non-interactive shell ignores SIGINT. Stop such a process with SIGTERM; a SIGINT that has no effect there does not mean the process hangs on Ctrl+C.

## Program bookkeeping

Comment on the program issue at every state transition. Every PR carries this repo's gate evidence as a PR comment; that evidence is required for review, not permission to merge. Merging follows [Issue Bus Lite][org-agents-review]: request review with `@elsa-workflows-bot review`, Code Review (`elsa-workflows-review-bot[bot]`) approves and merges, and an author never merges its own PR. See [Program bookkeeping](docs/contributing/merging.md#program-bookkeeping) and [PR evidence](docs/contributing/merging.md#pr-evidence).

## Post-merge gates

`CI` also runs on every push to `main` and nightly. Before starting work that assumes a healthy main, check for an open `main is red: CI` issue first; see [Post-merge gates](docs/contributing/merging.md#post-merge-gates).

## Refresh generated maps

`dotnet run --project tools/maps/Elsa.Maps.Generator -- check` is the only authoritative freshness answer, and `-- all` refreshes every map. Refreshing is a deliberate act; afterwards stage every changed map by explicit path, `docs/maps/manifest.json` included. See [Generated maps](docs/contributing/generated-maps.md).

## Personal operating preferences

Maintainers publish organization-repository branches and contributors use fork PRs; per-user selections live in ignored `.agent-prefs/`. See [Agent preferences](docs/contributing/agent-preferences.md).

## Task paths

| Task | Entry point |
|---|---|
| Feature development | [Speckit flow](docs/skills/catalog.md#speckit-flow-guide) and [task path](docs/contributing/task-paths.md#feature-development) |
| Architecture development | [Critical Constitution Review](docs/skills/catalog.md#critical-constitution-review) or [Work Unit Planner](docs/skills/catalog.md#work-unit-planner) |
| Codebase verification | [Verify Codebase Against Constitution](docs/skills/catalog.md#verify-codebase-against-constitution) |
| Backend e2e tests | [Backend e2e tests](docs/contributing/task-paths.md#backend-e2e-tests) and [e2e-tests/README.md](e2e-tests/README.md) |
| Constitution review of a change | [Review checklist](docs/contributing/review-checklist.md) |
| Glossary lookup | [docs/glossary/root.md](docs/glossary/root.md) and [docs/glossary/elsa.md](docs/glossary/elsa.md) |
| Feature composition | [Feature Composition Explorer](docs/skills/catalog.md#feature-composition-explorer) |
| Architecture tour | [docs/architecture-tour.md](docs/architecture-tour.md) |

## Constitution boundary

Both constitutions are ratified at the document level (2026-08-08; now framework v6.1.0 and Elsa v6.0.2, after #2578 and #2591); sections marked draft or provisional, framework §2.24 and Elsa §E2.8 activity versioning, Model X and §E2.9, are excluded from that ratification. Warn users when a section's draft or provisional status matters to their task, and route unratified material through [Constitution Readiness](docs/program-goals/constitution-readiness.md). Draft history: [docs/reports/archive/constitution-draft-history.md](docs/reports/archive/constitution-draft-history.md); current gaps: [docs/reports/knowledge-inventory.md](docs/reports/knowledge-inventory.md) and [docs/reports/unfinished-work.md](docs/reports/unfinished-work.md).

New work moves toward this rule: constitution for gates and governance, glossary for meanings, skills for executable workflows, maps and catalogs for navigation and generated facts, reports for current findings and unfinished work.

[org-agents]: https://github.com/elsa-workflows/.github/blob/main/AGENTS.md
[org-agents-ibl]: https://github.com/elsa-workflows/.github/blob/main/AGENTS.md#issue-bus-lite
[org-agents-review]: https://github.com/elsa-workflows/.github/blob/main/AGENTS.md#review-and-merge-rules
