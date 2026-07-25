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

On 2026-07-25 the program owner ratified expanding the supplied bundle from 12 to 13 workloads with
`diagnostics-durable-history`, split into Structured Logs and OpenTelemetry suboperations. SQLite
uses the retained same-provider EF oracle; SQL Server, PostgreSQL, and MongoDB use absolute
operational budgets plus correctness digests, native plans, and Groundwork physical-form evidence.
This records the workload contract only: T050 and T051 remain open until #646 retains and imports the
measured verdicts.

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

On clean `codex/642-groundwork-diagnostics-replay` integration head
`47d3892446022a2b41a678f0913eba51b3ef4662`, with Groundwork resolved at
`0.0.1-preview.81`, the provider-neutral lifecycle command recorded in
[`evidence/us3-lifecycle-results.json`](evidence/us3-lifecycle-results.json) passed **49/49** in
Release with no failures or skips. The tested head had merged `origin/main`
`165cf20723e088ca1bcb1530abf2149cabacb2bc`; it was 26 branch commits ahead and 0 behind.

This certifies T043's load, shutdown, lifecycle, and observability boundary only. It does not certify
the container-provider matrix, performance verdict, zero-EF removal, or a future exact-main head.

## Preview.81 integrated four-provider recertification (2026-07-24)

On clean integrated head `47d3892446022a2b41a678f0913eba51b3ef4662`, after merging `origin/main`
`165cf20723e088ca1bcb1530abf2149cabacb2bc`, the exact commands retained in the US1 and US2
manifests passed:

- US1 real-provider matrix: **24/24** across SQLite, SQL Server, PostgreSQL, and replica-set MongoDB.
- Structured Logs adapter: **18/18**.
- OpenTelemetry adapter: **74 discovered, 73 passed, 1 explicit #130 skip**.
- Provider-independent durable operations: **5/5**.
- US2 real-provider query/scope/retention/native-plan matrix: **17/17**.
- Four-provider bounded-plan slice: **5/5**, including eleven record routes and eight catalog routes.
- Full shared diagnostics persistence checkpoint: **147/147**.

The provider fixture contains exactly the four mandatory providers and starts pinned SQL Server,
PostgreSQL, and MongoDB Testcontainers without fallback or provider skips; SQLite uses a real
database file. The provider and adapter projects were force-restored before their recorded runs,
and their assets resolve every Groundwork library to `0.0.1-preview.81`. The #130 skip remains a
truthful blocker for provider-side grouped trace reduction and repeated-trace merge across durable
batches; the passing matrix does not claim that behavior. The manifests retain the exact commands,
full relative source paths with SHA-256 hashes, provider case counts, and evidence boundaries. The
147-test result is adapter-checkpoint evidence only; it is not T057 final zero-EF certification.

## Independent-review remediation checkpoint (2026-07-24)

Three adversarial read-only reviews of the pre-remediation candidate found real correctness,
evidence, and scope defects. Commit `5538d8414` resolved zero-capacity retention, deterministic tied
catalog eviction, paged trace detail, the declared resource/trace service filters, corrupt catalog
taxonomy, and bounded physical plans. Commits `9ac3b62d8` and `d6e10c293` added four-provider
coverage for every declared trace filter plus resource, trace, metric, and log service-name filters,
and native-plan coverage for those record predicates. Commit `f3866b9cb` then resolved the
adversarial review's nine-value full-trace-filter blocker by raising only the trace stream's
predicate-value budget to nine; all other signal streams retain the tighter eight-value budget.
Commit `47d389244` aligned the SQLite adapter regression with that same complete nine-value filter.
The exact four-provider fixture supplies every trace filter together and separately certifies
resource status, search, and service filters.

The evidence review also found that the original T015 red baseline was recorded too late. T015
therefore remains unchecked as a non-retroactive process deviation; later green runs establish
behavior, not historical test-first compliance. The scope review found two future-phase guards
enabled before their prerequisites. Commit `5538d8414` removed the premature T047 zero-EF assertion
and T050 lane-local performance-contract tests. Both tasks remain unchecked and their tests must be
introduced test-first only when the EF-deletion and #646 performance phases begin.

