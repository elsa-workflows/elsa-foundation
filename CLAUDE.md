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

```powershell
claude --add-dir ../elsa-foundation-project-management
```

Use that sibling only when a task explicitly needs historical meeting notes, follow-up files, or project-management artifacts. For normal work in this repository, start from [AGENTS.md](AGENTS.md), the docs index, and the two constitution files.
