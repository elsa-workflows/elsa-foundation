---
status: proposed
date: 2026-08-05
decision_context: Runtime Execution Evidence domain grill approved by Sipke
---

# Execution evidence value capture is explicit and lean

## Context

Tests often need to assert variable, input, output, and stimulus values. Capturing every value by
default would add storage and serialization overhead and could persist secrets or personal data.
Conversely, metadata-only evidence cannot verify value flow.

A comprehensive host policy matrix for subjects, classifications, sizes, and permissions would add
substantial policy infrastructure before a concrete multi-tenant or production use case exists. The
initial target is an explicitly enabled QA module with explicitly opened evidence sessions.

## Decision

Relevant committed mutations always produce metadata evidence when their evidence kind is enabled.
Actual values are opt-in through an evidence-session capture profile with explicit subject
allowlists. The profile can select supported variable, input, output, and payload subjects without
turning on blanket full-payload capture.

Before checkpoint persistence or outbox recording, values pass through the registered sanitizer and
redactor chain. Values identified as secrets or credentials are redacted. The module supplies built-in
handling for known secret-bearing values and exposes a sanitizer contribution seam for application-
specific types and conventions.

A simple module-wide maximum captured-value size protects the checkpoint and evidence store from
unbounded records. Ordinary endpoint authorization controls session creation and evidence access.
The first version does not implement per-subject host ceilings, a general data-classification system,
or a separate capture-policy authorization engine.

Every value-bearing field carries one explicit disposition: `captured`, `redacted`, `omitted`, or
`truncated`. A consumer does not infer capture state from a missing or null payload.

## Considered options

- Capturing all values by default was rejected because it increases overhead and data exposure.
- Metadata-only evidence was rejected because value-flow assertions are a primary testing use case.
- A generalized host capture-policy matrix was deferred because it adds speculative complexity for
  the initial QA deployment model.
- Applying redaction in query responses was rejected because sensitive material would already have
  entered checkpoint, outbox, and storage records.

## Consequences

- Test authors explicitly request only the values needed by their scenarios.
- Mutation evidence remains useful when a value is omitted or redacted.
- Sanitization executes on the commit path and must be deterministic, bounded, and failure-safe.
- Oversized values are represented as truncated rather than silently dropped.
- Deployments with stronger governance needs can add sanitizers now; a richer host policy model
  requires a later justified decision.

## Linked decisions

- [Execution evidence is checkpoint-atomic and delivered at least once](0052-execution-evidence-is-checkpoint-atomic-and-at-least-once-delivered.md)
- [Execution evidence capture is explicitly session-scoped](0053-execution-evidence-capture-is-explicitly-session-scoped.md)
- [Evidence capture profile and evidence value disposition](../glossary/elsa.md)
