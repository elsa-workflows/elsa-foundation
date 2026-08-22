# CLAUDE.md - Elsa Foundation compatibility shim

Claude-specific instructions in this repository are intentionally thin.

## First action

Read [AGENTS.md](AGENTS.md). It is the provider-neutral source of truth for how to work in this workspace.

## Claude/Speckit adapters

- Speckit skills installed for Claude live in `.claude/skills/`.
- Speckit integration manifests live in `.specify/integrations/`.
- Speckit workflow and git extension files live under `.specify/workflows/` and `.specify/extensions/`.

These files are adapters and execution surfaces. They are not canonical architecture documentation.

## Optional sibling context

Some older constitution drafting records refer to a sibling repository:

Use a locally available checkout only when a task explicitly needs historical meeting notes,
follow-up files, or project-management artifacts; do not assume a sibling path exists. For normal
work in this repository, start from [AGENTS.md](AGENTS.md), the docs index, and the two constitution
files.

## Agent skills

### Issue tracker

Issues are tracked in GitHub Issues for `elsa-workflows/elsa-foundation`; external PRs are not a triage request surface. See `docs/agents/issue-tracker.md`.

### Triage labels

Use the default triage label vocabulary: `needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, and `wontfix`. See `docs/agents/triage-labels.md`.

### Integrating main

`main` is the source of truth. Check whether it is ahead early, and after merging re-run the suites that
cover deployment modes - a gate that landed on `main` can make a whole mode impossible while unit tests
stay green. See `docs/agents/main-integration.md`.

### Domain docs

Use the single-context domain-doc layout: root `CONTEXT.md` plus root `docs/adr/` when present. See `docs/agents/domain.md`.
