# Data Model: Groundwork Runtime Evaluation And Hardening

## Runtime Store Candidate

Represents one persistence surface being evaluated.

Fields:

- `Name`: Human-readable candidate name.
- `Family`: Groundwork workload family.
- `RequiresAtomicCommit`: Whether writes must atomically persist multiple runtime state changes.
- `RequiresPostCommitDispatch`: Whether writes are coupled to post-commit intent dispatch.
- `IsOperationalStream`: Whether the store behaves like a queue, outbox, log, or event stream.
- `RequiresLeaseOrMailbox`: Whether the store coordinates ownership, locks, or execution agents.
- `AllowsGroundworkDefault`: Whether the candidate is already known to fit the portable document-store default.

## Runtime Store Evaluation

Represents the recommendation for a candidate.

Fields:

- `Candidate`: The evaluated candidate.
- `Recommendation`: Groundwork candidate category.
- `Decision`: Go, benchmark gate, or no-go.
- `Reason`: Explanation for the recommendation.
- `EvidenceGates`: Required evidence before implementation or migration.

## Evidence Gate

Represents migration prerequisite evidence.

Fields:

- `Kind`: Benchmark, concurrency, retry, diagnostics, or operations.
- `Description`: Specific evidence requirement.

Validation:

- Benchmark-gated candidates must include benchmark and concurrency gates.
- Specialized-provider candidates must include an operational reason and at least one gate.
