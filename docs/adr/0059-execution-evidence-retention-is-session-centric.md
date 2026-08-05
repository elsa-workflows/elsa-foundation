---
status: proposed
date: 2026-08-05
decision_context: Runtime Execution Evidence domain grill approved by Sipke
---

# Execution evidence retention is session-centric

## Context

Independent record expiry can introduce sequence gaps into an otherwise valid evidence range and
make negative assertions unreliable. QA evidence can contain captured values and should not remain
indefinitely by accident.

Portable storage-pressure detection would require provider-specific capacity metrics and thresholds.
Filesystems, relational databases, document stores, and in-memory collectors do not expose one
reliable shared definition of pressure, so building it into the foundational contract would add
speculative complexity.

## Decision

Execution evidence is retained and deleted as one complete evidence session. A module setting defines
a short default retention period that starts when the session completes. A session may request a
shorter period but does not silently request indefinite retention.

The API supports explicit deletion of completed sessions. Scheduled cleanup removes session metadata,
evidence records, and associated delivery state together after the retention period. It does not
expire individual evidence records from a retained session.

The initial contract has no storage-pressure interface, capacity quota, or automatic capacity-based
eviction. Infrastructure monitoring owns capacity alerts. If a provider rejects a write, the strict
checkpoint-recording or post-commit-delivery failure rules apply.

## Considered options

- Per-record TTL was rejected because partial expiry destroys range completeness.
- Indefinite retention by default was rejected because QA data should have a bounded lifecycle.
- Portable storage-pressure detection and automatic eviction were deferred because their behavior is
  provider-specific and there is no demonstrated need for a shared abstraction.
- Silently evicting active-session records was rejected because it would violate evidence integrity.

## Consequences

- Retention cleanup operates efficiently on a session ownership key.
- A retained session remains whole and gap analysis is not confused by normal expiry.
- Operators monitor provider capacity using their ordinary infrastructure tooling.
- Storage exhaustion is visible as a capture or delivery failure rather than hidden data loss.
- Quotas and provider-specific pressure management require a later justified work unit.

## Linked decisions

- [Execution evidence is checkpoint-atomic and delivered at least once](0052-execution-evidence-is-checkpoint-atomic-and-at-least-once-delivered.md)
- [Negative evidence claims require a complete gap-free range](0058-negative-evidence-claims-require-a-complete-gap-free-range.md)
- [Evidence retention](../glossary/elsa.md)
