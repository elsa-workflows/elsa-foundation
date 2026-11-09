# Durable HTTP route delivery (partial T005)

An owed batch with a retained output plan can allocate a durable HTTP request attempt before transport.
Only a returned permit authorizes sending; replay can return the same live permit and the caller must
not send it concurrently. Each attempt retains the original effect identity/body, owner fence, request
identity, start and expiry. The first attempt anchors the route deadline, which is never reset on retry
or replacement. No network requests are made by these store tests.

Only 200/201/204 acknowledge. Null status represents uncertainty; 408/429/500/502/503/504 retry with
capped exponential backoff. A valid later absolute Retry-After extends the delay, even beyond the
backoff cap; a delay reaching the deadline exhausts instead. Transport parsing of delta/date headers
remains future runtime work. A request timeout is clamped to the remaining deadline without overflowing
near the supported timestamp ceiling. Late or mismatched receipts do not change durable work.

Replacement ownership and expired requests first close an attempt as uncertain, without authorizing
an immediate resend. A subsequent eligible begin allocates a new attempt under the same effect.
Main acknowledgement survives an error-route retry and replacement claim. When all remaining HTTP
routes are waiting, the indexed partition eligibility time yields acquisition to other partitions.
A mixed file/HTTP batch remains eligible while file publication evidence is not yet implemented.

## Durable accounting and schema

Attempt JSON/digest and batch/partition/run accounting commit atomically under the current lease,
with an authority recheck after saving. Replay does not charge again or move the source position.
Canonical journals use the existing run-wide metadata budget and private document bound. Each
in-flight route reserves 512 bytes of transition headroom; each waiting route reserves 256. Recording
a receipt or final deadline releases enough headroom for its bounded metadata growth, so a full run
budget cannot strand an already-authorized response. A new request pays for its history and replenishes
the reserve before receiving a permit. This bounds logical accounting, not physical database size.

Migration `20261021140000_ConnectorHttpDeliveryAttempts` adds nullable journal/digest and partition
not-before columns plus an eligibility index in place. Paired digest/output-dependency and positive
retry-time checks preserve old rows and the existing foreign-key graph. Released v1 migrations are
unchanged. Historical 1300 tests still target 1300; their reads/writes now mention only columns that
exist there. Structural upgrades do not prove retained old-worker execution.

## Review and validation

Root integration and three bounded delegated slices covered transitions, schema and store tests.
The first focused run passed 40 and found one test helper call using its default policy instead of
the test's custom policy. Root review corrected mismatched-effect receipt handling, stale-ID replay,
timeout boundary consistency and timestamp-overflow handling. A subsequent focused run passed 45.
Review then found the metadata headroom issue described above and added full-budget completion tests.
The final ETL suite passed **708 tests, zero failed/skipped**. The final focused run passed 47.
Two negative controls failed as intended: removing retry eligibility admitted the delayed partition
before unrelated work, and withholding released receipt headroom returned BudgetExceeded instead of
recording a 429 at full quota. Both production files were restored byte-for-byte before the full run.
Architecture passed **584 tests, zero failed/skipped**. Maps, solution filters, 53 relative links and
diff checks are recorded in the PR checkpoint receipt. Existing build warnings are not claimed resolved.

The relevant commands are:

```sh
dotnet test Extensions/Etl/tests/Tests/Elsa.Etl.Tests.csproj --no-restore
dotnet test Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj --no-restore
dotnet run --project tools/maps/Elsa.Maps.Generator -- check
bash tools/solution-filters/generate-solution-filters.sh --check
git diff --check
```

## Remaining acceptance

**3/22 tasks are complete; T005 remains open.** This checkpoint acknowledges route attempts without
advancing or settling input. Source-fetch attempts, file publication receipts, atomic accepted progress,
nonempty page advancement and terminal/outbox integration remain required. Connector execution remains
explicitly unavailable until the full runtime path is integrated. Old-worker resume, real HTTP transport,
native/API/Studio and two-process journeys remain owed. External effects remain at least once unless a
receiver's stronger contract is independently demonstrated. No workflow/queue pause capability or generic
task runner is implemented; the existing program follow-up preserves acceptance/dispatch/continuation
boundaries. Historical timeout causes remain unexplained.
