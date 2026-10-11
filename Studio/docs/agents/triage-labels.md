# Triage Labels

GitHub is the source of truth for this repository's labels. Verify the current set before applying one:

```shell
gh label list --repo elsa-workflows/elsa-foundation-studio
```

The workflow-specific labels currently defined are:

| Label | Meaning |
| --- | --- |
| `ready-for-agent` | Fully specified and ready for an AFK agent |
| `wontfix` | Will not be actioned |
| `needs-triage` | Filed by an agent and waiting for a maintainer to decide on it |
| `auto-qa` | Filed by elsa-foundation's [UX QA routine](https://github.com/elsa-workflows/elsa-foundation/blob/main/docs/skills/catalog.md#ux-qa-routine) |
| `size:S` / `size:M` / `size:L` | One small PR / one larger PR / needs splitting first |

The UX QA routine files a defect as `ready-for-agent` + `auto-qa` only when a second agent reproduced it and the fix needs no design choice. It files everything else as `needs-triage` + `auto-qa`. The [UX fix routine](ux-fix-routine.md) works the `ready-for-agent` + `auto-qa` issues.

There are no dedicated `needs-info` or `ready-for-human` labels. Do not invent or apply missing labels when a skill uses one of those canonical roles; describe the state in a comment and use an existing standard label only when its meaning is an exact fit.
