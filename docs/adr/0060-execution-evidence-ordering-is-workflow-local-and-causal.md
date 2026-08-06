---
status: proposed
date: 2026-08-05
decision_context: Runtime Execution Evidence domain grill approved by Sipke
---

# Execution evidence ordering is workflow-local and causal

## Context

One evidence session can contain concurrent workflow instances, child workflows, scheduled resumes,
and stimuli processed by different runtime workers. Assigning one strict session-wide semantic order
would require centralized coordination and would still risk encouraging tests to depend on incidental
scheduling order.

Consumers do need deterministic ordering inside a workflow and a reliable way to follow semantic
relationships across workflow boundaries.

## Decision

Every workflow instance has a strict order expressed as `(WorkflowCheckpointOrder,
CheckpointOrdinal)`: generic Runtime assigns the positive, stable monotonic checkpoint order before
enrichment, and each record in that checkpoint has a distinct deterministic ordinal. Timestamps,
hashes, lexical IDs, session counters, mutable reads, and delivery arrival order do not establish
semantic order.

Cross-workflow relationships use explicit causation references, such as the evidence identity of a
parent dispatch that caused a child workflow start. #1133 reserves the optional envelope field but
does not emit child/stimulus/scheduling causation; #1135 owns that coverage. Correlation metadata
groups related work without claiming that grouping establishes order.

Timestamps are diagnostic attributes and do not establish distributed semantic order. An opaque API
cursor provides stable query continuation but does not promise that unrelated records were committed
in the returned order.

## Considered options

- A global monotonic sequence per evidence session was rejected because it introduces a coordination
  bottleneck and unnecessary distributed-ordering guarantees.
- Timestamp ordering was rejected because clocks and concurrent commits cannot establish causality.
- Sink arrival order was rejected because retries and independent delivery can reorder records.

## Consequences

- Tests can assert exact order within one workflow and one checkpoint.
- Tests spanning workflows assert explicit causal chains rather than incidental interleaving.
- Storage providers need efficient session queries but do not need a distributed global sequencer.
- The common envelope must carry workflow sequence, checkpoint ordinal, causation identity, and
  correlation metadata separately from the API cursor.

## Linked decisions

- [Execution evidence is checkpoint-atomic and delivered at least once](0052-execution-evidence-is-checkpoint-atomic-and-at-least-once-delivered.md)
- [Execution Evidence API exposes neutral verification primitives](0057-execution-evidence-api-exposes-neutral-verification-primitives.md)
- [Evidence ordering](../glossary/elsa.md)
