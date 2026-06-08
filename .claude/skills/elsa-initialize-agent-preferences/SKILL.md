---
name: "elsa-initialize-agent-preferences"
description: "Bootstrap Elsa local agent preferences. Use when .agent-prefs has no preference files other than .gitkeep, or when a user asks to set up repository-local agent preferences for Git workflow, session execution model, or other personal workflow choices."
argument-hint: "Preference setup scope"
compatibility: "Requires elsa-foundation AGENTS.md and docs"
metadata:
  author: "elsa-foundation"
  source: "docs/skills/catalog.md#initialize-agent-preferences"
user-invocable: true
disable-model-invocation: false
---

## User Input

```text
$ARGUMENTS
```

## Outline

1. Read `AGENTS.md#personal-operating-preferences`.
2. Read `docs/skills/catalog.md#initialize-agent-preferences`.
3. Read `docs/reference/agent-preferences.md#expected-preference-files` to discover known preference files and ask triggers.
4. Read `docs/reference/git-operating-models.md` only when Git workflow setup is relevant.
5. If `.agent-prefs/` already has preference files besides `.gitkeep`, summarize the existing preferences and do not overwrite them without explicit user approval.
6. Ask brief setup questions only for preferences needed by the current task.
7. Create selected local `.agent-prefs/*.md` files using `docs/skills/catalog.md#create-agent-preference`.
8. Leave skipped preferences unset; deciding per session is valid.

Never commit `.agent-prefs/*.md`; only `.agent-prefs/.gitkeep` may be committed.
