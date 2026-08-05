---
status: proposed
date: 2026-08-05
decision_context: Runtime Execution Evidence domain grill approved by Sipke
---

# Baseline execution evidence records committed semantic transitions

## Context

Automated verification needs to explain meaningful workflow behavior without coupling tests to
runtime implementation mechanics. A catalog that mirrors every internal notification would be noisy,
expensive, and unstable across scheduler or pipeline refactors. A catalog limited to final workflow
status would omit the activity, state, stimulus, and causation facts needed to diagnose failures.

## Decision

The baseline catalog is organized around committed semantic transition families:

- workflow lifecycle: started, suspended, resumed, completed, faulted, and cancelled;
- activity lifecycle: scheduled, started, completed, faulted, cancelled, and skipped;
- state mutation: variable and durable-value writes plus policy-selected input and output values;
- bookmark lifecycle: created, consumed, expired, and removed;
- incident lifecycle: created, resolved, and dismissed;
- causation and stimuli: workflow dispatch, child-workflow request, signal or trigger acceptance,
  rejection and deduplication, and timer scheduling and firing; and
- durability: checkpoint commit and explicit settled barriers.

The catalog excludes state reads, heartbeat events, middleware traversal, scheduler polling, ordinary
log messages, method calls, and exceptions whose effects do not commit. Multiple transitions that
become true in one checkpoint remain distinct typed evidence records with stable checkpoint-local
ordinals.

Exact kind strings, payload contracts, and rollout allocation are specified by feature work units
and compatibility fixtures rather than frozen by this architectural decision.

## Considered options

- Mirroring all runtime notifications was rejected because it exposes implementation mechanics and
  creates high-volume unstable contracts.
- Recording only terminal workflow outcomes was rejected because it cannot verify or explain the
  intermediate behavior under test.
- Periodic full-state snapshots were rejected as the baseline because they obscure which semantic
  transition occurred and add unnecessary payload cost.

## Consequences

- Tests can verify lifecycle, value flow, stimuli, causation, incidents, and durability without
  parsing logs.
- Runtime refactors that preserve semantic transitions do not require evidence-contract changes.
- Source-domain adapters must translate existing generic observations into these stable families.
- Feature specs must define kind identifiers, schemas, and fixtures before implementation.

## Linked decisions

- [Execution evidence kinds form a governed extensible catalog](0054-execution-evidence-kinds-form-a-governed-extensible-catalog.md)
- [Execution Evidence integrates through domain-owned adapters](0055-execution-evidence-integrates-through-domain-owned-adapters.md)
- [Baseline evidence catalog](../glossary/elsa.md)
