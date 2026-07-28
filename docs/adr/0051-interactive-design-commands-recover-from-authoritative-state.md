---
status: accepted
date: 2026-07-28
decision_context: Workflow-authoring resilience grill approved by Sipke
---

# Interactive design commands recover from authoritative state

Ordinary interactive workflow-design commands guarantee an atomic server-side state transition,
authorization, validation, uniqueness, and optimistic-concurrency enforcement. They do not
guarantee exactly-once command execution or exact replay of a previously returned response.

When a response is lost after an interactive design command may have committed, the client recovers
by rereading authoritative design state. Commands must therefore expose a stable target identity or
a deterministic lookup by which their committed outcome can be found. A create operation that
cannot otherwise be located after an ambiguous response should use a client-known or deterministically
derived resource identity rather than requiring a permanent server-side response-replay record.

This decision applies to ordinary workflow-editor and designer mutations, including routine create,
edit, discard, restore, and delete interactions. It does not set the reliability contract for
publication or promotion, automated import/reconciliation, runtime command delivery, bulk
administrative work, or other long-running operations. Those boundaries may use durable receipts,
idempotency records, or replay when an explicit delivery model and failure analysis justify them.

## Considered options

- Keeping a durable operation ledger for every design command was rejected. It turns routine
  editor requests into a permanent command-replay subsystem, adds an unbounded document family and
  request/result fingerprinting, and provides little value when users can reconcile from current
  design state.
- Leaving recovery entirely to client-local bookkeeping was rejected. A client can retain request
  intent, but it cannot prove whether an unacknowledged server transaction committed. Recovery
  therefore depends on server-owned authoritative state and a stable way to locate it.
- Retrying every ambiguous command blindly was rejected. Create and state-transition commands may
  not be naturally repeatable, and a retry must not silently create a second resource or overwrite
  a concurrent edit.

## Consequences

The current shared `designOperation` ledger is not the default resilience mechanism for interactive
design commands. A follow-up work unit should inventory its callers, retain it only for separately
justified boundaries, revise affected API contracts, and define a safe migration or retirement path
for existing ledger documents. That work must preserve atomic multi-document writes, optimistic
concurrency, persistence uniqueness constraints, and typed conflict outcomes.

Client applications should treat timeout or disconnect as an unknown outcome, reload the target or
its deterministic lookup, and then either accept the committed state, reapply the still-relevant
intent against the new revision, or report a conflict. Client-generated operation keys may remain an
ergonomic aid for boundaries that explicitly support replay, but endpoint-generated opaque keys do
not make a later client retry replay-safe.

## Linked decisions

- [Design command recovery](../glossary/elsa.md)
- [Author-requested forward workflow versions](0050-author-requested-forward-workflow-versions.md)
- [Workflow definitions reconcile from and export to Git](0034-workflow-definitions-reconcile-from-and-export-to-git.md)
