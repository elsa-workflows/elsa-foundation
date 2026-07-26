---
status: accepted
date: 2026-07-26
decision_context: Issue 1016 grill-with-docs review approved by Sipke
---

# Runtime alterations use snapshotted atomic jobs

An alteration plan first captures its complete target cohort using durable keyset pagination, persists and deduplicates each matching workflow execution identity, and seals that immutable snapshot before it dispatches any alteration job. Target capture and execution are separate durable lifecycle phases, so a plan can select an operationally unbounded number of executions without an arbitrary API limit and without mutations from an earlier page changing membership in a later page.

The sealed plan produces one durable job per target. Each job preserves the submitted alteration order, preflights the complete sequence, and applies it atomically through the workflow execution's single-writer boundary; a failure commits none of the alterations for that execution, records the failing alteration, and marks later alterations as skipped, while other jobs continue independently. Workflow execution migration is admitted only for a suspended, fully quiescent execution and an exact same-definition target artifact whose nodes, activity contracts, bookmarks, scopes, and variable declarations are identity-compatible. Variable modification is limited to workflow-scoped values addressed by stable variable reference key and guarded by the expected root-frame revision. Activity rescheduling supersedes only a scheduled, waiting, suspended, or faulted execution and creates a distinct server-identified execution with lineage to it; running, completed, cancelled, and recovered executions are ineligible, and existing history is never rewritten. A faulted activity may be rescheduled only while its workflow remains non-terminal and its source incident is already non-blocking; incident recovery/redrive is a separate explicit operation, and rescheduling never resolves an incident or implicitly recovers a terminal faulted workflow. Scheduling a new activity requires both an authored node identity and an explicit active parent activity-execution identity; the server derives its scope, provenance, pinned artifact, fresh execution identity, and normally authored input evaluation. Clients cannot supply an execution identity, arbitrary input payload, or an inferred parent context. `CancelWorkflow` is exclusive within a job; `Migrate` may appear at most once and must be first; variable modification and activity schedule/reschedule operations may follow and are validated against the resulting artifact and state, while duplicate or conflicting targets fail preflight.

Each submitted alteration is a stable wire envelope consisting of a kind, schema version, and payload. Trusted host modules register handlers for supported envelopes; the initial feature supplies the built-in handlers. Persisted contracts never use CLR type names and never accept client-supplied executable code. An unknown or unsupported kind or schema version rejects the submission before target capture starts.

Deferred execution payloads are stored only in a protected runtime representation and are redacted
from plan, job, result, and audit reads. The tenant-scoped canonical request hash used for
idempotency is stored separately from the protected execution payload.

Submission requires workflow-runtime management permission and evaluates the caller's tenant and execution-authority scope once. The plan persists that sealed scope and the submitting operator's provenance for audit. Background workers run as trusted system components constrained to the sealed scope; they do not depend on, refresh, or repeatedly re-authorize the submitter's session. A later permission revocation does not reinterpret an accepted plan, while a currently authorized operator may cancel work that has not begun.

Submission requires a tenant-scoped idempotency key. Repeating the key with the same canonical request returns the existing plan; repeating it with different content returns a conflict and creates no additional work.

Plan, job, and execution-command identities are deterministic and delivery is at least once. Transient failures that occur before a durable commit may retry; validation failures are final. When commit acknowledgement is uncertain, the worker reconciles against durable checkpoint evidence before deciding whether to retry, and never blindly applies an alteration a second time.

Plan cancellation is cooperative. It stops target capture and prevents undispatched jobs from starting, but a job already executing inside a workflow actor finishes its atomic checkpoint. Completed job results remain durable; the cancelled plan reports succeeded, failed, and cancelled counts rather than rewriting prior outcomes.

Durable plan and job results contain typed outcome codes, policy-safe messages, timestamps, submitting-operator provenance, and structural identifiers. They do not copy variable values before or after modification, arbitrary payloads, stack traces, or secrets; detailed exceptions remain in protected server logs.

The server exposes one durable submission path and plan/job read APIs. It does not expose a separate immediate `run` endpoint; clients that need synchronous ergonomics submit and wait by polling the same durable contracts, so disconnects and retries do not create a second execution or audit model.

## Considered options

- Live query evaluation during job execution was rejected because target membership, authorization, totals, and replay behavior would drift.
- A fixed target-count limit and silent truncation were rejected. Durable paged capture, sealing, backpressure, and deployment-level storage quotas provide operational control without changing API semantics.
- Elsa 3-style partial application was rejected because earlier mutations can survive a later failure and job completion can become ambiguous.
- Active-execution migration and caller-supplied mapping were rejected for the initial contract because Foundation's split runtime state has no safe mapping language or rollback boundary for them.
- Client-selected activity execution identities, arbitrary schedule-time inputs, and ambiguous inferred parent contexts were rejected because the runtime owns structural identity, authored input evaluation, and scheduling provenance.
- Implicit workflow recovery during activity rescheduling was rejected because reopening a terminal workflow is a separate lifecycle mutation with its own audit and recovery contract.
- Incident resolution inside activity rescheduling was rejected because incident recovery/redrive is a separate explicit operation and the incident-strategies work unit owns that contract.
- Persisted CLR alteration types and client-supplied executable alterations were rejected because they couple storage to implementation types and cross the runtime's trusted-code boundary.
- An unaudited immediate-run endpoint was rejected because it duplicates execution semantics and makes request timeouts, disconnects, and retries ambiguous.

## Linked decisions

- [Workflow value flow](0045-workflow-value-flow-uses-role-owned-bindings-and-immutable-invocation-records.md)
- [Runtime execution pipelines](0029-runtime-execution-flows-through-the-pipelines.md)
- [Runtime burst and single-writer drain](0031-runtime-burst-execution-sticky-single-writer-drain-with-in-process-fast-path.md)
- [Runtime checkpoint cadence](0032-runtime-checkpoint-cadence-is-policy-driven-per-workflow.md)
- [Runtime Alterations program goal](../program-goals/runtime-alterations.md)
- [Runtime Alterations specification](../../specs/141-runtime-alterations/spec.md)
