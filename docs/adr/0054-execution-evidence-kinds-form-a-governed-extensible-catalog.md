---
status: proposed
date: 2026-08-05
decision_context: Runtime Execution Evidence domain grill approved by Sipke
---

# Execution evidence kinds form a governed extensible catalog

## Context

A closed enumeration of evidence kinds would force the foundational catalog to know every optional
Elsa feature and activity domain. An unrestricted event dictionary would allow incompatible payloads
to reuse names, make assertions depend on accidental shapes, and prevent safe evolution.

Execution evidence needs a stable baseline while preserving Elsa's module contribution model.

## Decision

Every execution-evidence record uses a common envelope containing a stable string kind and schema
version. The Execution Evidence `.Core` module owns the baseline workflow, activity, state, stimulus,
scheduling, incident, and checkpoint kinds as typed contracts.

Optional modules may register additional typed evidence kinds by referencing the Execution Evidence
contract module. Registrations declare the stable kind, schema version, payload contract, and capture
metadata required by policy. Unregistered ad hoc dictionary payloads are rejected.

Breaking payload changes require a new schema version. Consumers that do not understand a registered
kind can still inspect and filter its common envelope without interpreting its typed payload.

## Considered options

- A closed core enumeration was rejected because it would centralize knowledge of optional modules
  and require foundational releases for every extension.
- Arbitrary string names with dictionary payloads were rejected because they provide no governed
  contract, collision protection, or safe schema evolution.
- CLR type names as wire identities were rejected because refactoring code should not rename stored
  evidence contracts.

## Consequences

- Baseline evidence semantics remain stable and independently versioned.
- Kind registration and duplicate/conflicting registration validation become startup concerns.
- Typed SDKs can provide first-class support for known kinds while retaining forward-compatible raw
  envelope access.
- Contributors depend on Execution Evidence contracts; the Execution Evidence domain does not add
  knowledge of every contributing module.
- Catalog documentation and compatibility fixtures become part of the public evidence contract.

## Linked decisions

- [Execution evidence is checkpoint-atomic and delivered at least once](0052-execution-evidence-is-checkpoint-atomic-and-at-least-once-delivered.md)
- [Execution Evidence domain and evidence kind](../glossary/elsa.md)
