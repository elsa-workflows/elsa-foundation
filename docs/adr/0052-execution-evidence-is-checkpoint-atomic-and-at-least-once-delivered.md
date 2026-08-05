---
status: proposed
date: 2026-08-05
decision_context: Runtime Execution Evidence domain grill approved by Sipke
---

# Execution evidence is checkpoint-atomic and delivered at least once

## Context

Automated tests must be able to treat the presence and absence of execution evidence as meaningful.
Publishing evidence before runtime state commits can expose facts for work that is later rolled back.
Publishing it best-effort after commit can silently omit facts for work that did commit. Exactly-once
delivery would move distributed coordination into every evidence sink and still would not remove the
need for idempotent consumers.

Elsa already separates runtime checkpoint persistence from post-commit delivery. Execution evidence
should use that durability boundary rather than create a parallel consistency model.

## Decision

The complete authoritative execution-evidence intent is recorded atomically with the runtime
checkpoint that makes its semantic facts true. An evidence-owned post-commit handler materializes
the intent into the query store with at-least-once delivery. The durable intent is sufficient to
recover materialization; the query-store row does not need to share the runtime checkpoint's physical
transaction.

For a workflow associated with an evidence session, failure to prepare or record evidence required
by the active capture profile fails the runtime checkpoint. There is no best-effort canonical
evidence mode. Once the checkpoint and its pending delivery work commit, later delivery failure does
not roll back workflow state; delivery retries, and the evidence session remains incomplete until the
record becomes available or an integrity failure is declared.

Every evidence record has a stable evidence identifier and a workflow-local sequence number. The
post-commit handler and store materialize idempotently by evidence identifier and use the sequence to
order records and detect gaps. A sink must not infer that a duplicate delivery represents a second
semantic occurrence.

An explicitly non-durable, in-memory QA implementation may lose already committed evidence when its
process fails. It must still publish only committed evidence, retain stable identities and sequence
numbers, and disclose that crash loss prevents completeness claims across the failure boundary.

## Considered options

- Best-effort emission after commit was rejected because missing evidence would be
  indistinguishable from missing runtime behavior.
- Continuing a checkpoint after required evidence recording fails was rejected because a successful
  checkpoint would then violate the evidence contract without a recoverable delivery record.
- Emission before commit was rejected because rolled-back behavior could become observable as fact.
- Exactly-once delivery was rejected because it imposes distributed transaction or sink-specific
  coordination without eliminating the need for idempotency.
- Treating the in-memory collector as durable was rejected because process loss invalidates that
  guarantee; it is a useful QA adapter, not the durable contract.

## Consequences

- Evidence creation participates in the runtime checkpoint commit path; external delivery does not.
- Enabling an evidence session deliberately makes evidence-recording availability part of checkpoint
  success.
- Evidence delivery can be retried independently without changing committed workflow state.
- Consumers must tolerate duplicate delivery and can detect, but not silently conceal, sequence
  gaps.
- A successful checkpoint cannot silently omit evidence required by the enabled capture policy.
- Non-durable collectors can support fast initial testing but cannot prove completeness across a
  process failure.
- Storage adapters must preserve evidence identity, workflow-local ordering, and pending delivery
  state consistently with [ADR 0020](0020-runtime-checkpoint-commit-post-commit-work.md).

## Validation note (2026-08-05)

A disposable prototype validated the existing generic `IRuntimeCheckpointCommitEnricher` and opaque
runtime post-commit intent path with both the in-memory and Groundwork checkpoint stores. It proved
deterministic replay, rejection of skipped commits containing evidence work, idempotent redelivery
after a simulated crash following the evidence write, and intact opaque kind/payload persistence in
Groundwork. No new Runtime extension point or evidence-specific Runtime branch was required. See the
[prototype findings](../reports/runtime-execution-evidence-seam-prototype.md).

## Linked decisions

- [Runtime checkpoint commit records post-commit work without inline delivery](0020-runtime-checkpoint-commit-post-commit-work.md)
- [Checkpoint-Gated Activity Execution Inspection](0001-checkpoint-gated-activity-execution-inspection.md)
- [Execution evidence](../glossary/elsa.md)
