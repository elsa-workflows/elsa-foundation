# Runtime Execution Agent Provider Contract

This slice introduces provider-neutral contracts for actor-style workflow execution agents.

Required guarantees:

- One workflow execution ID maps to one active mailbox/agent.
- Commands are delivered through envelopes with idempotency and optional sequence metadata.
- Providers expose capabilities without coupling Elsa Core to actor frameworks.
- Passivation/deactivation names safe runtime boundaries.
- Elsa checkpoint state remains authoritative.
