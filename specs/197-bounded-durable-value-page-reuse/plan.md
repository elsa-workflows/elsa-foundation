# Implementation Plan: Bounded Durable-Value Page Reuse

**Branch**: `claude/runtime-db-materialization-spec-2396` | **Date**: 2026-10-07 | **Spec**: [spec.md](spec.md)

**Input**: Approved-for-planning draft `specs/197-bounded-durable-value-page-reuse/spec.md`

## Summary

Reuse detached raw `IDurableValueStateStore.ListPageAsync` results only inside an eligible, actively owned coalescing execution segment. Re-run the existing overlay merger for every logical read so staged additions, replacements, and deletions stay visible. A single `CoalesceDurableValueReads` control keeps enabled and disabled runs on the same coalescing cadence; Immediate mode and non-EF or ambiguous compositions keep the existing provider path. Eligibility is captured from the existing runtime backend ownership metadata before decoration, plus the built-in stable continuation codec. Eligibility must prove the effective singleton codec is the same object used by the inner EF store; custom, transient/scoped or unprovable codec compositions bypass reuse. Per-call access, cancellation, identity, owner, and request checks remain in force. Writes and ownership boundaries fence entries; finite limits force complete uncached fallback. The feature does not change paging or durability contracts and makes no latency claim.

The design was accepted through T08. Implementation is now active under #1306: [T001's nonempty EF baseline](evidence/nonempty-ef-baseline.md) is accepted; subsequent tasks and all delivery gates remain tracked in [tasks.md](tasks.md). The integrated SQLite EF fixture now proves six backing page SELECTs versus two with equivalent normalized results. This is fixture evidence; no primary HTTP/REST page-reuse saving or latency result is accepted.

## Technical Context

**Language/Version**: C# on .NET 10.0.

**Primary Dependencies**: Existing Microsoft.Extensions.DependencyInjection registrations, Elsa Runtime coalescing services, EF Core durable-value store, `System.Text.Json` models, and the existing `IRuntimeRecoveryContinuationCodec` contract.

**Storage**: No schema or migration change. The cache is private, in-memory state owned by one coalescing drain session. Supported EF providers remain governed by the existing provider test project.

**Testing**: Runtime unit tests, Runtime EF integration tests, EF provider tests, plus the existing rebuilt PostgreSQL HTTP reference and valid REST regression scripts. The program lead selects regression-first verification for this work unit: preserve an uncached non-empty EF baseline, add behavior regressions before or alongside implementation, and retain before/fixed or focused revert/mutation bite-proof. The owner has delegated implementation and QA decisions; no test-cadence approval is outstanding. This does not amend the application constitution.

**Target Platform**: Elsa Runtime server hosts using Coalesced checkpoint persistence.

**Project Type**: .NET runtime library and its tests.

**Performance Goals**: A deterministic non-empty normal-path fixture must show fewer backing durable-value page requests with reuse enabled than disabled while preserving byte-equivalent observable inputs and results. No latency or end-to-end speedup is guaranteed; bounded latency comparisons remain with T17/T18.

**Constraints**: Preserve Immediate behavior, checkpoint cadence, provider cursor semantics, access checks, overlay visibility, and write-failure behavior. Start with session-local caps of 32 pages, 1,024 rows, and 4 MiB of conservatively accounted retained content; successful boundaries clear the old generation before beginning a fresh bounded segment. These are engineering guardrails, not measured-optimal values or exact heap accounting. Never truncate a provider result to fit a cap.

**Scale/Scope**: One ordinary scoped access context, one workflow execution, and one active coalescing owner. No persistent cache, global cache, point-`FindAsync` miss cache, schema change, public store interface, or projected-value cache.

### Source baseline and evidence

Source claims in this plan were checked against pinned Git objects at `a6684744daa7df444a2b2944a6984172ab3470eb`. The graph index was used for symbol discovery only. The T07 report is merged at `7bb1aaecb3567b7ff973be8ec1daf35258395f64`; it records a reachable empty-state CLR mechanism signal, not a measured reduction for the primary HTTP or REST workload. Source mechanism, measured capture ancestry, and expected benefit remain separate claims.

