# Implementation Plan: Finite CSV-to-JSONL ETL

**Branch**: `agent/codex/3109-finite-etl` | **Date**: 2026-10-20 | **Spec**: [spec.md](spec.md)

**State**: Proposed design. [ADR 0087](../../docs/adr/0087-etl-owns-its-execution-domain.md) awaits the owner's domain-admission decision. Independent regression coverage for existing runtime behavior can proceed; this document does not report the constitution gate as passed.

## Summary

Deliver [#3109](https://github.com/elsa-workflows/elsa-foundation/issues/3109) as one complete finite-file journey: Studio publishes an immutable ETL artifact, a native activity waits for one ETL run, independently leased partitions execute bounded batches, immutable JSONL chunks support replay, and Studio exposes durable progress. Reuse checkpoint/outbox/bookmark infrastructure and the qualified built-in host. Add neither an actor provider nor a generic execution kernel.

## Technical Context

- **Language/platform**: C#/.NET 10 and the existing ASP.NET Core endpoint framework; React/TypeScript in the Studio extension convention.
- **Dependencies**: Existing modularity, tasks, API, activity-runtime and workflow runtime/publishing contracts. Shared EF persistence policy and existing provider packages. Implement the spec's bounded CSV dialect with a streaming reader; no new CSV dependency is selected.
- **Storage**: ETL-owned relational design/runtime stores using the existing EF family, with separate contexts/schema ownership. Host-managed immutable input revisions and retained snapshots; immutable output chunks and an accepted manifest. Runtime requires no design database.
- **Testing**: xUnit/shared fixtures, real SQLite, two worker processes, applicable EF migration/provider fixtures, rebuilt Workbench HTTP e2e, Studio tests/browser verification.
- **Performance**: No throughput/SLA target. Prove finite logical record/byte bounds, independent progress and recovery. Payload limits are not RSS measurement.
- **Scope**: CSV and pinned local CSV lookup, deterministic validation/map/filter, accepted/error JSONL, at least two nonempty fixture partitions, native Sequence/Flowchart. Other connectors, continuous processing, BPMN invocation and broader topology remain on #2908.

## Constitution Check

Read framework v6.1.0, Elsa v6.0.2 and `docs/contributing/review-checklist.md` at baseline `29b7636385b773c6d53da3e3081d1e548b40e0e9`.

| Gate | Design disposition |
|---|---|
| Framework §2.1/§2.2/§2.18; Elsa §E2.1 | Optional `Elsa.Etl` purpose. **Pending** domain-list ratification, ADR 0087; do not broaden middleware silently. |
| §2.6.4; Elsa §E2.2/§E2.6 | Separate design/runtime interfaces. Runtime reads only retained artifacts and configured bindings. Native adapters have separate design/runtime packages. |
| §2.9/§2.10/§2.20 | EF-free domain-owned state/store contracts, separate command/query methods, provider-only EF. Add reviewed EF admission and migration inventory. |
| §2.5/§2.6/§2.23.3 | Scoped logic, internal sealed implementations, feature shape and public API tracking. Public contracts serve actual host/provider/API/native consumers; no cross-feature implementation references. |
| §2.16/§2.17 | Seven backend packages plus Studio, each with a real dependency/deployment boundary. Domain-owned store contracts follow existing Runtime.Core stores; no empty persistence umbrella. |
| §2.21–§2.23 | Risk-based tests/shared fixtures and real recovery evidence. This unit chooses test-first for parsing/state transitions; it imposes no repository-wide cadence. Preserve valid tests. |
| §2.22/§2.23.5; Elsa §E6 | Catalog actual extension points/tasks, wrap infrastructure failures and use domain nouns. |
| §2.24/§E2.9 | Draft/provisional sections are not ratified here. Preserve activity-specific composition and artifact separation; no universal edges/joins. |
| §4/Elsa §E5 | Computed pre-GA package versions. Persisted ETL formats version 1 reject unknown versions. No existing wire/reader change. |

Recheck after design review and before production code. The domain decision is a specific pending amendment, not a new general approval flow.

## Execution and Durability

1. **Publish.** Validate schema/topology/limits/binding aliases. Produce an immutable, behavioral-content-hashed ETL artifact, independent of drafts. Native workflow compilation embeds that bounded artifact and hash in ETL-owned executable metadata, so workflow retention retains it without treating ETL IDs as workflow-artifact dependencies. It does not freeze current input-file content at workflow publication.
2. **Wait before admission.** `RunEtl` suspends with typed immutable private state and a completion registration. Its bookmark is ordinary queued creation work. An ETL checkpoint enricher validates the actual `BookmarkCreated` commit's activity/state/bookmark and adds a deterministic admission intent, atomically persisted by the existing outbox with the bookmark. No invocation buffer or kernel hook.
3. **Pin inputs.** The short admission handler idempotently creates a run with trusted tenant/partition and invocation identity, resolves host-controlled immutable source/lookup revisions, and pins output destination revisions once. Validation refusal still produces a durable terminal result/outbox; it must not leave the parent suspended with an acknowledged start and no result. Storage unavailability retains start delivery responsibility. A separate bounded preparation worker copies and verifies expected digests before partition work; the outbox handler does not process a file under its delivery lease. Never claim an atomic snapshot of an arbitrarily edited file or substitute revisions on replay.
4. **Prepare partitions.** Stream a bounded parse/index pass into disjoint exhaustive logical-record ranges. Validate framing/encoding and lookup size/key uniqueness before dispatch. Malformed framing ends with a visible business error and no JSONL effects; well-framed row conversion/lookup failures become bounded error descriptors during processing.
5. **Admit batches.** Transactionally reserve both record count and serialized input bytes before dispatch. Each partition has at most one owed batch. Its descriptor/identity/reservation survives worker loss; recovery reuses it without reserving twice or releasing on timeout. Host-local permits cover finite input/lookup/output allowances and remain held until each live attempt unwinds. A recovery attempt acquires its own local permit.
6. **Apply, then progress.** Renew/check ownership before effects. Write bounded JSONL into attempt-private staging files, flush/close/verify hashes, then atomically publish complete chunks under stable run/partition/batch keys without overwriting. Interrupted staging files are not chunks; cleanup waits for their writer to stop. Identical existing content is replay success; conflicting final content faults. One fenced ETL transaction accepts chunk descriptors, advances the cursor and settles its logical reservation. Physical I/O already underway may finish after fence loss; it cannot make output authoritative.
7. **Settle and deliver.** Finalize a deterministic manifest from accepted chunks. A run-level compare-and-set accepts one terminal result and terminal-outbox row atomically. Deliver through `IBookmarkResumeDispatcher` with a stable identity and exact parent/activity/bookmark/schema checks. Keep delivery owed until consumption or a demonstrated parent-terminal disposition, not just queueing.
8. **Cancel.** Existing whole-workflow/subtree cancellation retains private state. Derive an ETL cancel intent from that state and persist a tombstone even before admission. Terminal completion/cancellation race through one durable decision. Preserve accounting of admitted work/effects; cancellation and lease expiry do not prove worker termination.

The workflow and ETL stores have separate transactions, bridged by idempotent intent admission and terminal delivery. Unused `RuntimeWaitRegistration`/wait-dependent-intent fields are not implemented infrastructure and are not used. [research.md](research.md) records source verification; [data-model.md](data-model.md) records feature state.

## Project Structure

```text
Extensions/Etl/
  src/
    Core/                         Elsa.Etl.Core
    Design/                       Elsa.Etl.Design
    Runtime/                      Elsa.Etl.Runtime
    Persistence/EntityFrameworkCore/
                                  Elsa.Etl.Persistence.EntityFrameworkCore
    Api/                          Elsa.Etl.Api
    Workflows/Design/             Elsa.Etl.Workflows.Design
    Workflows/Runtime/            Elsa.Etl.Workflows.Runtime
  tests/Tests/                    Elsa.Etl.Tests with shared fixtures
  studio/Elsa.Studio.Etl/          Studio module/frontend tests
e2e-tests/etl/                     HTTP journey against rebuilt Workbench
```

Compose optional features through existing host manifests. Register the `Elsa.Etl.` architecture path bucket, EF dependency admission, schema/migration inventories, solution entries, lock files and generated maps. Authoring-only, runtime-only and combined deployments remain distinct. Change no workflows, permissions or release/deployment configuration.

## Delivery Order

1. Ratify the domain addition and review these artifacts. Independently test the existing bookmark/cancellation seam.
2. Establish versioned artifact/state contracts and bounded parser/transform tests. Add only store contracts needed by real consumers.
3. Implement durable admission, partition claims, bounded batches, chunk acceptance, terminal outbox and cancellation tombstones. Prove recovery before UI.
4. Integrate native publication pins and suspend/resume; prove Sequence/Flowchart and business-error/fault/cancellation with real persistence.
5. Implement authorized API/Studio and rebuilt-server/browser acceptance.
6. Review integrated changes and safeguard-removal/restoration evidence, architecture/maps/migrations, exact-head CI and resulting main. Parent #2908/program remain open.

## Risks and Evidence Limits

- Cross-store delivery repeats; both sides need durable identity and acknowledgement tests.
- Physical chunks differ from accepted output. Unaccepted/partial chunks must not appear completed.
- Lease expiry does not release an owed reservation or a live task's buffers.
- Two-process file recovery requires a declared shared filesystem and durable database, not arbitrary nodes with unrelated disks.
- CSV quoted fields span physical lines. Bound bytes/fields while parsing, not after allocating the record; test invalid UTF-8 and output expansion.
- Missing/corrupt snapshots or unsupported persisted formats are explicit refusals. Retention/rollback keeps readers and snapshots for accepted runs.

## Complexity Tracking

No complexity exception is requested. Domain admission is explicitly pending; this proposed design is not production acceptance evidence.
