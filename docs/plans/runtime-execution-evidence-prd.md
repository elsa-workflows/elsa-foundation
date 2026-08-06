# PRD: Runtime Execution Evidence

**Status:** Proposed
**Date:** 2026-08-05
**Tracking:** [GitHub epic #1132](https://github.com/elsa-workflows/elsa-foundation/issues/1132)
**Program goal:** [Runtime Execution Evidence](../program-goals/runtime-execution-evidence.md)
**Terminology:** [Elsa glossary](../glossary/elsa.md)
**Architecture decisions:** [ADR 0052](../adr/0052-execution-evidence-is-checkpoint-atomic-and-at-least-once-delivered.md) through [ADR 0061](../adr/0061-baseline-execution-evidence-records-committed-semantic-transitions.md), plus [ADR 0063](../adr/0063-execution-evidence-starts-in-memory-and-adds-groundwork-durability.md). ADR 0062 remains the JavaScript binding-grammar decision.

## Problem Statement

QA engineers and automated test systems can start workflows and inspect current runtime state, logs,
or traces, but they do not have a stable, complete record of the committed semantic facts that explain
what the workflow did. Logs and traces are best-effort diagnostic signals, internal engine details can
change during refactoring, and current inspection projections answer selected state questions rather
than providing a deterministic verification stream.

This makes tests timing-sensitive and difficult to diagnose. A test may need to know whether a
workflow started, an activity executed, a bookmark was consumed, a stimulus was deduplicated, a child
workflow was dispatched, an incident was created, or a variable changed to an expected value. It must
also distinguish “did not happen” from “has not arrived yet” and “evidence is incomplete.”

Adding this instrumentation directly to existing runtime modules would impose a QA-oriented concept
and overhead on every host. Capturing all values would broaden data exposure. Emitting best-effort
events after commit would make missing evidence ambiguous. The solution therefore needs explicit
activation, transactionally recoverable capture, governed schemas, bounded value handling, neutral
remote APIs, and a module boundary that leaves existing Elsa domains unaware of Execution Evidence.

## Solution

Create a new **Execution Evidence domain** composed of separate contract, implementation, API, and
Groundwork provider modules. A host explicitly enables the domain, and a caller explicitly opens an
evidence session. Only workflows associated with that session produce evidence; the association
propagates through scheduling, stimuli, child workflows, and resumed execution.

The domain records immutable, typed facts about committed semantic transitions. A deterministic
checkpoint enricher derives a complete opaque evidence batch and adds it to Runtime's existing generic
post-commit intent collection. Runtime persists that intent atomically with the checkpoint. An
Execution Evidence-owned handler materializes the batch idempotently into the query store with
at-least-once delivery. The API reports a range as complete only after required deliveries settle and
no sequence gaps remain.

The first vertical slice uses a process-local in-memory store and a minimal workflow/activity catalog.
Later features expand semantic coverage, add stimuli and causation, add opt-in value capture, and add
Groundwork durability and distributed recovery. Elsa publishes neutral protocol and conformance
fixtures; J-Test owns its assertion DSL and framework integration.

### Rollout features

1. [**Foundation vertical slice — #1133**](https://github.com/elsa-workflows/elsa-foundation/issues/1133) — domain modules, governed envelope/catalog, evidence sessions,
   deterministic checkpoint intent, strict failure behavior, in-memory materialization, minimal
   workflow/activity evidence, query/wait API, registration tests, and performance baselines.
2. [**Committed lifecycle coverage — #1134**](https://github.com/elsa-workflows/elsa-foundation/issues/1134) — complete workflow, activity, bookmark, incident, checkpoint,
   sequencing, deduplication, integrity, settled barrier, and completeness behavior.
3. [**Stimulus and scheduling causation — #1135**](https://github.com/elsa-workflows/elsa-foundation/issues/1135) — dispatch, child workflows, signals, triggers,
   deduplication outcomes, timers, scheduling, resumes, and causal propagation.
4. [**State and value evidence — #1136**](https://github.com/elsa-workflows/elsa-foundation/issues/1136) — variable and durable-value mutations, selected inputs and outputs,
   capture profiles, dispositions, maximum size, sanitizers, and redaction.
5. [**Groundwork durability and distributed hardening — #1137**](https://github.com/elsa-workflows/elsa-foundation/issues/1137) — durable idempotent materialization, crash
   recovery, failover, retention cleanup, and provider conformance.
6. [**Consumer conformance and J-Test integration — #1138**](https://github.com/elsa-workflows/elsa-foundation/issues/1138) — versioned protocol fixtures and neutral
   conformance kit in Elsa, with the fluent adapter and assertions implemented in J-Test.

Dependencies: #1133 → #1134; after #1134, #1135, #1136, and #1137 may proceed in parallel; #1138
requires all three parallel slices. Each feature issue starts with its own numbered Speckit
specification, plan, and task list rather than sharing one epic implementation plan.

## User Stories

1. As a QA engineer, I want to enable Execution Evidence only in selected shells, so that production
   and unrelated environments carry no evidence-specific behavior.
2. As a test runner, I want to open an evidence session before driving a workflow, so that every fact
   from that scenario has one isolation key.
3. As a test runner, I want to associate my own test-run and test-case identifiers with a session, so
   that I can correlate Elsa evidence without forcing test terminology into Elsa contracts.
4. As a QA engineer on a shared host, I want unscoped workflows to produce no evidence, so that
   concurrent workloads do not leak into my test results.
5. As a test author, I want a workflow's evidence-session association to survive scheduling and
   resumption, so that asynchronous continuations remain in the same verification range.
6. As a test author, I want child workflows to retain explicit causation back to their parent
   dispatch, so that I can verify cross-workflow behavior without relying on arrival order.
7. As a test author, I want to observe committed workflow lifecycle transitions, so that I can verify
   started, suspended, resumed, completed, faulted, and cancelled outcomes.
8. As a test author, I want to observe committed activity lifecycle transitions, so that I can verify
   which activities were scheduled, started, completed, faulted, cancelled, or skipped.
9. As a test author, I want to observe bookmark creation and consumption, so that I can verify wait
   and resume behavior.
10. As a test author, I want to observe incident creation and resolution, so that I can verify fault
    handling without parsing logs.
11. As a test author, I want to observe accepted, rejected, and deduplicated stimuli, so that I can
    test signals and triggers precisely.
12. As a test author, I want to observe timer scheduling and firing, so that I can test time-based
    workflows without inspecting scheduler internals.
13. As a test author, I want to observe workflow and child-workflow dispatch facts, so that I can
    verify causal handoffs.
14. As a test author, I want metadata for variable and durable-value writes, so that I can prove that
    state changed even when its value is not captured.
15. As a test author, I want to allowlist selected variable, input, output, and payload values, so that
    I can assert value flow without enabling blanket payload capture.
16. As a security-conscious operator, I want sanitization and redaction to run before checkpoint
    persistence, so that known secrets do not enter the evidence outbox or store.
17. As a test consumer, I want every value field to say whether it was captured, redacted, omitted, or
    truncated, so that a missing payload is never ambiguous.
18. As an operator, I want a module-wide captured-value size limit, so that an accidental large value
    cannot destabilize checkpoint persistence.
19. As a test consumer, I want stable evidence identifiers, so that at-least-once delivery cannot
    create false duplicate occurrences.
20. As a test consumer, I want a strict sequence per workflow and stable ordinal within a checkpoint,
    so that I can assert deterministic local order.
21. As a test consumer, I want explicit causation references across workflows, so that I do not need
    a costly or misleading global session order.
22. As a remote test runner, I want an HTTP API for session lifecycle, query, wait, integrity, and
    deletion, so that tests do not need to run inside the Elsa process.
23. As a remote test runner, I want to filter by session, kind, workflow, activity, subject,
    correlation, and sequence, so that I can retrieve only relevant facts.
24. As a remote test runner, I want cursor-based waiting, so that I can await behavior without fixed
    sleeps or repeatedly downloading old evidence.
25. As a remote test runner, I want wait outcomes to distinguish match, timeout, completion, and
    integrity failure, so that timeouts do not masquerade as negative proof.
26. As a test author, I want a completeness boundary for negative assertions, so that “did not
    happen” means the observed range is settled and gap-free.
27. As a test author, I want suspended and long-running workflows to expose settled barriers, so that
    negative assertions do not require every workflow to terminate.
28. As an operator, I want evidence recording failure to abort an evidence-scoped checkpoint, so that
    successful runtime behavior cannot silently lack its durable evidence intent.
29. As an operator, I want evidence materialization failures to retry after commit, so that workflow
    state does not roll back after its checkpoint succeeds.
30. As a test consumer, I want a session to remain incomplete while delivery is pending or a sequence
    gap exists, so that I cannot accidentally trust a partial range.
31. As a module author, I want to register a typed, versioned evidence kind, so that optional modules
    can add semantic facts without editing the baseline catalog.
32. As a module author, I want conflicting or unregistered kind definitions rejected at startup, so
    that stored evidence schemas stay governed.
33. As a client with an older SDK, I want to filter and inspect the common envelope of an unknown
    registered kind, so that protocol evolution remains forward-compatible.
34. As a runtime maintainer, I want Execution Evidence to use existing generic checkpoint and outbox
    seams, so that Runtime does not acquire evidence-specific contracts or branches.
35. As a runtime maintainer, I want enrichers to be deterministic and replay-stable, so that evidence
    cannot introduce checkpoint fingerprint conflicts.
36. As an operator, I want completed evidence retained and deleted as a whole session, so that normal
    expiry cannot create partial ranges.
37. As an operator, I want a short configurable default retention period and explicit deletion, so
    that QA values do not remain indefinitely.
38. As a provider author, I want a shared conformance suite for ordering, deduplication, integrity,
    retention, and failure semantics, so that stores provide equivalent behavior.
39. As a distributed QA engineer, I want the Groundwork provider to recover evidence across restart
    and failover, so that I can prove completeness beyond one process lifetime.
40. As a performance engineer, I want measured absent, unscoped, metadata-only, and value-capture
    baselines, so that later releases can detect overhead regressions.
41. As a J-Test maintainer, I want neutral versioned protocol fixtures, so that I can build ergonomic
    assertions without binding Elsa to my framework.
42. As a support engineer, I want failed tests to include the relevant ordered evidence and integrity
    state, so that I can diagnose the semantic cause rather than reconstruct it from logs.

## Implementation Decisions

- Execution Evidence is a separate Elsa domain. Existing modules do not reference its contracts,
  settings, models, or events. Domain-owned adapters consume existing generic seams.
- The initial module family is `Elsa.Workflows.ExecutionEvidence.Core`,
  `Elsa.Workflows.ExecutionEvidence`, and `Elsa.Workflows.ExecutionEvidence.Api`. The durable provider
  is `Elsa.Workflows.ExecutionEvidence.Persistence.Groundwork`.
- The `.Core` module contains provider-neutral envelope, catalog, session, capture-profile, cursor,
  integrity, query, store, and contribution contracts. It has no ASP.NET Core, Groundwork, or test-
  framework dependency.
- The common envelope includes stable evidence identity, evidence-session identity, kind and schema
  version, workflow identity and local sequence, checkpoint identity and local ordinal, occurred time,
  optional activity identity, causation identity, correlation metadata, and a typed payload.
- Evidence kinds use stable strings and explicit schema versions. Baseline kinds are typed contracts;
  optional contributors register additional typed kinds. CLR names and arbitrary unregistered
  dictionary payloads are not wire contracts.
- The baseline catalog covers committed workflow, activity, bookmark, incident, state-mutation,
  stimulus, causation, scheduling, checkpoint, and settled-barrier transitions. Reads, polling,
  heartbeats, logs, middleware traversal, method calls, and rolled-back exceptions are excluded.
- Capture uses two gates: explicit host feature activation and an explicitly opened evidence session.
  `EvidenceSessionId` is the runtime concept; test identifiers are correlation metadata.
- The evidence-session association propagates through opaque runtime metadata across scheduling,
  stimuli, child dispatch, and resume boundaries. Losing the association is an integrity failure.
- The validated primary capture seam is the existing generic checkpoint commit enricher. It derives a
  canonical bounded evidence batch before checkpoint persistence and fingerprinting and appends one
  opaque post-commit intent.
- Evidence intent, batch, and record identities are deterministically derived from stable commit
  identity and fixed discriminators. Enrichment does not read current time, randomness, or mutable
  external state.
- Runtime persists the complete evidence intent with checkpoint state through its existing outbox
  contract. Failure to prepare or persist required evidence fails the checkpoint.
- The domain registers an existing generic post-commit intent handler and an explicit delivery driver
  for its intent kind. The handler materializes records idempotently. Delivery failure does not roll
  back committed workflow state but prevents session completeness and remains retryable or becomes an
  explicit integrity failure.
- The atomicity contract applies to the durable evidence intent, not to a separate query-store row.
  Cross-store ACID and exactly-once side effects are unnecessary.
- Workflow ordering is strict and monotonic per workflow, with stable checkpoint-local ordinals.
  Cross-workflow ordering uses causation references. Timestamps and API cursor order are not semantic
  global ordering.
- The in-memory implementation is process-local and cannot claim completeness across process loss.
  Groundwork is the only first-party durable provider and must prove restart and failover behavior.
- Metadata evidence remains available for enabled kinds. Values are opt-in through session-level
  allowlists and pass through deterministic bounded sanitizers before persistence.
- Value payloads have explicit captured, redacted, omitted, or truncated dispositions. One module-
  wide maximum captured-value size is included; a generalized host policy matrix is not.
- Standard endpoint authorization protects session lifecycle, query, and deletion. Existing tenant
  scope and access-context rules apply; callers cannot use evidence APIs to cross those boundaries.
- The API owns session lifecycle, filtered snapshot query, opaque cursor continuation, cursor-based
  wait, integrity/completeness results, retention metadata, and completed-session deletion.
- A timeout is inconclusive. Definitive absence requires a terminal fact, settled barrier, or
  completed-session boundary and a gap-free range.
- Retention applies to the whole completed session. TTL begins at completion, callers may request a
  shorter period, and explicit deletion is supported. Per-record expiry, capacity-based eviction, and
  a storage-pressure interface are excluded.
- Module absence adds no evidence-specific registrations, branches, allocation, serialization, or
  persistence work to existing modules. Enabled-unscoped capture performs only a constant-time session
  check. Numeric regression budgets follow measured first-slice baselines.
- Elsa supplies neutral protocol and store conformance fixtures. J-Test owns fluent assertions,
  framework lifecycle, pass/fail reporting, and its Elsa client adapter.

## Testing Decisions

- Tests assert externally visible semantic facts and API behavior, not middleware steps, scheduler
  loops, actor callbacks, handler class names, or store implementation details.
- `.Core` contract tests cover envelope validation, catalog registration conflicts, schema-version
  compatibility, deterministic identity, ordering, cursor binding, value dispositions, query filters,
  and integrity state.
- Default implementation tests cover session activation, unscoped no-op behavior, deterministic
  checkpoint enrichment, strict failed-checkpoint behavior, idempotent materialization, delivery
  retries, settled barriers, retention, sanitization, and module registration order.
- Runtime integration tests use the checkpoint committer and generic post-commit outbox as the highest
  existing seam. They prove one opaque intent per checkpoint, deterministic replay, no intent after a
  failed/skipped checkpoint, and unchanged workflow outcome after materialization failure.
- Groundwork conformance tests prove atomic opaque-intent persistence, idempotent durable
  materialization, ordering, filtering, restart/failover recovery, cleanup, and incomplete delivery
  reporting across supported providers.
- API integration tests exercise session creation, normal workflow API invocation, cursor-based wait,
  filtered query, completeness, authorization, expiry, and deletion through HTTP.
- Backend end-to-end suites drive a rebuilt Elsa.Server through the real REST, persistence, runtime,
  scheduling, and stimulus paths. Suites are added for lifecycle, bookmarks/incidents, stimuli/timers,
  values/redaction, and Groundwork restart recovery.
- Compatibility fixtures freeze kind strings, schema versions, envelope/payload wire shapes, cursor
  binding rules, and J-Test protocol examples.
- Failure-injection tests cover enricher failure, checkpoint-store failure, crash after evidence write
  before acknowledgement, duplicate delivery, exhausted retry, sequence gap, process loss in the
  in-memory adapter, and Groundwork restart/failover.
- Benchmarks compare module absent, module enabled but unscoped, metadata-only scoped capture, and
  value capture across representative value counts and serialized sizes. The first feature records
  baselines; later work sets evidence-based regression limits.
- Prior art includes Runtime checkpoint commit/outbox contract tests, activity-execution inspection
  API tests, Groundwork provider conformance, structured-log cursor tests, diagnostics durable store
  tests, and backend REST end-to-end suites.

## Out of Scope

- Studio, dashboard, timeline, or other human-facing UI.
- Always-on production capture or retroactive capture of workflows that were not in an evidence
  session.
- Replacing Runtime inspection projections, structured logs, OpenTelemetry, metrics, or traces.
- Canonical evidence for attempted, rolled-back, or otherwise uncommitted behavior.
- Middleware-, actor-, scheduler-loop-, method-, heartbeat-, read-, or polling-level event streams.
- An Elsa-owned test assertion DSL, test-suite model, pass/fail model, or framework retry policy.
- A global total order across concurrent workflows.
- Exactly-once external delivery, distributed transactions between Runtime and the evidence query
  store, or cross-store ACID.
- Blanket payload capture, a general data-classification engine, or a per-subject host policy matrix.
- Storage quotas, storage-pressure detection, or automatic capacity-based eviction.
- Record-by-record expiry or indefinite retention by default.
- First-party EF Core persistence.
- J-Test implementation code inside the Elsa repository.

## Further Notes

- A disposable prototype validated the generic checkpoint enricher and opaque post-commit intent path
  with both the in-memory and Groundwork checkpoint stores. It proved deterministic replay, strict
  skip/failure behavior, idempotent crash redelivery, and intact opaque Groundwork payloads. No new
  Runtime extension point was required; prototype code was removed after verification.
- The program-goal state is the named **Runtime Execution Evidence** bucket. It is separate from
  Runtime Execution Seam because the new domain consumes Runtime, and separate from Diagnostics
  Observability because semantic committed evidence is not telemetry.
- The PRD is the end-state epic. Each rollout feature receives its own numbered Speckit specification,
  plan, tasks, implementation branch, and GitHub feature issue. The epic must not become one mega-spec.
- Proposed ADRs remain proposed until their first implementing work unit validates and accepts them
  through normal architecture review.