### Constitution Check

**Before design — PASS**

- Framework §2.10: no combined query/mutation API is added; existing page reads and writes remain separate.
- Framework §2.21.1: existing test subjects and assertions remain; new tests add safety and reduction evidence.
- Framework §2.21.2 leaves greenfield test cadence to the application. This work unit selects its verification cadence under the existing program gates and preserves §2.21.1 test continuity; no new application-wide rule is asserted.
- Elsa §E2.2.3: Immediate and Coalesced deployment selections remain available; no deployment shape is removed.
- Elsa §E2.6: the change stays in Runtime over its existing runtime state contract and introduces no Design dependency.

**After design — PASS**

The page memo is a bounded, session-owned Runtime Services implementation. Its implementation class is `public sealed` for direct unit testing under framework §2.23.3; its retained state and entry representation remain private. Public paging, runtime store, cadence, and durability contracts remain unchanged. All reuse is conditional and falls back to the existing complete provider result.

### Design decisions

1. **Store raw provider pages only.** Key each entry by the current access context (`scope`, policy, purpose, across-scopes flag), workflow execution, inner page limit, exact opaque input continuation, and continuation-codec object identity. Do not parse or normalize the token in Runtime Services. Cache only after the inner EF call succeeds, so non-null input cursors have already passed the provider's decode and binding checks. Re-run the existing staged-page merger for every logical read.
2. **Limit automatic eligibility to owned first-party EF composition.** Before replacing `IDurableValueStateStore`, inspect the current runtime backend metadata and effective durable registration descriptors. The marker must be the single unambiguous `EntityFramework` backend and must own the currently selected durable contract and its effective owned concrete registrations. Any custom, in-memory, ambiguous, missing, or later-overridden composition bypasses reuse without changing the store's existing behavior. This is trusted host registration metadata, not a security attestation. Do not add an EF provider-type probe, reflection-based provider discovery, or a new public cache interface. Capture the decision narrowly when constructing the decorated store/session; do not mutate global options because a host is ineligible.
3. **Preserve signed-cursor rejection behavior.** The pinned `EfDurableValueStateStore.ListPageAsync` checks cancellation, identity, and scope, then decodes and validates a non-null cursor before querying. A cache hit may rely on a previous successful validation only for the same complete request key and the same built-in `HmacRuntimeRecoveryContinuationCodec` instance. That codec is sealed, captures its key at construction, and has deterministic decode behavior. Custom `IRuntimeRecoveryContinuationCodec` implementations bypass reuse because they may revoke or expire a token. A different codec instance or key cannot match a memo entry. Keep access and cancellation checks live on every call; do not copy EF cursor parsing or its private purpose constant into Services. The same proof applies to deterministic EF identity/scope-length validation of immutable exact keys: invalid requests never admit, changed keys never hit. Use the existing `Current.RequireScope()` on every eligible call, and require a unique scoped or singleton accessor descriptor shared with the EF inner store; transient, missing, changed or ambiguous accessor composition bypasses. Do not redesign the pre-existing workflow-ID-keyed overlay as part of this memo.
4. **Keep one reversible behavioral control.** Add `CoalesceDurableValueReads = true` beside `CoalesceInspectionReads` in the existing coalescing options and expose it as a shell manifest setting for Coalesced mode. `false` disables only page reuse; cadence and checkpoint coalescing remain identical. Immediate returns before coalescing registration and stays unaffected. When an authored segment-cap override creates per-session options, copy both boolean controls rather than resetting them to defaults.
5. **Fence cache state around actual writes.** Use cache-local synchronization, a generation, and an active-write count. Advance the generation and clear before an actual inner checkpoint commit or direct durable-value `SaveAsync`/`DeleteAsync`, and clear/fence again in `finally`. While a write is active, bypass hits and fills. A failed or cancelled write disables admissions for the rest of that session. For a healthy memo, a successful boundary clears and starts a fresh empty generation. Do not invalidate on logical `BufferDeferred`, which has not reached the provider. A late page load can be admitted only if its generation, owner, current request context, and active session still match. Bypass overlapping reads rather than serializing provider work; preserve the provider's existing consistency semantics.
6. **Respect owner lifetime and nesting.** The session and overlay remain single-writer. Cache data gets its own small lock. Entering a nested coalescing owner permanently disables the parent memo for that scope; a legitimate child begins with an empty memo. Dispose, drain interruption/cancellation, deactivation, and recovery clear only cache state and cannot alter the existing overlay or cancellation path. Recovery starts with a new empty session.
7. **Detach values and enforce complete bounds.** Deep-clone entries into cache-owned immutable snapshots and clone again on each hit. Include every durable-value field, mutable metadata map, type schema and inline `JsonElement`, external reference, and nested external-reference metadata. Successful empty pages may be cached; exceptions, rejection, and cancellation are never cached. Estimate retained content deterministically before cloning, with checked arithmetic over keys, input/output continuations, row strings/metadata, and JSON raw UTF-8 plus fixed entry/row overhead. At any limit, return the complete provider result uncached and clear/disable further admission until a successful boundary or reset. Do not truncate or add eviction complexity.

