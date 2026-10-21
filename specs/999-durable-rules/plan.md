# Implementation Plan: Durable Rules

**Branch**: `agent/codex/3181-durable-rules` | **Date**: 2026-10-21 | **Spec**: [spec.md](spec.md)

**State**: Ready for implementation after bounded integrated planning review on 2026-10-21. Runtime, package, provider and native acceptance are pending.

## Summary

Deliver the complete #3181 independently packaged fourth model through one owned leaf and reviewable checkpoints. Rules owns immutable inference artifacts, durable round progress, cancellation and terminal delivery. Existing native publication/checkpoint/bookmark seams bridge to persisted Sequence and Flowchart; neither composite acquires rule semantics.

## Technical context

- C# / repository stable .NET 10 SDK and existing package version calculation.
- Optional modules under `Extensions/Rules`; Core contracts have no heavy dependencies.
- EF is the only first-party durable family, with separate Rules context/history and SQLite, PostgreSQL, SQL Server and MySQL migrations.
- Existing xUnit/provider fixtures, real package consumer, separate PostgreSQL-backed processes and rebuilt Workbench REST journeys provide acceptance.
- No wall-clock performance measurement or budget; existing owner policy retires it. Byte/count bounds are correctness constraints.
- Deployment proof is same-machine processes/containers, conventional file-based framework-dependent packages. Optional actor providers and a general task runner are excluded.

## Constitution check

Both framework 6.1.0 and Elsa 6.1.0 gate indexes and the repository review checklist were read. The plan requires: Core/implementation separation (§2.1); public contracts only for actual consumers; ordinary feature composition and scoped services (§2.5); no runtime Design dependency (§E2.2); artifact-only recovery (§E2.6); CQS store/reader methods (§2.10); provider-neutral persistence contracts and provider-suffixed EF implementation (§2.9/§2.20); version calculator and tracked public surfaces (§E5/§2.23.3); local/root extension catalogs (§2.22); additive migration and actual behavioral tests (§2.21/§2.23). Four-engine qualification follows ADR 0073.

[ADR 0088](../../docs/adr/0088-rules-owns-its-execution-domain.md) adopts the narrow Rules domain under the owner's delegated design/review authority after bounded review, with Elsa 6.2.0/sync-impact/domain-purpose changes following ADR 0087. This planning decision does not qualify unimplemented runtime behavior. Framework §2.24 and Elsa §E2.9 remain draft/provisional; this leaf neither ratifies nor expands them. No universal execution API or membership service is introduced.

## Project structure

Six modules: `Extensions/Rules/src/Core`, `src/Runtime/Elsa.Rules.Runtime`, `src/Persistence/Core`, `src/Persistence/EntityFrameworkCore`, `src/Workflows/Design`, `src/Workflows/Runtime`. Tests belong to `Extensions/Rules/tests/Tests` and `tests/Persistence/EntityFrameworkCore/ProviderTests`. Package proof belongs to `tools/rules/`; the real HTTP journey belongs to `e2e-tests/rules/`. Source project layout must pass the existing architecture guard; do not add empty modules.

This specification owns research, data model, contracts, quickstart, tasks and evidence. The issue/project remains the public progress record; preparation documents are not acceptance receipts.

## Finite v1 bounds

Define immutable v1 limits: 1,024 rules, 4,096 distinct vocabulary facts, 64 premises per rule, 256 UTF-8 bytes per atom/rule ID, 1 MiB canonical artifact, 4 MiB common invocation/round receipt/terminal result, 8 MiB current persisted state projection, 256 entries per inspection page, and 4,097 as the greatest accepted positive round limit. These are independent maxima, not a promise that their Cartesian product fits every byte limit. Identifiers are ordinal and must have valid Unicode encoding; canonical encoding is versioned. The artifact explicitly declares its vocabulary. Every initial fact, rule premise and conclusion must be a member; reject out-of-vocabulary atoms rather than silently extending the set. Empty vocabulary and no rules are valid and close in one round. Atom and rule IDs are nonempty.

