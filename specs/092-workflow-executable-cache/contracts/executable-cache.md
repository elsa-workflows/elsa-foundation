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
| `CacheWorkflowExecutables` | `true` | When false, use the provider directly. Disable on nodes that cannot accept process-local immutable-artifact retention. |
| `WorkflowExecutableCacheCapacity` | `256` | Must be positive when caching is enabled; maximum resident entry count. |

Disabling caching is the rollback path. Cache state is shared by scoped store adapters within one
shell service provider and partitioned by the authorized `PersistenceScope`; a replacement shell
generation or process restart begins empty.

The cache is local to one shell service provider. Content-addressed IDs cannot be replaced with
different content, and mutable source references are checked before artifact lookup. A delete
evicts the node on which it is executed; another node may retain that immutable artifact until
local eviction/restart. Operators that require coordinated eager eviction can disable the cache
until issue #636 supplies a distributed invalidation protocol. Privileged/global reads bypass
shared values. Successful privileged scoped mutations invalidate their partition; global and
cross-scope mutations invalidate that artifact across every resident local partition.

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
