# HTTP source recovery and physical-attempt checkpoint

Scope: T014 in [spec 996](../spec.md), issue #3151, draft PR #3156.
This completes T014, extending the existing source/cache and shared-worker evidence. **14/22 tasks**
are complete. The HTTP leaf remains an incomplete draft; final delivery gates still apply.

## Source recovery boundary

`Source_recovery_uses_changed_response_only_when_original_manifest_was_never_published` uses a real
loopback server that returns different valid JSON on its second request. The first transport response is
written as a content-addressed body, optionally followed by its immutable request manifest, before the
original lease is released. A new SQLite context/store claims a higher fence.

- With the manifest, the successor recovers the original bytes and original attempt/fence without a second GET.
- Without the manifest, the raw body remains an unreferenced object. The original attempt becomes uncertain;
  the successor honors its persisted retry delay, then fetches and captures the changed representation under
  a new physical permit. The first permit and fixed request deadline remain in the journal.
- Both paths verify exact raw bytes/hash, one attached page, released source input reservation, journal
  capture identity and replay without another GET. The orphan body remains byte-identical. No orphan cleanup
  or coherent remote snapshot across pages is claimed.

The acquisition fixture now shares an optional clock between the real transport and store. Existing cases
continue to use system time; the new case advances only the persisted retry eligibility boundary.

## Physical attempt lifetime

`Http_attempt_replacement_keeps_capacity_through_cancellation_and_reuses_the_published_page_once` stops
the background scheduler and invokes the real DI-composed connector execution path with a shared attempt
budget and real loopback transport. It is an in-process lifetime proof; the separate
[process checkpoint](http-process-checkpoint.md) covers actual child-worker recovery.

The first GET is held at the server while its partition lease expires and a higher fence is claimed. The test
checks records/input/lookup/output allowances independently while the GET remains pending, after replacement,
and during asynchronous cleanup following cancellation. The response is released inside its source-attempt
window; its immutable cache is published, but cancellation prevents the old owner from committing SQL capture.
No attached page or batch exists before the successor runs. Only complete unwind releases the old allowance.

The successor uses that original page without another GET, preserving the original attempt ID, fence and
receipt timestamp. It produces exactly one Ada record in JSONL, no error output, one accepted batch/item and
zero outstanding quota. Retained input includes both lookup bytes and page bytes. This does not imply that
cancellation can stop a remote server or undo an external effect. Native cancellation remains T015.

Both process and in-process test adapters now inherit one shared connector forwarding adapter, preserving the
optional connector capability and avoiding duplicate method forwarding. The existing seven process cases are
included in the full ETL rerun after this extraction.

## Existing and strengthened acceptance

| Requirement | Executable evidence |
|---|---|
| Missing/corrupt body or missing manifest after SQL attachment never authorizes refetch | `Source_acquisition_never_refetches_missing_or_corrupt_attached_page`; unchanged source journal and page count |
| Raw body hash, manifest identity/checksum and valid-looking provenance corruption | `EtlHttpPageCacheTests` |
| Missing manifest versus missing referenced body versus unavailable root | `Missing_manifest_is_absent_but_missing_body_and_unavailable_root_are_failures` |
| Conflicting later body cannot overwrite a published page | `A_changed_body_for_an_existing_request_is_a_conflict_and_keeps_the_original` |
| Empty pages retain continuation; success waits for exhaustion | `Source_acquisition_fetches_real_pages_and_retains_empty_page_continuation`, `Empty_http_pages_advance_without_accepting_records_and_success_waits_for_exhaustion` |
| Exhausted source cannot dispatch another GET | `Source_acquisition_cannot_dispatch_again_after_durable_empty_page_exhaustion` |
| Partial page acceptance and final fully filtered acceptance retain correct accounting | `Http_partial_and_full_page_batches_recover_and_final_fully_filtered_acceptance_terminalizes` |
| Real shared worker fully filtered input sends no output | `Fully_filtered_connector_batch_advances_without_dispatching_an_output` |

## Review and verification

Independent review identified missing acquisition-level checks for a removed manifest after attachment and
an already exhausted partition. Both were added; the exhaustion case keeps a sibling active so whole-run
terminalization cannot mask a repeated dispatch. Root integration also corrected a test expectation that omitted
lookup bytes, and allowed an absent error-output directory when there are no error effects. Four targeted
controls detected their expected failures:
body hash removal, manifest checksum removal, bypassed published-page recovery, and premature physical-capacity
release before cleanup. The published-page case failed when recovery was bypassed while the orphan-body case
still passed. The replacement case failed at its cleanup-capacity assertion when disposal moved earlier. Each
production source was restored byte-for-byte before final validation.

Three bounded root/independent review passes covered source coverage, physical-attempt lifetime and shared
fixture integration, and final evidence/diff checks. No actionable finding remains. Focused source tests passed
29 cases before the final two-shard exhaustion refinement; the focused integrated replacement/exhaustion pair
then passed. The final full ETL suite includes all final source changes and the seven process cases.

| Final gate | Result |
|---|---|
| `dotnet test Extensions/Etl/tests/Tests/Elsa.Etl.Tests.csproj --no-restore` | 941 passed, 0 failed, 0 skipped |
| `dotnet test Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj --no-restore` | 584 passed, 0 failed, 0 skipped |
| `dotnet run --project tools/maps/Elsa.Maps.Generator -- check` | Passed |
| `dotnet run --project tools/maps/Elsa.Maps.Generator -- solution-filters-check` | Passed |
| Relative documentation links and `git diff --check` | Passed |

No production behavior is changed by this checkpoint. Exact-head CI and final main verification remain
separate gates. Historical receipt/HTTP/SQLite-open timeout causes remain unproved; main tracker #2293 remains
independent. T003 predecessor compatibility, T015 native cancellation, T017 policy/telemetry, T018 inspection,
T019 real Workbench/browser acceptance and T020–T022 final delivery remain open. The task runner and scoped
worker pause remain follow-up work. External effects remain at least once.