The frozen checkpoint candidate `93312bcb04da2432e87379f0c5e6891436eb68b6` then received three
parallel, adversarial, read-only reviews over
`165cf20723e088ca1bcb1530abf2149cabacb2bc..93312bcb04da2432e87379f0c5e6891436eb68b6`.
The correctness/mechanism, evidence-integrity, and scope/test-preservation reviewers each returned
**CLEAN**. The correctness reviewer independently traced the nine-value trace path and bounded
provider plans; the evidence reviewer reconciled all 28 source hashes, commands, counts, provenance,
task states, and checkpoint boundaries; the scope reviewer confirmed the retained EF oracle,
#649/#692 coverage, one explicit #130 skip, and open future-phase tasks. No findings remained for
disposition after the remediations recorded above.

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
suite discovers **74 tests: 73 pass and one is explicitly skipped**. Repeated trace fragments still require merge-before-
filter semantics. A proposed retained-window client merge was rejected because it violated the
provider-bounded query contract. The pending contract test is retained as an explicit skip blocked by
`valence-works/Groundwork#130`, which owns provider-native grouped diagnostic reduction. This blocks
trace parity and T054; it is not deletion evidence. No EF deletion, performance task, architecture
task, package/solution configuration, or task checkbox was changed by this intake.

## Host-composition correction pending exact-head recertification (2026-07-25)

Inspection found that the original `DiagnosticsGroundworkPersistence` feature contributed only its
document manifest. The two concrete Groundwork adapters were not catalog-discoverable shell features, so
enabling the composite host feature did not select either `IOpenTelemetryStore` or
`IStructuredLogStore`. The correction makes that composite feature atomically delegate to both adapters,
marks it with the `elsa-diagnostics` schema contribution, and extends the reference deployment-schema
selector with diagnostics-only and identity-plus-diagnostics forms. The four unified provider leaves now
register the provider-native diagnostic-record session factory required by the selected store bindings.

`Elsa.Server` now references the combined diagnostics Groundwork composition project and selects
`DiagnosticsGroundworkPersistence` in `shells.json`; only its two diagnostics EF project references were
removed. Identity and all other EF oracle work remains outside this correction. A real SQLite CShell
activation test pre-applies the combined document schema and all five diagnostic-record streams, then
asserts both Groundwork stores resolve with no EF diagnostics store.

On exact implementation head `e70995de5ea6ac14a8f1dacbb558121faf2c42e3`, the diagnostics
persistence suite passed **148/148**, the combined deployment-schema suite passed **6/6**, and the
complete unified-host suite passed **53/53**. The unified-host result contains all five real SQLite
activation cases and all thirteen schema-selector cases, including the four explicit generic
provider registrations. A clean `Elsa.Server` Release build completed with **0 errors** and 29
disclosed pre-existing preview.81 obsolescence warnings in unchanged source. Commands, package
resolution, source hashes, and retained-result hashes are in
[`evidence/us4-composition-results.json`](evidence/us4-composition-results.json). These results
complete T045 and T049. They do not certify the four-provider promotion matrix, performance, or EF
deletion; those remain under T050-T057.

## Host-composition independent review (2026-07-25)

Three adversarial read-only reviewers inspected exact initial range
`6313171ff33f4c42a54507885a080ce185d35526..b0a2a646922bebc95105d37318fdcac10dd068fa`
on correctness/mechanism, evidence integrity, and scope/test preservation:

- Correctness found that the four explicit generic unified-provider overloads registered a session
  factory but did not expose the selected diagnostic deployment source. The remediation centralized
  conditional aliasing in `AddGroundworkReferenceDeploymentSchema<TDeploymentSource>`, routed all
  four providers through it, and added a four-provider singleton-identity regression.
- Evidence integrity found that T045/T049 had narrative totals but no exact-head manifest, and that
  “rejects an EF store” overstated an absence assertion. The remediation added the hash-bound
  [`evidence/us4-composition-results.json`](evidence/us4-composition-results.json) manifest and
  narrowed the claim to “no EF diagnostics store is registered or selected.”
- Scope/test preservation returned clean: Identity, OpenIddict, the frozen EF performance oracles,
  non-diagnostics shell settings, architecture guards, and existing tests remain intact. Only
  `shells.json` changed among the serialized files in this slice.

The correctness reviewer withdrew a provisional fresh-schema concern after re-verifying the
ratified pre-start deployment contract: runtime sessions must admit already-deployed storage and
must not materialize or repair schema. The originating reviewers re-verified the remediations before
merge; no finding was waived.

## Final dependency audit

```bash
rg -n "EntityFrameworkCore|Persistence\.EFCore|DbContext|Migration" src/Elsa/Diagnostics tests/Elsa/Diagnostics
rg -n "Groundwork" src/Elsa/Diagnostics/*/Core
```

The first command may match temporary oracle code until the removal phase; it must have no diagnostics EF implementation/dependency matches at completion. The second command must remain empty throughout.

## Delivery rule

Work on the feature branch, implement by user-story slice with tests first, obtain an independent review, push the organization-owned branch, merge the reviewed PR, and leave the repository with all required checks green.
