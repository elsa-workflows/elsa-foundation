# Source-fetch storage integration

Scope: T005 storage integration in [spec 996](../spec.md), issue #3151, draft PR #3156.
HTTP runtime execution remains unavailable; this checkpoint is not the full feature acceptance.

## Behavior and boundaries

- The optional connector store exposes typed begin/complete/capture operations. The additive SQLite
  migration preserves prior request/page/batch rows and initializes source journals to null and reservations
  to zero. Existing v1 provider interfaces and canonical artifact bytes are unchanged.
- Before returning a GET permit, a serializable transaction reserves run-wide logical input capacity and
  bounded outcome/capture/page/progress metadata. Concurrent partitions cannot spend the same capacity.
  Capture shrinks the input reservation to the real raw-page length. Attachment transfers it to retained
  input; batch reservation transfers progress metadata to acceptance without a second charge.
- New attachments require a matching durable capture receipt under the current partition lease. Original
  permit and receive-time provenance can survive replacement and uncertainty without rewriting history.
  Source waiting state persists its retry deadline and claim eligibility while another partition progresses.
- Post-save ownership and permit-expiry checks roll back a transaction that loses authority during saving.
  Recovery and successful terminal audits validate journal identity, capture/page agreement and reservation
  accounting. Historical legacy pages with null journals remain recoverable; new pages cannot use that path.
- Source receipt persistence is not cache verification or physical dispatch. Runtime composition still must
  read the actual immutable cache body, enforce local attempt capacity, retain cancellation responsibility
  and handle orphan cleanup. Logical input quotas do not bound every physical filesystem leftover.

## Review findings and corrections

The first full ETL run found eight failures: three historical migration fixtures selected columns that did
not yet exist at their intentionally old targets, four narrow downstream budget cases could not afford the
new earlier source reservation, and a two-continuation case exposed a real document-envelope defect.
Historical tests now project only the columns available at their original target. Downstream quota tests
seed explicit legacy attached pages and re-claim them through the normal full audit, preserving their
independent budget assertions instead of raising their quotas. Ordinary pipeline tests use real source
begin/capture/attach transitions.

The former 512 KiB metadata promise was mathematically false for some valid continuations. Repeating
`a>a` produces UTF-16LE Base64 `YQA+AGEA`; JSON encodes `+` as six bytes, giving thirteen encoded bytes
per three original UTF-8 bytes. Two maximum continuations exceed 512 KiB before wrappers. Connector
metadata now has a bounded 1 MiB envelope; the source reservation accounts conservatively for escaping,
while the 64 KiB continuation ceiling and aggregate metadata/input quotas remain unchanged. The exact
canonical encoding and existing persisted documents are preserved. A replacement-owner regression reads
both full-size escaped continuations. This is an explicit bound correction, not a transport workaround.

Bounded review also checked source-to-batch reservation transfer, streaming history audits, migration
constraints, terminal-operation coverage and the optional v1 provider seam. The self-review loop stopped clean after three passes: integration/fixture/envelope corrections,
terminal/provider coverage, then final diff and verification review. Final gate results below refer only
to this storage checkpoint.

## Verification

The final restored-source ETL suite passed **793 tests**, and architecture passed **584 tests**, with
zero failures or skips. Generated maps and solution-filter freshness, **65 relative evidence/program links**
and the staged diff checks passed. No generated map changed. Fresh-head CI remains pending.

Commands from the implementation worktree:

```bash
dotnet test Extensions/Etl/tests/Tests/Elsa.Etl.Tests.csproj --no-restore
dotnet test Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj --no-restore
dotnet run --project tools/maps/Elsa.Maps.Generator -- check
bash tools/solution-filters/generate-solution-filters.sh --check
git diff --cached --check
```

These validate the storage implementation and existing v1 journeys. The new HTTP Workbench/browser
journeys and process proof have not run because the runtime composition is still unavailable.

Four separate controls remove the matching-capture requirement, aggregate pre-dispatch input check,
post-save ownership check and corrected document envelope. Each failed its intended regression; each
production file was restored byte-for-byte. The ownership test gives its request permit a longer lifetime
than its partition lease so permit expiry cannot mask a missing ownership check.

## Remaining delivery

Actual scoped HTTP binding/transport composition, native Sequence/Flowchart continuation, authoring/API/
Studio journeys, two-process interruption/replacement, frozen old-worker resume and full product gates
remain owed. Runtime HTTP execution is not enabled by these store changes. The same #3151 claim and draft
continue. External effects remain at least once; task-runner/pause stays a post-program follow-up.
No historical timeout cause is claimed established.
