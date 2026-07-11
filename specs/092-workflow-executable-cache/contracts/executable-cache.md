# Contract: Workflow Executable Cache

## Store behavior

The decorator preserves `IWorkflowExecutableStore` behavior:

- `FindAsync(id)`: return a resident positive result or coalesce one provider lookup. Do not retain not-found, failure, or cancellation.
- `SaveAsync(executable)`: persist first; after success, admit/replace by artifact ID.
- `DeleteAsync(id)`: delete first; after success, evict by artifact ID.
- `ListAsync(...)`: delegate directly and do not populate the cache.

Mutable workflow-definition and source-reference lookup remains outside this component and authoritative.

## Configuration

| Setting | Default | Contract |
|---|---:|---|
| `CacheWorkflowExecutables` | `true` for durable Groundwork providers | When false, use the provider directly. |
| `WorkflowExecutableCacheCapacity` | `256` | Must be positive when caching is enabled; maximum resident entry count. |

Disabling caching is the rollback path. A new service-provider/shell begins with an empty cache.

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
- Save/delete and cache-state changes are serialized sufficiently that a successful delete cannot be followed by serving the deleted resident value.
