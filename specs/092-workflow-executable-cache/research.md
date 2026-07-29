# Research: Bounded Workflow Executable Cache

## Decision 1: Cache immutable executable artifacts, not source references

`IWorkflowExecutableStore.FindAsync` receives an artifact ID after higher-level resolution. That ID represents immutable runnable content, making it safe to retain without a distributed invalidation protocol. Workflow-definition and source-reference mappings remain mutable authorities and are intentionally outside scope.

## Decision 2: Add dedicated shared bounded state with scoped adapters

The repository's generic cache abstraction does not provide both a hard capacity and same-key async miss coalescing. A dedicated `WorkflowExecutableCache` owns persistence-partitioned LRU/in-flight state for one shell service provider. Scoped `CachingWorkflowExecutableStore` adapters preserve request lifetimes, and `GroundworkWorkflowExecutableCacheLoader` owns an independent persistence-operation scope for a shared provider miss.

## Decision 3: Coalesce loads without sharing caller cancellation

One provider task is created per missed persistence-partition/artifact key. Each caller awaits it with its own cancellation token, while the provider operation uses an independent token. The in-flight entry is removed after success, null, cancellation, or failure, preventing permanent poisoning and allowing later retries.

## Decision 4: Wrap durable Groundwork providers only

The in-memory store already retains executable objects and gains no deserialization benefit. Groundwork is the shared durable implementation used by SQLite and PostgreSQL runtime/unified features, so wrapping its concrete store produces broad runtime benefit without changing custom provider registrations.

## Decision 5: Prefer entry-count capacity for v1

Executable graphs do not expose a reliable byte size after deserialization. A positive entry-count capacity is deterministic, portable, and operationally understandable. A future measured need may add weight-based admission behind the same options surface.

## Alternatives Rejected

- **Cache HTTP route lookup only**: route lookup is already in-memory after synchronization and does not remove executable provider/deserialization cost.
- **Cache materialized workflow instances**: instances are mutable execution state and cannot safely be shared.
- **Negative caching**: would need expiry/invalidation semantics and could hide newly persisted artifacts; misses are instead retried.
- **Unbounded concurrent dictionary**: fast but violates the explicit bounded-memory requirement.
- **Distributed cache**: adds serialization and coordination to immutable objects without evidence that process-local reuse is insufficient.
