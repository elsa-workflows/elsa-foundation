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
4. Only positive provider results and successful saves are admitted.
5. Successful delete removes the resident entry.
6. The cache never maps a mutable source reference to an artifact ID.
7. Cache state does not survive service-provider/shell replacement.
