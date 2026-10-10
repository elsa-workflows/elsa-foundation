# Research: Groundwork Runtime Evaluation And Hardening

## Decision: G8 Is A Go/No-Go Evaluation Slice

Rationale: The roadmap explicitly requires runtime stores to receive a decision before any hot-path migration. The current runtime seam is still evolving, so moving storage now would freeze premature assumptions.

Alternatives considered: Implementing a Groundwork checkpoint writer. Rejected because runtime checkpoint atomicity and post-commit semantics need benchmark and retry evidence first.

## Decision: Runtime Continuation State Is Benchmark-Gated

Rationale: Checkpoints and bookmarks are durable continuation state. They might fit Groundwork with physicalization later, but only after atomic commit, concurrency, and recovery behavior are proven.

Alternatives considered: Treating checkpoints as ordinary documents. Rejected because checkpoint commits combine state changes, versioning, and post-commit intent dispatch.

## Decision: Operational Streams Stay Specialized By Default

Rationale: Queues, outbox records, execution logs, leases, and locks need provider-specific throughput, ordering, retention, retry, and coordination behavior.

Alternatives considered: Storing operational streams in portable documents. Rejected because the portable document contract does not define queue/lease/outbox semantics.

## Decision: Runtime-Defined Business Data Remains Groundwork Default

Rationale: Runtime-defined business data was validated in G6 as document/index storage and does not carry workflow continuation semantics.

Alternatives considered: Marking all runtime-adjacent storage as specialized. Rejected because business data and workflow operational state have different risk profiles.
