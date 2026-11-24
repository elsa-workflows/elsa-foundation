# Contract: adapters

Adapters carry metadata only. The skill's content lives in the skill folder.

## `.claude-plugin/marketplace.json` (repository root)

```json
{
  "name": "elsa-workflows",
  "owner": { "name": "Elsa Workflows" },
  "plugins": [
    {
      "name": "elsa3-migration",
      "source": "./plugins/elsa3-migration",
      "description": "Migrate Elsa 3 workflows and custom components to Elsa 4."
    }
  ]
}
```

## `plugins/elsa3-migration/.claude-plugin/plugin.json`

```json
{
  "name": "elsa3-migration",
  "version": "<dotnet-elsa version>",
  "description": "Migrate Elsa 3 workflows and custom components to Elsa 4."
}
```

The plugin declares no custom skill path: its default `skills/` folder holds exactly one skill,
`elsa3-workflows-migration/`.

## `SKILL.md` front matter

```yaml
---
name: elsa3-workflows-migration
description: Migrate Elsa 3 workflow definitions and custom Elsa 3 code (activities, JavaScript functions, variable types, storage drivers) to Elsa 4 with a local test migration. Use when someone moves from Elsa 3 to Elsa 4.
metadata:
  cli-version: "<dotnet-elsa version>"
---
```

## `.agents/skills/` users

`SKILL.md` and the README section tell them to copy the whole skill folder into their workspace's
`.agents/skills/elsa3-workflows-migration/`. Nothing is added to this repository's own `.agents/skills/`, which holds
contributor skills.
