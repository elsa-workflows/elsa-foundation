# Agent Preferences

This catalog describes local agent preference files. It is committed reference material, not a personal preference file.

Personal selections belong in `.agent-prefs/`, which is intentionally ignored by Git except for `.agent-prefs/.gitkeep`.

Use agent preferences for stable user workflow choices that affect how agents run sessions but are not shared repo facts, architecture decisions, product requirements, or team-wide gates.

## Placement Rules

- Commit reusable catalogs, option descriptions, and templates under `docs/reference/`.
- Store user-specific selections under `.agent-prefs/<preference-name>.md`.
- Do not commit `.agent-prefs/*.md`.
- Do not use `.agent-prefs/` for secrets, credentials, environment variables, or project facts.
- When a preference would affect other contributors by default, propose a committed reference/catalog change instead of recording a personal selection.

## Preference File Shape

Use short Markdown files with:

- a clear title
- the chosen preference
- any relevant scope or defaults
- the date or rationale only when it helps future agents avoid re-asking

Prefer stable, descriptive filenames such as:

- `.agent-prefs/git-operating-model.md`
- `.agent-prefs/session-execution-model.md`

## Expected Preference Files

Use this table as the current setup registry. Add new rows here when a new local preference type becomes expected.

| Preference | Local file | Ask when missing | Committed catalog/template |
|---|---|---|---|
| Git operating model | `.agent-prefs/git-operating-model.md` | Before pushing, opening pull requests, changing remotes, or choosing a branch strategy. | [git-operating-models.md](git-operating-models.md) |
| Session execution model | `.agent-prefs/session-execution-model.md` | Before substantial planning, substantial implementation, or multi-session/fresh-agent workflows. | This file |
| Map script shell model | `.agent-prefs/map-script-shell.md` | Before refreshing generated maps when both PowerShell and Bash scripts are available. | This file |

## Quick Setup

If `.agent-prefs/` contains no preference files other than `.gitkeep`, run a short setup before substantial planning or multi-session work.

Use [Expected Preference Files](#expected-preference-files) to decide which preferences exist. Ask only for preferences needed by the current workflow.

Write only the files the user selects. It is valid for a user to skip a preference and decide per session.

## Session Execution Models

Common local choices:

- `current-session`: keep planning and execution in the active session unless the user asks otherwise.
- `control-room-with-fresh-workers`: keep the active session as a lightweight control room; prepare reviewed handoff prompts and start fresh agent/thread workers for substantial planning or implementation.
- `ask-each-time`: ask before substantial planning or implementation.

## Session Execution Model Template

Create `.agent-prefs/session-execution-model.md` locally with content like:

```md
# Session Execution Model Preference

Preferred model: control-room-with-fresh-workers

Default behavior:
- Treat the current session as a lightweight control room for substantial planning or implementation.
- Before substantial planning, ask whether to continue here or prepare a reviewed handoff prompt for a fresh agent/thread.
- After worker execution, summarize the result back in the control-room session.
- Ensure completed file-changing work is committed locally before moving to the next handoff.

Exceptions:
- Handle small answers, inspections, and explicitly local edits in the current session.
```

Only commit `.agent-prefs/.gitkeep`; never commit the personal preference file.

## Map Script Shell Models

Map script shell selection controls which equivalent map generator family agents prefer when both PowerShell and Bash are available.

Common local choices:

- `prefer-powershell`: use `tools/maps/*.ps1` first; fall back to Bash when PowerShell is unavailable.
- `prefer-bash`: use `bash tools/maps/*.sh` first; fall back to PowerShell when Bash is unavailable.
- `platform-native`: use PowerShell on Windows and Bash on macOS/Linux unless the user asks otherwise.
- `ask-each-time`: ask before refreshing maps when both script families can run.

## Map Script Shell Template

Create `.agent-prefs/map-script-shell.md` locally with content like:

```md
# Map Script Shell Preference

Preferred model: platform-native

Default behavior:
- When both PowerShell and Bash map generators exist, choose the script family according to the preferred model.
- If the preferred shell is unavailable, use the equivalent fallback script and note the fallback.
- When verifying parity, run both script families regardless of the preferred default.
```

Only commit `.agent-prefs/.gitkeep`; never commit the personal preference file.
