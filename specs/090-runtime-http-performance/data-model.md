# Data Model: Runtime HTTP Hot-Path Performance

This feature adds no new durable Elsa domain entity or persisted wire format. It introduces one shell-scoped configuration model and one tooling-only evidence record.

## Checkpoint persistence settings

| Field | Type | Default | Validation | Meaning |
|---|---|---:|---|---|
| `Mode` | `Immediate` or `Coalesced` | `Immediate` | Known value | Selects the runtime checkpoint persistence strategy for the shell. |
| `MaxSegmentCheckpoints` | positive integer | `50` | `> 0` when Coalesced | Maximum replayable checkpoints buffered before an intermediate flush. |

### State transitions

Settings are applied when a shell service provider is built. Changing settings rebuilds the shell; an already-running execution never changes policy mid-drain.

```text
configured → validated → post-configured → active for new shell executions
                 └── invalid → shell startup failure
```

## Performance evidence run

Tooling emits a non-domain result with:

- timestamp and source revision;
- operating system, architecture, runtime version, and processor summary;
- endpoint URL, provider, policy, segment cap, warmup count, and sample count;
- cold request duration;
- warm p50, p95, p99, maximum, and mean durations;
- response validation status;
- optional physical checkpoint-marker delta and commits per request;
- whether the budget was enforced and whether it passed.

This evidence is a report artifact, not application state and not a Groundwork document kind.

## Existing durable models preserved

- Runtime checkpoint commit marker
- Workflow execution state
- Activity execution state and inspection projection
- Scheduler state and work item
- Post-commit outbox item
- Durable HTTP response instruction
- Ownership and fencing state

Their persisted shapes and identities do not change.
