---
status: proposed
date: 2026-08-05
decision_context: Runtime Execution Evidence domain grill approved by Sipke
---

# Negative evidence claims require a complete gap-free range

## Context

Automated tests commonly need to verify that an activity did not execute, a duplicate stimulus was
not accepted, or a variable was not changed. A wait timeout proves only that the consumer did not
observe a match during the allotted time. Delivery lag, an open workflow, a sequence gap, or a failed
sink could produce the same result.

Treating timeout as absence would turn infrastructure timing into domain truth and create flaky
negative assertions.

## Decision

The Execution Evidence API distinguishes these wait outcomes:

- matching evidence observed;
- timeout with an inconclusive result;
- a completeness boundary reached without a match; and
- integrity failure, including a sequence gap or failed delivery.

Queries return an observed-through evidence cursor and integrity information. A consumer may make a
definitive absence claim only for a range bounded by terminal workflow evidence, an explicit settled
barrier, or completed evidence-session lifecycle and only when that range is gap-free.

An open range or timeout is never implicitly complete. A duplicate delivery does not make a range
incomplete when the stable evidence identifier proves it is a retry of an already observed record.

The precise mechanics for session completion and settled barriers belong in the relevant feature
specification, but they must preserve this semantic distinction in every provider and transport.

## Considered options

- Treating wait timeout as negative proof was rejected because it confuses elapsed client time with
  completed runtime behavior.
- Requiring every workflow to terminate before any negative assertion was rejected because suspended
  and long-running workflows need bounded verification points.
- Ignoring sequence gaps was rejected because a missing record could itself be the event for which
  the consumer is testing.

## Consequences

- Test libraries must represent an inconclusive timeout separately from a passing negative
  assertion.
- Evidence providers must expose ordering, gaps, and settlement information, not only event rows.
- The API needs cursor, integrity, session-lifecycle, and barrier contracts.
- Long-running workflows can still support negative assertions at explicit committed boundaries.

## Linked decisions

- [Execution evidence is checkpoint-atomic and delivered at least once](0052-execution-evidence-is-checkpoint-atomic-and-at-least-once-delivered.md)
- [Execution Evidence API exposes neutral verification primitives](0057-execution-evidence-api-exposes-neutral-verification-primitives.md)
- [Evidence completeness boundary](../glossary/elsa.md)
