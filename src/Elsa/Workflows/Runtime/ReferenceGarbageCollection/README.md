# Workflow executable reference garbage collection

This optional Runtime feature schedules physical reclamation of workflow executable artifacts. The collector itself is registered by the Runtime composition root and can also be invoked by a host-owned scheduler.

Artifact lifetime is derived from durable roots, not from publication state alone. A sweep retains an artifact while either a live source reference or a retained workflow execution points to it. Every retained execution status is a root; completed, canceled, and faulted executions remain inspectable until the host's execution-retention policy deletes their records.

The sweep is deliberately conservative:

1. prune expired or retired source-reference records;
2. obtain distinct pinned artifact IDs through the execution-state store's provider-efficient query;
3. exclude artifacts inside creation/staging grace;
4. select artifacts with no root;
5. atomically acquire a provider-backed deletion guard;
6. recheck both root sets while new root-write leases are blocked; and
7. physically delete only through the matching conditional guard.

Canonical root writers acquire a provider-backed lease before committing a source reference or workflow-execution pin. A lease that wins prevents deletion from beginning; a deletion guard that wins prevents an uncoordinated root write from being persisted. Provider CAS makes this ordering valid across processes. If a root query or final guard fails, the artifact remains stored and a later sweep retries it. Expired leases and guards are recovered conservatively after their configured crash-recovery ceilings.

## Host configuration

Compose `WorkflowsRuntimeReferenceGarbageCollection` alongside the Tasks feature to run the recurring pump. Its settings control the normal sweep interval, maximum failure backoff, and artifact creation/staging grace period. Hosts that do not compose the feature may resolve `IWorkflowExecutableReferenceGarbageCollector` and schedule `SweepAsync` themselves.

Persistence providers replacing `IWorkflowExecutionStateStore` must implement distinct retained-root enumeration without loading every complete workflow-execution record, and must keep that projection consistent with execution save and deletion.

See [ADR 0040](../../../../../docs/adr/0040-one-artifact-store-with-reference-derived-lifetime.md) and the Runtime [extension-point catalog](../EXTENSION_POINTS.md).
