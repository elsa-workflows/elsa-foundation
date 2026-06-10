# Contract: Runtime Operational Recovery And Post-Commit Outbox

Runtime separates operational reliability from workflow/domain retry:

```text
lost lease / stale heartbeat -> recovery candidate -> requeue from last checkpoint
activity failure policy -> domain retry decision
```

Post-commit intent delivery follows:

```text
record intent -> checkpoint commit succeeds -> deliver intent -> mark delivered
```

Rules:

- Operational recovery does not increment domain retry counters.
- Recovery scans carry separate lease and heartbeat timeout inputs.
- Drain/quiescence stops new work at safe boundaries without rewriting workflow state semantics.
- Outbox delivery state is provider-facing delivery state, not workflow variable state.
- Outbox delivery results carry failure messages only for failed outcomes.
- Wait-dependent post-commit intents carry a durable wait dependency and failure policy.
