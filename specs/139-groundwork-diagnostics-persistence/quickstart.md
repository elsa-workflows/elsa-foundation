# Quickstart: Durable Diagnostics Persistence

## Planning validation

```bash
rg -n "NEEDS CLARIFICATION|\[FEATURE|\[###|TODO|TBD" specs/139-groundwork-diagnostics-persistence
```

## Focused test progression

```bash
dotnet test tests/Elsa/Diagnostics/StructuredLogs/Persistence/Groundwork/Tests/Elsa.Diagnostics.StructuredLogs.Persistence.Groundwork.Tests.csproj
dotnet test tests/Elsa/Diagnostics/OpenTelemetry/Persistence/Groundwork/Tests/Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork.Tests.csproj
dotnet test tests/Elsa/Diagnostics/Persistence/Groundwork/Tests/Elsa.Diagnostics.Persistence.Groundwork.Tests.csproj
```

The replay targets Groundwork `0.0.1-preview.81`. Runs recorded against preview.73, preview.75, or
preview.80 are historical provenance only and cannot promote the lane. The T043 manifest records one
clean exact-branch-head preview.81 lifecycle certification; it does not replace the focused and
four-provider recertification required after any later `main` integration.

## Required provider certification

Run the same highest-seam diagnostics fixture against:

1. SQLite on a real database file;
2. SQL Server in the pinned integration container;
3. PostgreSQL in the pinned integration container; and
4. MongoDB in the pinned replica-set integration container.

For each provider, retain test evidence for schema validate/apply, restart, concurrent writers, acknowledgement loss, exact retention, cross-scope isolation, and bounded plans for scale-bearing operations.

## Performance gate

Performance measurement and gate ratification are owned by issue #646 and the spec-094 performance
handoff. This lane consumes the resulting diagnostics verdict and retained artifacts; it does not
create a private harness or self-authorize a replacement gate. Keep the EF oracle until #646 records
a passing or program-owner-ratified verdict.

## Replay-base evidence (not promotion evidence)

On 2026-07-24, before integrating the then-current `main`/Groundwork preview.81, the replay implementation
compiled and passed:

- Structured Logs Groundwork: 17/17.
- OpenTelemetry Groundwork: 68/68.
- Combined diagnostics deployment schema: 6/6.
- Provider-neutral drain and observability slice: 49/49.
- Architecture slice excluding the intentionally pending zero-EF assertion: 19/19.
- Feature, readiness, lifecycle, schema, and durable-operation slice: 34/34.
- OpenTelemetry core/receiver regression suite, preserving the #692 trusted-ingestion seams: 71/71.
- Structured Logs core regression suite, preserving the #649 replay/live-delivery seams: 74/74.

These results prove the replayed implementation is internally coherent on its preview.80 base. They
do not complete T008, T022, T027, T033, T050-T052, or any EF-removal task, and they must not be
represented as preview.81/four-provider certification.

## Preview.81 exact-head T043 lifecycle recertification (2026-07-24)

On clean `codex/642-groundwork-diagnostics-replay` head
`df39cc9aae3ad5ac2ebfdb0f4589c88b014a1d52`, with Groundwork resolved at
`0.0.1-preview.81`, the provider-neutral lifecycle command recorded in
[`evidence/us3-lifecycle-results.json`](evidence/us3-lifecycle-results.json) passed **49/49** in
Release with no failures or skips. At the run, `origin/main` was
`6458b39ad4a251c992a7384f1f325e164320d481`; the branch was 10 commits ahead and 9 commits behind,
with merge base `78033cf1167071123cb9fe5ef38653973bd65200`.

This certifies T043's load, shutdown, lifecycle, and observability boundary only. It does not certify
the container-provider matrix, performance verdict, zero-EF removal, or a future exact-main head.

## T053/T054 removal-ledger intake (2026-07-24)

[The EF test-removal ledger](ef-test-removal-ledger.md) inventories all 46 facts in the pending
Structured Logs (30) and OpenTelemetry (16) EF test projects, including their EF-only test hosts
and factories. It added and passed two previously missing replacement checks:

- `GroundworkStructuredLogReplayTests.Complex_structured_log_fields_round_trip_through_durable_storage`
  (Structured Logs Groundwork suite: **18/18 passed**);
- `DiagnosticsPersistenceReadinessTests.Selected_durable_stores_resolve_and_start_through_the_shared_host_lifecycle`
  (readiness slice: **7/7 passed**).

The same intake exposed two OpenTelemetry gaps. Metric/log `ServiceName` is now projected from the
durable resource catalog and filtered inside the diagnostic-record provider; the full OpenTelemetry
suite passes **69/69 with one explicit skip**. Repeated trace fragments still require merge-before-
filter semantics. A proposed retained-window client merge was rejected because it violated the
provider-bounded query contract. The red test is retained as an explicit skip blocked by
`valence-works/Groundwork#130`, which owns provider-native grouped diagnostic reduction. This blocks
trace parity and T054; it is not deletion evidence. No EF deletion, performance task, architecture
task, package/solution configuration, or task checkbox was changed by this intake.

## Final dependency audit

```bash
rg -n "EntityFrameworkCore|Persistence\.EFCore|DbContext|Migration" src/Elsa/Diagnostics tests/Elsa/Diagnostics
rg -n "Groundwork" src/Elsa/Diagnostics/*/Core
```

The first command may match temporary oracle code until the removal phase; it must have no diagnostics EF implementation/dependency matches at completion. The second command must remain empty throughout.

## Delivery rule

Work on the feature branch, implement by user-story slice with tests first, obtain an independent review, push the organization-owned branch, merge the reviewed PR, and leave the repository with all required checks green.
