# Implementation Plan: Bounded Coalesced Runtime-Store Page Merging

**Branch**: `claude/runtime-db-paging-2392` | **Date**: 2026-10-05 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/193-bounded-coalesced-pagination/spec.md`.

## Summary

Reduce repeated inner-store page calls made while a Coalesced runtime store merges durable rows and session overlays. Use a bounded page buffer and per-call exhaustion state; keep fetched-but-unemitted candidates recoverable at output-page boundaries without persisting row payloads or introducing global cache state. Preserve sorted overlay semantics, existing store interfaces, opaque continuation compatibility, Immediate defaults, and durability/fencing/inspection behavior. Implementation is assigned to #2393 only after this plan and its tasks are reviewed. Task #2392 also requires an exact-current-head Coalesced normal-host reference trace; it remains pending while the shared build machine is overloaded.

## Technical Context

**Language/Version**: C# / .NET 10.0.300 SDK on the current host.
**Primary Dependencies**: Existing Elsa runtime store abstractions, EF Core runtime persistence adapter, and existing PowerShell REST e2e helpers.
**Storage**: Existing runtime stores; PostgreSQL 16 is used only for the separate normal-host reference trace in a uniquely named disposable container/database.
**Testing**: `tests/essentials/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj`, plus the manual HTTP capture fixture in `e2e-tests/http/Capture-RuntimeDbPaging2392Reference.ps1` after the host trace can run.
**Target Platform**: Workbench normal host and the existing supported runtime-store providers.
**Project Type**: .NET runtime library with REST/e2e validation.
**Performance Goals**: Deterministic bounded inner-page call counts in correctness tests. Elapsed time is evidence only; it is not a CI threshold.
**Constraints**: Do not exceed `RuntimeStorePageRequest.MaximumLimit` (currently 500); do not materialize the whole store; do not add arbitrary row data to tokens, global caches, public store-interface changes, or provider-specific pagination assumptions. Preserve stable ordering/replacement/deletion, cancellation, old `crsp1` tokens, Immediate default mode, atomic durability, fencing, and inspection readback. The normal-host reference is not equivalent to the historical custom-transform workload.

## Constitution Check

**Initial and post-design gate: PASS with scoped constraints.**

- Framework §2.21.1 requires existing tests and their objectives to remain intact when changing existing runtime behavior. Extend the current coalesced paging test surface; do not delete or weaken existing assertions.
- The design adds no public type, store interface, provider contract, or module boundary. It keeps implementation state inside the existing coalesced read path and its existing runtime test project.
- The regression proof is deterministic: assert visible rows and inner-store calls, then show the call-count test red on the unmodified merger and green on the correction. It does not introduce a global performance gate.
- Preserve Elsa §E2.6 artifact-only execution and the existing executable/runtime boundary. The feature does not change authored definitions or their execution eligibility.
- Framework §2.24 remains draft and Elsa §E2.9 remains provisional; this plan does not rely on either section or introduce a new sanctioned pattern. No change is proposed to either constitution.

## Design Decisions

1. Read a bounded durable page into a transient buffer no larger than the request limit (already validated at 1–500). Merge against the overlay from the last emitted identity, releasing rows as they are emitted or suppressed.
2. Track exhaustion in the merge call. An empty terminal read becomes exhausted immediately and is not queried again within that call. The existing `RuntimeStorePage<T>` constructor rejects an empty page with a non-null continuation, so the merger does not advance such a page or widen that contract.
3. Preserve a fetched candidate through row selection and has-next look-ahead. If an output page boundary leaves fetched rows unconsumed, serialize the position before that bounded provider page with the last emitted identity and replay/filter on resume. This intentionally permits one bounded page replay on a later caller request and stores no row payload.
4. Commit a provider continuation after a fetched batch only after its rows have all been emitted or suppressed. A terminal page with an unconsumed visible row is not yet an exhausted position.
5. Preserve the existing opaque `crsp1` envelope, query binding, and malformed-token failure. Verify a valid pre-change token by test.
6. Use existing counting-store/session fixtures in `RuntimeCheckpointCoalescingTests.cs`. Keep the issue #2392 reference e2e separate from the implementation in #2393 and label its built-in `elsa.intrinsic.set@1` computation non-equivalent to the original custom CLR transform.

## Project Structure

### Documentation (this feature)

```text
specs/193-bounded-coalesced-pagination/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── reference-trace.md
├── checklists/requirements.md
├── contracts/coalesced-page-merge.md
└── tasks.md
```

### Source and validation paths (implementation issue #2393)

```text
src/essentials/Workflows/Runtime/Services/Coalescing/CoalescingRuntimeStateStores.cs
src/essentials/Workflows/Runtime/Services/Coalescing/CoalescingRuntimeStoreContinuation.cs
src/essentials/Workflows/Runtime/Core/Models/RuntimeStorePage.cs
tests/essentials/Workflows/Runtime/Tests/RuntimeCheckpointCoalescingTests.cs
tests/essentials/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj
e2e-tests/http/Capture-RuntimeDbPaging2392Reference.ps1
```

No other provider or API files are in the initial implementation scope. If a supported provider cannot honor the existing continuation semantics needed by this contract, stop and request a reviewed design adjustment before changing its interface.

`RuntimeStorePage.cs` is a read-only reference for the existing constructor validation; no change to that file or the public page contract is in scope.

**Structure Decision**: Keep the change within the existing runtime coalescing merger, its existing cursor representation, and focused runtime tests. Reuse `_ElsaCommon.ps1` for normal-host REST setup; the scoped HTTP fixture adds no process/database lifecycle harness.

## Validation Strategy

1. Before the implementation, add deterministic tests and run the exact named tests against this baseline to record the expected red call-count failure. Do not change production code for the pre-fix proof.
2. Implement #2393, then run the entire affected runtime test project as a whole. Verify all pages and exact call counts, cancellation, empty-terminal exhaustion, the existing constructor rejection for an empty page with a continuation, equality replacement, tombstone suppression, and valid pre-change token decoding.
3. Perform a mutation bite-proof by restoring the repeated-probe behavior or disabling buffer retention, show the bounded-call test failing, restore the fix, and rerun the complete focused project green.
4. Run `e2e-tests/http/Capture-RuntimeDbPaging2392Reference.ps1` on the rebuilt current candidate with PostgreSQL 16 in a fresh disposable container/database. This is a manually invoked capture, outside the global `Test-*.ps1` HTTP suite; it is not a routine test or performance gate. Keep response correctness, terminal state, per-run cadence, SQL command output, and settled follow-up work as separate evidence. The initial #2392 baseline uses SHA `7b8e5d1304c198ce80dcc3d17aed5598245c7352` and the same settings for after comparison.
5. Root owns the final integration gates: relevant REST e2e, architecture guard, generated-maps check, full diff review, and issue/Project transitions. No measurement is interpreted as a latency result while the shared machine is overloaded.

## Complexity Tracking

None. The design retains existing interfaces and token envelope and adds no new project, service, global state, public cursor type, or provider-specific behavior.
