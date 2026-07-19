# Tag Assignment Origins Own Assertions Without Precedence

Status: accepted (2026-07-19; ratified through the workflow-tagging design grilling session)

## Context

The first tagging release permits workflow authors to assign tags manually. Later releases may
derive tags from source-controlled workflow declarations, host policy, system classification, or
automation. If all contributions overwrite one row, reconciliation can erase author intent. If
one origin silently outranks another, the effective result depends on hidden precedence rules and
can change when integrations are enabled.

Audit actor and assignment authority are different concepts. A user can trigger source
reconciliation without becoming the owner of the resulting source assertion.

## Decision

Each stored assignment assertion has an origin kind and stable origin key. The supported origin
kinds are `Manual`, `Source`, `System`, `Policy`, and `Automation`; the first release creates only
manual assertions. The actor, timestamp, correlation identity, and optional idempotency identity
are recorded separately as audit facts.

An origin may add, replace, or remove only its own assertion slice:

- Multiple-valued definitions expose the union of values asserted by all origins.
- Identical values asserted by several origins collapse into one effective assignment while
  retaining every backing origin.
- Different values asserted by several origins for a single-valued definition produce a visible
  conflict. No origin wins implicitly.
- Reconciliation reports conflicts and preserves assertions until an authorized user resolves the
  conflict or changes an origin declaration.

When a future source declaration introduces a canonical key that is not present, it may provision
that definition subject to host policy. If the key already resolves to compatible semantics, source
reconciliation reuses it. If the semantics conflict, reconciliation emits a diagnostic and does
not mutate the existing definition or manual assertions.

## Consequences

Manual edits cannot erase source, system, policy, or automation assertions, and source
reconciliation cannot erase manual assertions. Readers need an effective projection plus optional
origin detail and conflict diagnostics. The persistence model must retain assertions even when
their effective value is de-duplicated.

There is no general-purpose precedence configuration to explain, secure, or migrate. A product
that later needs an override rule must introduce an explicit policy and migration rather than
changing the meaning of existing assertions.
