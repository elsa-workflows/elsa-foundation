---
status: proposed
date: 2026-08-05
decision_context: Runtime Execution Evidence domain grill approved by Sipke
---

# Execution Evidence integrates through domain-owned adapters

## Context

Workflow runtime, scheduling, stimuli, activities, and other existing Elsa domains provide the facts
from which execution evidence is derived. Adding evidence-specific models or services to those
domains would spread a QA-oriented concern through production runtime contracts and reverse the
desired optional dependency direction.

The Execution Evidence area is broader than one package or activation switch: it owns vocabulary,
contracts, capture and query behavior, adapters, providers, and APIs.

## Decision

Execution Evidence is an Elsa domain composed of its own modules and host-composition features. Its
`.Core` module owns contracts only. #1133 uses a provider-neutral base for session/capture and
Runtime adapters, an explicit InMemory provider leaf, and a transport-only API that depends on
Core/base rather than the provider. Integration adapters owned by the Execution Evidence domain
observe generic extension seams published by existing domains and translate committed facts into
registered evidence kinds.

Existing Elsa modules do not reference Execution Evidence contracts and do not receive
evidence-specific interfaces, models, events, settings, or conditional branches. Optional future
modules may deliberately contribute evidence kinds by depending on `ExecutionEvidence.Core`; that is
an outward opt-in dependency, not a dependency imposed on existing modules.

If a required fact cannot be observed through an existing generic seam, the preferred remedy is a
semantically general extension point owned by the source domain. Such a seam must be useful without
Execution Evidence terminology and justified independently of this consumer.

## Considered options

- Adding evidence hooks directly to runtime contracts was rejected because disabled hosts would
  still carry evidence-specific concepts and future source domains would become coupled to one
  observer.
- Treating Execution Evidence as one feature inside Workflow Runtime was rejected because capture,
  persistence, API, retention, and optional adapters have distinct dependency envelopes.
- Modifying existing modules to contribute evidence by default was rejected because it reverses the
  optional dependency and expands the change surface of the initial rollout.

## Consequences

- Installing no Execution Evidence modules leaves existing domain contracts and composition
  unchanged.
- The new domain may need several narrow adapters and provider modules rather than one monolithic
  project; #1133 does not create a provider-agnostic umbrella for its sole concrete provider.
- Generic source-domain seams remain governed by their source domains and must preserve runtime
  semantics whether or not any evidence adapter is installed.
- Future modules can opt into richer evidence without making the foundational catalog know them.

## Linked decisions

- [Execution evidence kinds form a governed extensible catalog](0054-execution-evidence-kinds-form-a-governed-extensible-catalog.md)
- [Execution Evidence domain](../glossary/elsa.md)
