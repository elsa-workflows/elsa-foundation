# Personal operating preferences

Moved from `AGENTS.md` (#2578). The committed preference catalog and templates are in [docs/reference/agent-preferences.md](../reference/agent-preferences.md).

Personal workflow choices are local implementation details, not shared repo facts.

- Keep committed preference catalogs/templates in `docs/reference/`.
- Use [Git operating models](../reference/git-operating-models.md) for publication: maintainer work (including Sipke’s sessions) always uses feature/work-unit branches in the organization repository, never fork PRs. Contributors without organization write access use fork PRs. Determine the applicable path from the session context and repository access; do not require a workflow selection before pushing or opening PRs.
- Keep per-user selections in `.agent-prefs/`; only `.agent-prefs/.gitkeep` is committed.
- If `.agent-prefs/` has no preference files other than `.gitkeep`, use [Initialize Agent Preferences](../skills/catalog.md#initialize-agent-preferences) only for personal choices needed by the current task. Missing preference files do not gate publication.
- An optional `.agent-prefs/git-operating-model.md` may record remote names and other local Git details; it must respect the maintainer/contributor publication paths above.
- If `.agent-prefs/session-execution-model.md` exists, follow it for control-room vs fresh-agent/thread workflow.
- If a user states a stable personal workflow preference, use [Create Agent Preference](../skills/catalog.md#create-agent-preference) to decide whether to record it locally under `.agent-prefs/` or propose a committed preference catalog/template.
- After completing an approved work unit that changes files, make a local commit with a useful message describing the work unless the user explicitly asks to leave the changes uncommitted or the work is intentionally paused for review.
- Treat the current session as a lightweight control room when the user prefers fresh-agent execution: before substantial planning, ask whether to plan/execute here or prepare a reviewed handoff prompt and start a fresh agent/thread; after worker execution, summarize the result, ensure completed file-changing work is committed locally, and ask whether to continue here or prepare the next handoff.
- Do not commit personal preference files from `.agent-prefs/`.
