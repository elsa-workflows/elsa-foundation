---
status: proposed
date: 2026-08-05
decision_context: Runtime Execution Evidence domain grill approved by Sipke
---

# Execution Evidence API exposes neutral verification primitives

## Context

Remote test runners need to open capture scopes, drive Elsa through its ordinary APIs, wait for
committed behavior, and query the resulting evidence. Embedding a particular test framework's
assertion model in Elsa would leak suite, case, retry, and assertion concepts into the runtime
contract and make other consumers second-class.

The HTTP endpoint surface also has a different dependency envelope from evidence contracts,
collection, and persistence.

## Decision

The Execution Evidence domain includes a dedicated `Elsa.Workflows.ExecutionEvidence.Api` module.
It exposes provider-neutral HTTP endpoints for:

- evidence-session lifecycle;
- filtered evidence queries by session, kind, workflow, activity, subject, correlation, and
  sequence;
- cursor-based waiting for matching evidence without client polling sleeps; and
- evidence integrity observations, including duplicate suppression and delivery state.

For #1133, the API can expose an inconclusive timeout, incomplete delivery, terminal integrity
failure, and an observable completed-range-without-match result. It does not claim #1134 settled
barriers, gap-free completeness, or definitive-negative semantics, and it does not implement #1136
value/disposition behavior.

Responses expose typed baseline contracts where appropriate and preserve common-envelope access for
registered kinds unknown to a particular client. Continuation uses an opaque evidence cursor rather
than exposing database offsets or storage-provider details.

The API does not provide a fluent assertion language, test-case lifecycle, framework-specific retry
semantics, or pass/fail outcomes. J-Test and other consumers build those facilities over the neutral
API.

The `.Api` module owns transport and endpoint concerns. Its public feature may inherit the base
feature registration and calls `base.ConfigureServices`; the module depends on Core/base and never
the concrete InMemory provider. The `.Core` contract module does not depend on ASP.NET Core or a test
framework.

## Considered options

- Putting endpoints in the default implementation module was rejected because it couples HTTP
  hosting to collection and persistence.
- Providing an Elsa-owned assertion DSL was rejected because it would couple the domain to test
  framework concepts and duplicate consumer responsibilities.
- Exposing provider offsets as cursors was rejected because it would make the public API depend on
  the selected evidence store.

## Consequences

- Remote QA tools can consume evidence without running in the Elsa process.
- Test libraries own ergonomic assertions while sharing one stable evidence protocol.
- The API module requires explicit host activation and authorization.
- Cursor, filtering, wait, integrity, and session-lifecycle contracts must be specified and tested
  independently of any J-Test adapter.

## Linked decisions

- [Execution evidence capture is explicitly session-scoped](0053-execution-evidence-capture-is-explicitly-session-scoped.md)
- [Execution Evidence integrates through domain-owned adapters](0055-execution-evidence-integrates-through-domain-owned-adapters.md)
- [Evidence cursor](../glossary/elsa.md)