Scope keys use ordinal case-sensitive equality of valid Unicode strings without trimming, case-folding or Unicode normalization. Persistence partition and invocation ID are nonempty and each at most 256 UTF-8 bytes; tenant is either null or a nonempty string with the same bound. Null has its own canonical tag. Provider indexes use the lowercase 64-character SHA-256 of a versioned length-prefixed canonical scoped identity; always compare the retained full canonical identity before treating a hash match as the same run. A hash collision conflicts without revealing or mutating another run. Case variants and canonically equivalent but differently encoded Unicode strings remain distinct identities. Fixed ASCII index keys avoid provider collation/length differences; they do not replace full identity verification.

Historical accepted/consumed receipts use separate immutable rows and bounded pages; the projection cap is not a cap on cumulative history and never authorizes truncation.

Before admission, verify the actual canonical input and conservative maximum receipt/result representations over the finite artifact vocabulary fit all envelopes, including escaping and fixed routing/identity overhead. An accepted valid model must not fail later merely because a reachable complete round exceeds the envelope cap. Tests cover exact limits and one-over values, including encoded byte lengths. This is bounded admission, not successful truncation. Explicit positive lower round budgets remain supported; omitted budgets are resolved and frozen before invocation identity. Operational lease durations use trusted host configuration and controllable test clocks, not model-authored expiry.

## Implementation checkpoints

1. Consolidate formal contract, domain admission and source overlap; review scope and architecture.
2. Implement canonical semantics, full receipt validation, direct facade, SQLite durable transitions and real success/fault/cancellation/refusal/unavailability cases.
3. Implement four provider contexts/migrations and one invariant suite, including unique-key admission races, conditional writes, lost replies, delivery and schema coexistence. Register required native-provider cases in the existing EF suite manifest.
4. Pack actual modules/dependency closure with computed versions; implement clean package-only consumer and separate-process PostgreSQL recovery at pending-receipt and consumed-round boundaries, retaining exact artifact/state/receipt identity and loaded semantic asset proof.
5. Implement normal CLR native adapter, immutable publication pin, checkpoint admission/cancellation responsibility and proof-based terminal delivery. Prove real persisted Sequence/Flowchart continuation and terminal/temporary-unavailability cases through the rebuilt Workbench HTTP journey.
6. Prove additive native schema coexistence and original-pin Rules upgrade/rollback recovery; complete docs/catalogs, meaningful removal controls, compatibility/architecture/filter checks, bounded self-review, exact-head CI and resulting-main evidence. Only then close the leaf.

Detailed signatures, state transitions, commands and task ordering are consolidated in the adjacent reviewed planning artifacts. Do not substitute a smaller SQLite-only or source-project-only proof for the declared acceptance.

## Risks and validation controls

Acceptance must reject incomplete inference as well as extraneous conclusions. Losing a receipt response must not force unnecessary recomputation or permit duplicate advancement. Renewal must not mutate model revision. Native collection-valued results need canonical content comparison, not list-reference equality. Parent proof must precede delivery acknowledgement. Package content identity, archive provenance and publisher trust remain distinct. Provider collation/key limits and transactional behavior require separate real-engine proof.

Run heavy work serially through the machine's build-slot wrapper; use focused project graphs. Every removal control restores the real code before normal verification. Do not change workflow permissions/configuration to manufacture a green gate. No API or Studio product module is added without a concrete consumer requirement.

## Test cadence and integration ownership

Elsa deliberately has no mandatory greenfield test-first cadence (constitution 6.0.0 D9); there is no unresolved owner choice to invent. Implement behavior with the required contract/registration/branch tests and meaningful removal controls before accepting each checkpoint. The root integrates and independently reviews delegated output. Shared solution/filter/catalog registration is serialized by the root; module-only work may be delegated on disjoint files after the reviewed planning checkpoint.

## Future worker layer

Rules durable state and completion responsibility belong to the model. Worker activation, placement and claim polling are replaceable hosting mechanics. No Rules contract assumes that one process or physical queue is dedicated to one workflow. A future pause policy can gate new claims while retaining already accepted work and result delivery; in-flight physical work cannot be retroactively stopped by a queue flag. This preserves an integration seam without implementing or promising queue/workflow pause in this leaf.
