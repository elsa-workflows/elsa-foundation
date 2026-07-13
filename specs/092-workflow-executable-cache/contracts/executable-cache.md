# Contract: Workflow Executable Cache

## Store behavior

The decorator preserves `IWorkflowExecutableStore` behavior:

- `FindAsync(id)`: return a resident positive result or coalesce one provider lookup. Do not retain not-found, failure, or cancellation.
- `SaveAsync(executable)`: persist first; after success, evict by artifact ID. The idempotent provider may retain an existing value, so the caller-supplied object is never admitted directly.
- `DeleteAsync(id)`: delete first; after the provider returns, evict by artifact ID, including a non-throwing not-found result.
- Root-write lease and deletion-guard transitions: delegate directly to the provider; these are durable retention-safety state, not cache state.
- `DeleteAsync(guard, now)`: delegate the guarded delete and evict by artifact ID only when the provider reports successful deletion.
- `ListAsync(...)`: delegate directly and do not populate the cache.

Mutable workflow-definition and source-reference lookup remains outside this component and authoritative.

## Configuration

| Setting | Default | Contract |
|---|---:|---|
| `CacheWorkflowExecutables` | `true` for SQLite reference features; `false` for PostgreSQL/distributed and legacy direct registrations | When false, use the provider directly. PostgreSQL hosts opt in only with an accepted immutable-retention/invalidation policy. |
| `WorkflowExecutableCacheCapacity` | `256` | Must be positive when caching is enabled; maximum resident entry count. |

Disabling caching is the rollback path. A new service-provider/shell begins with an empty cache.

The cache is process-local. Content-addressed IDs cannot be replaced with different content, and mutable source references are checked before artifact lookup. A delete evicts the node on which it is executed; another opted-in node may retain that immutable artifact until local eviction/restart, so PostgreSQL features remain disabled by default.

## Telemetry

Stable meter observations:

- cache request count, tagged only by `result=hit|miss`;
- eviction count, tagged only by bounded `reason=capacity|delete|save` when applicable;
- provider-load duration, tagged only by bounded `outcome=found|not_found|failed|cancelled`.

Workflow IDs, artifact IDs, source references, payloads, exception messages, and connection details are forbidden metric dimensions.

## Concurrency

- One provider load may exist per artifact ID.
- Cancelling one waiter cancels only that wait.
- Completion removes the in-flight record even when the provider completes synchronously or throws.
- Successful save and unconditional delete advance a mutation generation and invalidate the key. Guarded delete does so only when deletion succeeds. A lookup already in flight may complete for its original caller, but it cannot re-admit a pre-mutation result into the cache.
