# Implementation Plan: ETL runtime concurrency

**Branch**: `agent/codex/2908-etl-runtime-concurrency` | **Date**: 2026-11-04 | **Spec**: [spec.md](spec.md)

## Summary

Give each existing runtime store operation one bounded whole-unit EF retry, preserving once-per-call input snapshots and existing transaction boundaries. Qualify real server contention before enabling the three server contexts through normal module registration, then prove both ETL stores in owned Workbench processes.

## Technical Context

- .NET 10 / EF Core 10; existing `Elsa.Persistence.EntityFramework` retry, conflict classifier and provider binding.
- SQLite remains default. PostgreSQL 16, SQL Server 2022 and MySQL 8.4 use spec 1203 migrations unchanged.
- Existing `Elsa.Etl.Tests` and integration-tier `Elsa.Etl.Persistence.ProviderTests`; no new project or concrete provider dependency in production persistence.
- Owned Workbench controller extends spec 1202's journey; container credentials generated per run, ephemeral ports excluding 5095/4179. Record product/harness source separately.
- Scope is correctness and composition; no performance benchmark, new connector or later full crash/recovery matrix.

## Constitution Check

Framework 6.1.0 and Elsa 6.3.0, both gate indexes and the contributor review checklist apply.

- §2.1/2.9/2.20 and ADR 0087: provider mechanisms stay in the existing EF package; Core adds only a BCL exception contract, no engine reference or new module.
- §2.23.3/2.23.5: exception is public for hosts and third-party store authors, tracked and documented on the three public store contracts. One final boundary maps infrastructure failures while preserving the original cause. Deliberate observable change: callers catch the ETL exception rather than a provider exception; schema refusal, cancellation and corruption keep their existing distinct behavior.
- §2.21/2.23.2: preserve existing tests, add behavioral retry/cleanup/ownership proofs, real overlapping server races and actual host evidence. No documentation/source-text assertion.
- §E2.2/E2.6: runtime-only composition remains independent of design tables. The host executes its pinned artifact. No workflow/actor per record and no workflow authoring/state change.
- §E5: public API addition gets API/binary and computed-version magnitude review on the stable candidate; never author literal versions. §E7: existing integration project remains in its tier.
- No rule relies on draft §2.24 or provisional workflow model sections. Recheck gates after implementation; no architectural exception. The scoped pre-GA version-magnitude decision is recorded in [version review](evidence/version-review.json); it does not claim ordinary Minor compliance for a patch increment.

## Changes by boundary

1. **Core failure contract** — `Extensions/Etl/src/Core/Runtime/EtlStoreException.cs`, `PublicAPI.Unshipped.txt`, XML failure documentation on `IEtlRuntimeStore`, `IEtlConnectorRuntimeStore`, `IEtlConnectorRuntimeInspector` and the Core catalog. Fields: operation, nullable scope/run, original inner exception; safe message.
2. **Runtime persistence** — private retry/failure helper in the existing `EfEtlRuntimeStore` partials. Public calls perform cancellation/transaction-ownership preflight and capture mutable inputs once; private `*OnceAsync` bodies own the existing logical transaction. Pass db to `EfWriteRetry`, use one non-multiplying budget, clear abandoned tracked writes even at exhaustion. Standalone reads receive the same failure/retry boundary. Keep compaction+claim and final acceptance+terminal/outbox atomic. Replace the inspector's two public connector-read calls with an unretried private read core.
3. **Provider activation** — runtime registration uses the design store's `Binding.Select` pattern, keeps one scoped store and optional connector casts; add the three runtime contexts to `[EfModule]`. Update feature/project manifest descriptions and ETL/persistence READMEs. Activation changes no migration, provider default or stored shape.
4. **Behavioral tests** — shared retry/failure contracts in `Extensions/Etl/tests/Shared/RuntimeStore`; SQLite subclasses keep begin-immediate-specific gates. Provider tests cover genuine overlapping admission, preparation/partition claim, reservation budget, final acceptance/cancellation and delivery claims, plus bounded correctness soak. Gate a participant only on its first attempt and observe a real classified conflict. Add exhaustion/nonretryable/wrapped failure, cancellation, caller-transaction, dirty-tracker reuse, immutable-input and schema-refusal controls. Add a real-database multi-save preparation control: inject a classified conflict after the first successful save and before commit, verify rollback of earlier rows, complete reconstruction on retry, and no partial graph or tracked residue on exhaustion. Continuous compaction uses separate controls around its set-based writes, not a first-save rendezvous: inject after an update/delete before lease save, prove retained predecessor/request/batch integrity and exact compacted counters after retry, one live claim, and full history restoration on exhaustion.
5. **Actual hosts** — `specs/1206-etl-runtime-concurrency/evidence/runtime_store_workbench_journey.py` adapts the 1202 controller to put both ETL stores on the same selected server engine. Create, validate, publish and execute a two-partition JSONL pipeline via native Flowchart; assert expected physical records and one native completion, restart, and read back the exact run and publication. This is fresh server-host qualification, not an old-writer upgrade witness.

## Verification and delivery cadence

Use focused cases and one provider at a time while fixing; only new failures/changes reopen checks. Capture deterministic red/green controls, then run complete affected ETL/provider projects, public API/magnitude review, locked graph restore, architecture and generator checks on a stable candidate. Record discovery versus execution and source hashes. Independent full-diff review stays on draft; post gates, ready, request review. Merge base-first with fresh required checks; verify resulting main separately. Keep #2908/#2892/Project #62 In Progress and update evidence after the slice.

When the shared machine has no allocated host-build capacity, use the manual-only `CI`
`suite=etl-host` route. It publishes just Workbench, hashes its products against the actual
checked-out commit and reuses the same three-engine controller. The default PR/main/nightly
cadence is unchanged. Hosted job outcomes and retained partial evidence remain distinct from
a completed 36-check journey; required PR checks and resulting-main proof remain separate gates.

## Project Structure

All implementation stays in existing ETL Core, Persistence/EntityFrameworkCore and ETL test projects. Feature documents and runnable source-bound receipts are under this spec directory. See [research.md](research.md), [data-model.md](data-model.md), [failure contract](contracts/storage-failure.md) and [quickstart.md](quickstart.md). Tasks follow reviewed planning; no unrelated refactor or new project is proposed.
