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

There are no dedicated `needs-triage`, `needs-info`, or `ready-for-human` labels. Do not invent or apply missing labels when a skill uses one of those canonical roles; describe the state in a comment and use an existing standard label only when its meaning is an exact fit.
