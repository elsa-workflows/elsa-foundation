---
name: "elsa-create-agent-preference"
description: "Create or update an Elsa local agent preference. Use when a user states a stable personal workflow preference, asks agents to remember how they want sessions handled, or wants a preference recorded like the Git operating model without turning it into shared repo doctrine."
argument-hint: "Preference to record or template to create"
compatibility: "Requires elsa-foundation AGENTS.md and docs"
metadata:
  author: "elsa-foundation"
  source: "docs/skills/catalog.md#create-agent-preference"
user-invocable: true
disable-model-invocation: false
---

## User Input

```text
$ARGUMENTS
```

## Outline

1. Read `AGENTS.md#personal-operating-preferences`.
2. Read `docs/skills/catalog.md#create-agent-preference`.
3. Read `docs/reference/agent-preferences.md`.
4. Classify the request as a personal selection, reusable preference catalog/template, or shared repo rule.
5. For a personal selection, create or update a short ignored `.agent-prefs/<preference-name>.md` file and do not commit it.
6. For reusable options/templates, update committed reference docs before recording a personal selection.
7. Refuse to store secrets, credentials, environment variables, or project facts as agent preferences.

Never commit `.agent-prefs/*.md`; only `.agent-prefs/.gitkeep` may be committed.
