# Data Model: Bounded Workflow Executable Cache

No persistent data model is added.

## Runtime State

### Cache entry

- Artifact ID: immutable string key.
- Executable: immutable runnable workflow artifact returned by the provider.
- Recency node: position in the bounded least-recently-used list.

### In-flight load

- Artifact ID: same immutable key.
- Shared task: one provider lookup result for concurrent miss waiters.
- Terminal state: success with value, not found, failed, or cancelled; the entry is removed for every terminal state.

## Invariants

1. Resident entry count never exceeds configured capacity.
2. Every resident dictionary entry has exactly one recency node and vice versa.
3. A cache hit promotes that entry to most recently used.
4. Only positive provider lookup results are admitted.
5. Successful save and unconditional-delete operations remove the resident entry; guarded delete does so only when the provider reports success. The next lookup re-reads provider-authoritative state.
6. The cache never maps a mutable source reference to an artifact ID.
7. Resident cache entries do not survive service-provider/shell replacement; the replacement provider begins with an empty cache.
8. Root-write lease and deletion-guard state remains provider-owned and is never retained or synthesized by the cache.
