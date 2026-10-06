# Research: Bounded Coalesced Runtime-Store Page Merging

**Date**: 2026-10-05
**Initial source-audit candidate**: `7b8e5d1304c198ce80dcc3d17aed5598245c7352`. The later captured binary was built at `5d28bd0cd3d76004988a01b0f0abd7af3af313d6`; runtime source is unchanged across those documentation/capture commits.
**Graph source**: primary checkout `Users-sipke-Projects-Elsa-elsa-foundation-main`; the worktree was not indexed.

## R1. Current merge loop issues one-row reads and loses per-call reuse

`CoalescingRuntimeStorePageMerger.MergeAsync` in `src/essentials/Workflows/Runtime/Services/Coalescing/CoalescingRuntimeStateStores.cs` asks `ReadNextInnerAsync` for a durable candidate in each output-loop iteration. `ReadNextInnerAsync` calls the inner store with `limit: 1`. When an overlay identity sorts before a fetched durable row, `MergeAsync` restores `candidate.Before`; the next iteration queries the same row again. The has-next probe also sets the cursor to `candidate.Before`, so a candidate found only for look-ahead is read again on the next caller request.

When `ReadNextInnerAsync` receives an empty terminal page, it returns `null` without updating `InnerCursor.Exhausted`. The outer loop can therefore query an empty source again for each overlay value it emits. This is the direct deterministic mechanism for repeated calls; it does not by itself attribute any historical total database-read count to this merger. The existing `RuntimeStorePage<T>` constructor rejects an empty page with a non-null continuation, so no such page needs to be advanced by the merger.

The same implementation skips durable rows at or before the last emitted identity and rows suppressed by an overlay. When a single fetched durable row has a null next token, `candidate.After` marks that position exhausted. It is only safe to retain that terminal position after the candidate has been emitted or suppressed by the equal-identity overlay. A fetched but un-emitted row remains recoverable only from the position before that candidate unless its row payload is stored elsewhere.

## R2. Existing request bounds and continuation are public compatibility constraints

`RuntimeStorePageRequest` in `src/essentials/Workflows/Runtime/Core/Models/RuntimeStorePage.cs` defaults to 100 rows, caps requests at 500, and treats its continuation token as opaque. `CoalescingRuntimeStoreContinuation` in `src/essentials/Workflows/Runtime/Services/Coalescing/CoalescingRuntimeStoreContinuation.cs` currently encodes query binding and a cursor as checksummed `crsp1` JSON. Decoding rejects malformed tokens and tokens bound to another query. Keep that envelope backward compatible; do not add a provider-neutral public cursor contract or put arbitrary row data in it.

## R3. Correctness dimensions are specific to stable traversal

The existing overlay ordering and identity comparison use `StringComparer.Ordinal`. The correct merged result is thus the ordinally sorted union of durable rows not tombstoned or replaced by an overlay and all overlay upserts. An equal-identity overlay row wins once. A deterministic multi-page test can compare concatenated results directly to this expected logical view while counting inner page reads.

Page request, provider batch, and continuation boundary are distinct. A bounded in-call buffer can avoid refetching one candidate during merge selection and has-next look-ahead. At an external page boundary, if fetched rows remain un-emitted, the continuation must point to the provider position before that batch and carry the last emitted identity. Resumption may replay that bounded batch and filter identities at or below the last emitted identity. This avoids retaining arbitrary row payloads across calls; the possible bounded replay is an explicit compatibility tradeoff, not an unmeasured guarantee of zero reads across distinct requests.

## R4. Relevant existing test surface

The affected focused project is `tests/essentials/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj`; existing coalesced paging coverage is in `tests/essentials/Workflows/Runtime/Tests/RuntimeCheckpointCoalescingTests.cs`. `Coalesced_activity_pages_merge_overlay_without_traversing_the_inner_collection` currently covers a small overlay/persisted interleave and does not assert the stronger bounded-call contract. Extend the existing counting-store and session helpers rather than adding a second harness.

The successful HTTP workflow reference should reuse the repository's REST/e2e composition approach in `e2e-tests/http/Test-HttpEcho.ps1` and helpers in `e2e-tests/_ElsaCommon.ps1`. That reference validates a normal host path; it does not replace deterministic merger tests.

## R5. Narrow decision and limits

Use a bounded per-call durable-page buffer and per-call exhausted-source state. Read each provider cursor position at most once per merge call; a bounded page may be replayed on a later call when an output boundary leaves fetched rows un-emitted. Persist only the last emitted identity and provider position needed to replay that batch. Do not add global caches, change store interfaces, alter Immediate defaults, or relax durability/fencing/inspection. Internal batch size and cursor handling must be reviewed against every adapter used by the affected stores before implementation. Preserve the existing `RuntimeStorePage<T>` rejection of an empty page with a non-null continuation.

## R6. Host trace status

The pre-fix Coalesced normal-host trace is actual evidence, separate from the source mechanism above. The agreed fixture is specified in `quickstart.md`; the narrow build, owned PostgreSQL16.15 capture, effective readback, corrected engine-span ancestry and cleanup are recorded in [reference-trace.md](reference-trace.md). Root and independent QA accepted curated trace reconciliation. The fixture remains representative and non-equivalent to the earlier custom transform. Timing, untraced/background settled work, SQL-statement census and provider round trips remain explicitly qualified; no runtime optimization has been implemented by #2392.
