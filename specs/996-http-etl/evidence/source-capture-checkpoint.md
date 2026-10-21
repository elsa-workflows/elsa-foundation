# Source capture and page-cache checkpoint

Scope: partial T005 of [spec 996](../spec.md), issue #3151, draft PR #3156.
Task completion remains 3/22. These are tested storage primitives, not a working HTTP source runtime.

## Implemented behavior

- Source request journals separate physical permits, response/uncertainty reconciliation and a captured
  page. Permits bind a stable request hash, attempt ID/number, owner fence, fixed deadline and expiry.
  The journal also binds the expected page sequence to prevent descriptor remapping.
  Retry timing is shared with output delivery without sharing its acknowledgement semantics.
- Only a complete HTTP 200 page can be captured. Other success-class statuses, including 206, are
  refused; invalid complete pages fail explicitly. Bare HTTP 200 is not an accepted page receipt.
- Capture validates the exact historical permit and complete-response timestamp. The journal can
  record a valid original capture after uncertainty and a later attempt's failure, preserving both
  physical records. Captured source state issues no new permits. Neither journal reconciliation nor
  capture proves that a physical request has terminated or releases its capacity.
- The file cache first publishes bounded content-addressed raw JSON, then a create-only request
  manifest. The manifest is the sole recoverable publication point and has its own checksum. Its key
  binds invocation, source revision, partition, page and request; its provenance identifies the original
  attempt. A raw body without its manifest remains uncommitted cache material.
- Recovery verifies manifest integrity, exact body size/hash and JSON decoding. Body references name
  the actual raw JSON bytes. Same-body replay preserves original provenance; changed content conflicts.
  Missing manifests, unavailable storage and missing/corrupt referenced bodies remain distinct. Cache
  failures expose generic codes without response values or storage paths.

## Review and verification

Root review corrected exact-permit validation on capture replay, mutable-list retention and zero-byte
page descriptors. A final contract review also bound capture to the expected page sequence. Cache review added a manifest checksum so a valid-looking timestamp change is detected.
The corruption test preserves valid JSON and byte length so it exercises the body hash; reopen assertions
compare decoded structures rather than dictionary reference identity.

Focused validation passed 89 tests with zero failures or skips. Four separate controls removed the body
hash check, manifest checksum check, original-attempt expiry check and page-sequence match. Each failed its intended regression;
production files were restored byte-for-byte afterward. The final full ETL suite passed 777 tests with
zero failures or skips. Architecture passed 584 tests with zero failures or skips; maps, solution filters,
59 relative links and diff checks passed. The first architecture run refused stale assemblies after source
changed during review; the final run used rebuilt code. Fresh-head CI remains separate pending evidence.

## Remaining acceptance

T005 still owes EF request-journal persistence and integration into claims, history audits and page attachment.
The current owner must authorize attachment using a validated historical capture. Reserve bounded outcome,
page/position and next-request metadata before dispatch, transfer nonempty-page capacity to acceptance without
double charging, and account for concurrent in-flight input acquisitions. Cache leftovers need a safe cleanup
policy; logical quotas do not bound all physical filesystem leftovers.

Runtime connector execution remains unavailable. Real HTTP composition, native/API/Studio journeys, process
interruption/replacement and frozen old-worker compatibility remain owed. The general task-runner and pause
follow-up is unchanged. External effects remain at least once. This checkpoint is not permission to merge
an incomplete draft and establishes no cause for historical timeouts.