### Project Structure

#### Documentation (this feature)

```text
specs/197-bounded-durable-value-page-reuse/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/page-reuse.md
└── checklists/requirements.md
```

#### Source and tests expected during implementation

```text
src/essentials/Workflows/Runtime/Api/Coalescing/
src/essentials/Workflows/Runtime/Services/Coalescing/
src/essentials/Workflows/Runtime/Core/Models/
tests/essentials/Workflows/Runtime/Tests/
tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/
tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/ProviderTests/
e2e-tests/http/Capture-RuntimeDbPaging2392Reference.ps1
e2e-tests/http/Capture-RuntimeDbRest2386Control.ps1
```

**Structure Decision**: Keep production code within the existing Runtime API and Services coalescing layers; use a directly testable `public sealed` memo implementation under framework §2.23.3, with private retained state and no new Core cache capability interface. Put deterministic behavior tests in Runtime tests, first-party EF composition and page-count proof in EF integration tests, provider gates in the existing provider project, and real PostgreSQL HTTP/REST regressions in the existing HTTP scripts.

### Verification gates

Before delivery, prove enabled/disabled equivalence and a deterministic non-empty page-call reduction through a normal typed start → deferred `ActivityStarted` → invoke flow using the actual eligible first-party EF registration. Use test-only command interception or an equivalent narrow test counter; do not infer a reduction from T07's empty-state probe. Cover overlay changes, direct and checkpoint writes, write failure/cancellation, read/write overlap and late fills, identity and codec changes, ownership/nesting/disposal/recovery, bounds, cloning, and authored cap-option copying. Run the existing C4 concurrency, recovery, partition-isolation, and supported-provider gates, then rebuild the owned PostgreSQL host and run the HTTP `200` / `Alice Smith` reference plus valid REST regression before delivery. No new performance CI gate is introduced.

## Complexity Tracking

The implementation review on 7 October corrected the design's literal `internal` visibility to the framework's required `public sealed` implementation convention (§2.23.3). Reflection and `InternalsVisibleTo` were rejected. This adds a concrete Services implementation surface for direct tests; it does not add a provider capability interface or change public store/paging, persistence, cadence or durability contracts.

The existing backend metadata is sufficient for conservative eligibility without a new contract-to-concrete mapping API. Before decoration, validate complete owned-descriptor presence through the existing guard (treat its rejection as ineligible), then capture the owned descriptors and expected post-decoration service registrations. At activation, verify the watched groups, unique backend marker and stable singleton codec against the finalized composition. Watching all owned concrete registrations conservatively detects replacement of the concrete reached by the first-party durable-store factory without probing an EF type. The actual first-party EF integration oracle must pass; a design that merely makes every host ineligible is not accepted.
