# Diagnostics durable-history v1.3 successor

`diagnostics-durable-history-v1.3.json` supersedes the retained v1.2 workload without rewriting it.
The v1.2 runner compared `IStructuredLogStore.GetHighWaterMarkAsync()` with the number of rows appended
inside the primary scope. That comparison was invalid: the public contract defines high-water as the
maximum committed `Sequence`, and a provider may allocate those sequences across storage scopes. The
workload deliberately seeds 200 secondary-scope records before its 101,000 primary-scope records, so
Groundwork correctly reports primary high-water 101,200 while the retained EF comparator, whose scopes
use separate SQLite databases, correctly reports 101,000.

The v1.3 runner retains the same public operations and scale, but records the provider-neutral invariant
`structuredLogHighWaterMatchedMaximumCommittedSequence`. Each append writer retains its maximum
acknowledged primary sequence; the store's high-water must equal the maximum across writers and must
remain unchanged after trim and reopen. Absolute allocator coordinates are intentionally absent from the
cross-adapter observable digest. Scope isolation is still proven independently by the primary queries.

The new seed, input fingerprint, result digest, raw source digest, and explicit v1.2 lineage prevent v1.2
artifacts from being relabelled as v1.3 evidence. Correctness and native-plan capture must be rerun from a
clean exact v1.3 head before any performance verdict can consume the successor.
