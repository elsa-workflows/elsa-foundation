# Runtime In-Process Agent Provider Contract

This slice adds the default provider-neutral in-process execution agent implementation.

Required guarantees:

- One active in-process agent per workflow execution ID.
- Accepted commands are processed sequentially for that agent.
- Duplicate idempotency keys are reported as `Duplicate` without reprocessing.
- Passivation removes the active agent and makes the old agent stop accepting work.
- Runtime state remains Elsa checkpoint state; no actor framework persistence is introduced.
