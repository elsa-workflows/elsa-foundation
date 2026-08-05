---
status: proposed
date: 2026-08-05
decision_context: Runtime Execution Evidence domain grill approved by Sipke
---

# Execution evidence capture is explicitly session-scoped

## Context

Execution evidence adds runtime work and can expose values or metadata that ordinary workloads do
not need to retain. Enabling the module for an entire QA host is a useful deployment boundary, but
capturing every workflow on a shared host would create unnecessary overhead, mix concurrent test
runs, complicate retention, and broaden access to potentially sensitive data.

Using `TestRunId` as the runtime concept would make Elsa's evidence contracts depend on a particular
test-runner vocabulary and would not naturally cover other bounded verification or diagnostic uses.

## Decision

Execution evidence capture uses two explicit gates:

1. The host installs and enables the Execution Evidence module.
2. An authorized caller opens an evidence session and associates workflow execution with its
   `EvidenceSessionId` through associate-and-start or checkpoint-boundary `AttachIfAbsent`.

An enabled host does not capture unscoped workflows. #1133 carries the association only in a bounded,
versioned opaque generic execution-context snapshot, committed before capture and reused on replay.
Late association linearizes only with its committed attach checkpoint and captures no earlier facts.
Stimuli, child-workflow causation, and broader propagation remain #1135 work.

`EvidenceSessionId` is an Execution Evidence-domain association identity, not a Runtime concept.
Runtime carries a generic opaque context entry and neither names nor interprets it. Test
infrastructure can attach or map its own `TestRunId` or case identifier as external correlation
metadata, but those test-specific identifiers do not replace the evidence-session identity.

## Considered options

- Host-wide capture whenever the module is enabled was rejected because it expands overhead and
  data exposure and makes concurrent test isolation difficult.
- A `TestRunId`-only contract was rejected because it couples a general runtime evidence capability
  to one consumer category.
- Inferring capture from environment names or deployment configuration alone was rejected because
  workloads on a shared QA host may have different capture and data-governance requirements.

## Consequences

- Module enablement is necessary but insufficient to produce evidence.
- Session creation, closure, authorization, retention, and quota policy become explicit module
  responsibilities.
- The #1133 generic prepare/commit seam preserves the opaque association snapshot; losing it is an
  observable evidence continuity failure rather than silently starting an unrelated session.
- Evidence storage and query APIs use `EvidenceSessionId` as a primary isolation boundary.
- Test systems retain freedom to model suites, runs, cases, and retries independently.

## Linked decisions

- [Execution evidence is checkpoint-atomic and delivered at least once](0052-execution-evidence-is-checkpoint-atomic-and-at-least-once-delivered.md)
- [Execution evidence and evidence session](../glossary/elsa.md)
