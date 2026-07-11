# Contract: Runtime HTTP Performance Controls

## Shell feature

Stable feature identity: `WorkflowsRuntimeCheckpointPersistence`

Dependency: `WorkflowsRuntimeApi`

Settings:

```json
{
  "WorkflowsRuntimeCheckpointPersistence": {
    "Mode": "Coalesced",
    "MaxSegmentCheckpoints": 50
  }
}
```

### `Mode = Immediate`

- Leaves the runtime's selected provider stores undecorated.
- Resolves `ImmediateRuntimeCheckpointPersistencePolicy`.
- Preserves the existing one-decided-checkpoint-at-a-time behavior.

### `Mode = Coalesced`

- Decorates the selected checkpoint commit store, scheduler queue, outbox store, workflow state store, activity state store, durable value store, and scheduler state store after provider configuration.
- Resolves `CoalescingRuntimeCheckpointPersistencePolicy`.
- Uses `MaxSegmentCheckpoints` to bound buffering and replay.
- Never changes the mandatory-boundary set.

### Configuration failures

- Unknown `Mode`: shell startup fails with the feature and supplied value named.
- `MaxSegmentCheckpoints <= 0` in Coalesced mode: shell startup fails with the required range.
- Required runtime store missing: shell startup fails through the existing coalescing registration guard.

## Performance command

The command accepts:

```text
--url <published-endpoint>
--expected-body <text>
--warmup <count>
--requests <count>
--policy <label>
--segment-cap <count>
--provider <label>
[--groundwork-db <path>]
[--output-json <path>]
[--output-markdown <path>]
[--enforce-p95-ms <milliseconds>]
```

Exit status is non-zero when the endpoint response is invalid, a required tool is unavailable, or an explicitly requested budget is missed. Without `--enforce-p95-ms`, measured latency is reported but does not fail the command.
