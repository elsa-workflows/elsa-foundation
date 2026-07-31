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

The replay targets Groundwork `0.0.1-preview.88`. Runs recorded against preview.73, preview.75,
preview.80, preview.81, or preview.86 are historical provenance only and cannot promote the lane.
The T043 manifest records one clean exact-branch-head preview.81 lifecycle certification; it does not replace the focused and
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

## Preview.86 grouped-trace follow-up (2026-07-25)

Groundwork preview.86 supplies the provider-native grouped diagnostic-record
contract that was missing during the removal-ledger intake. The trace stream now
declares `trace-summary-v1`: trace id is the group key; earliest root/name,
minimum start, maximum end/status, case-policy-aware resource/service/workflow
set unions, and total span count are reduced by the selected provider before
filtering, ordering, take, or continuation. Trace list and detail both consume
that bounded grouped route; the previous latest-fragment path and explicit #130
skip are gone.

Root independently reproduced the final focused evidence:

- complete OpenTelemetry Groundwork adapter suite: **75/75**;
- grouped merge, supplementary-Unicode catalog behavior, pre-retention union
  boundary, and explicit pre-release reset behavior on SQLite, SQL Server,
  PostgreSQL, and MongoDB: **16/16**;
- native grouped-query plan inspection across the same four providers: **4/4**.

The exact implementation head, resolved preview.86 package/tool family, source
hashes, provider images/topology, exact commands, and raw totals are retained
in
[`evidence/preview86-grouped-trace-results.json`](evidence/preview86-grouped-trace-results.json).

The provider case includes concurrent fragment writes, disposal/reopen, merged
filtering, newest-window ordering/take, continuation, detail parity, Unicode
case collision, scope isolation, and retention. This closes the previously
disclosed #130 mechanism gap only. It is not #646 performance evidence, the
T057 promotion matrix, or authorization to delete either EF oracle; T050–T057
remain open.

The grouped trace definition is an intentional pre-release reset boundary. It
uses schema version 2 and a distinct logical storage name. A preview.81
definition is rejected fail-closed on all four providers, and the same four
providers admit the preview.86 definition on fresh storage. No migration or
dual-format shim is supplied because Elsa Foundation is unreleased and the
ratified program rule selects the clean end state.

The trace stream admits at most 5,000 retained records, and every grouped query
reduces exactly the newest 5,000 raw records before applying predicates. Each
record contributes at most 256 values to a resource, service, or workflow union,
so the grouped profile declares the exact finite bound `5,000 × 256 =
1,280,000` values. A configured trace capacity above 5,000 now fails before
provider work. The four-provider boundary cases prove both that 257 distinct
values remain available before trim and that an older matching fragment cannot
re-enter after the 5,000-record input window has been selected.

## Preview.86 grouped-trace independent review

Three adversarial reviewers inspected the exact candidate range from
`f4eaee963da34f650b7e40d0e859ae10b92d4a30`.

- Correctness/mechanism found two blockers: the immutable preview.81 stream
  fingerprint had no explicit transition policy, and the 256-value group union
  rejected individually valid retained fragments. Both findings were accepted.
  The remediation is the explicit schema-v2 fresh-storage boundary plus the
  bounded pre-retention union contract and the four-provider tests described
  above. The originating reviewer then found that the first 1,280,000-value
  remediation covered only the post-retention state because a drain commit
  could append 64 captures before trimming. That confirmed blocker was
  remediated by sharing the 1,000-record admission limit, committing one capture
  per drain unit, including the 499-record retention carry, and querying the
  four real providers before trim. The resulting 1,663,744-value limit remains
  finite and operationally useful; exact-head recertification passed 75/75,
  16/16, and 4/4.
- Evidence integrity found that the preview.86 pass totals existed only as
  narrative while the retained JSON manifests still described preview.81.
  The historical manifests remain immutable; the new hash-bound preview.86
  manifest records the exact implementation head, commands, totals, resolved
  package family, source hashes, and provider topology. Re-review found that
  its SQL Server, PostgreSQL, and MongoDB image tags did not match the shared
  fixture and that the fixture itself was not hashed. The manifest now records
  the exact CU18, PostgreSQL 16, and MongoDB 7.0.37 tags and binds the fixture
  hash.
- Scope/test preservation passed: no EF oracle, migration, project, package,
  solution, or host file was removed or changed; the former #130 skip is the
  only removed skip; T050–T057 remain open.

## Preview.88 grouped-trace window recertification (2026-07-25)

Groundwork preview.88 retains the grouped-reduction contract and adds the
atomic collection-bearing mutation mechanism required by OpenIddict. The
diagnostics integration additionally makes the grouped input window explicit:
the newest 5,000 raw trace records are selected before grouping and before any
caller predicate. The normal 64-capture drain batching is retained; correctness
no longer depends on serializing the drain to one capture at a time.

Root reproduced the exact focused evidence on implementation head
`a305b9811c603e50abe39c2d4e374b46cbfdf4f1`:

- complete OpenTelemetry Groundwork adapter suite: **76/76**;
- grouped trace, Unicode case policy, pre-retention union, newest-input-window,
  and pre-release reset behavior on all four providers: **20/20**;
- native grouped-query plan inspection across all four providers: **4/4**.

The resolved preview.88 package/tool family, exact commands, source hashes,
provider topology, and result totals are retained in
[`evidence/preview88-grouped-trace-results.json`](evidence/preview88-grouped-trace-results.json).
This remains focused correctness evidence only. It is not #646 performance
evidence, T057 final zero-EF certification, or authorization to delete either
EF oracle; T050–T057 remain open.

### Preview.88 checkpoint review remediation

The first exact-range adversarial review froze candidate `811900df7` and ran
three read-only axes in parallel. Evidence integrity passed after independently
recomputing the preview.88 attachment, all 36 current artifact hashes, all 108
historical preview.80/preview.81/preview.86 artifact hashes, exact provenance,
resolved package assets, and source-manifest hashes. Scope/test preservation
passed with no EF, host, solution, shell, Studio, BPMN, or #420 deletion; it
found one P3 wording omission, now corrected so the importer instructions name
preview.86 among the guarded historical generations.

Correctness/mechanism found three blockers in trace-capacity edge handling:

1. resource queries were incorrectly short-circuited by zero trace capacity
   rather than zero resource capacity;
2. trace list queries passed an invalid zero grouped-input limit instead of
   returning the empty retained stream; and
3. a configured query size above 5,000 could exceed the grouped profile's
   maximum take.

Commit `a305b9811c603e50abe39c2d4e374b46cbfdf4f1` remediates all three by keeping
catalog and signal capacities independent, returning empty before provider work
for zero trace capacity, and capping grouped trace take to the admitted trace
capacity. The strengthened zero-capacity and oversized-take regressions passed
2/2; exact-head Release recertification then passed 76/76 adapter cases, 20/20
four-provider behavior cases, and 8/8 newest-window plus native-plan cases.

Final re-verification of exact range
`b55aabc54a845e1d0366beb28bf3d9f91cddd613..b4cbf14723c8d6081349f83a2c83237e3bfafdfe`
passed all three axes:

- correctness/mechanism: **PASS**; the originating reviewer traced all three
  fixes, reran the strengthened regressions (2/2) and four-provider newest-window
  cases (4/4), and found no remaining blocker;
- evidence integrity: **PASS**; every preview.88 and historical artifact hash,
  ledger tuple/status, package asset, manifest hash, and exact-head result claim
  recomputed, with independent 76/76 adapter and 20/20 four-provider reruns; and
- scope/test preservation: **PASS**; no EF oracle, host, solution, shell,
  incident-strategy, Studio, BPMN, #420, or in-memory boundary changed, no test
  was weakened, and T015 plus T050–T057 remain open.

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

## Diagnostic-stream startup auto-apply follow-up (2026-07-28)

Groundwork PR [#149](https://github.com/valence-works/groundwork/pull/149) added the public,
provider-neutral `IDiagnosticRecordDeploymentApplier` and published it in `0.0.1-preview.94`. The
four provider packages expose matching appliers, and Groundwork.Tool uses the same boundary.
Appliers preflight every declared stream, reject definition drift before mutation, validate
provider topology, materialize only missing streams, and require a ready postcondition.

Elsa's four unified provider leaves now register that applier plus a shared diagnostic deployment
initializer. The initializer runs in `LifecyclePhase.Prepare` at order `1`, after document-schema
admission at order `0` and before diagnostics startup in the Default phase. It is active only when
`AutoApplySchemaOnStartup` is enabled and the selected deployment source declares diagnostic
streams. Runtime session factories remain read-only.

Focused verification passed:

- complete unified-host suite: **56/56**, including fresh SQLite creation, disabled-mode
  `GW-DIAG-DEPLOY-001`, restart idempotency, drift refusal with `GW-DIAG-DEPLOY-002`, and all four
  provider registrations;
- shared diagnostics persistence: **168/168** (162 lifecycle/non-CLI plus 6 real CLI schema cases);
- diagnostics Groundwork composition: **6/6**;
- Structured Logs Groundwork adapter: **18/18**;
- OpenTelemetry Groundwork adapter: **76/76**; and
- `Elsa.Server` build: **0 errors** (existing `GW000*` migration warnings remain tracked outside
  this correction).

## Host-composition independent review (2026-07-25)

The correctness/mechanism and evidence-integrity reviewers first inspected exact pre-remediation
range `6313171ff33f4c42a54507885a080ce185d35526..b0a2a646922bebc95105d37318fdcac10dd068fa`.
An independent scope/test-preservation reviewer then joined the exact final-candidate audit:

- Correctness found that the four explicit generic unified-provider overloads registered a session
  factory but did not expose the selected diagnostic deployment source. The remediation centralized
  conditional aliasing in `AddGroundworkReferenceDeploymentSchema<TDeploymentSource>`, routed all
  four providers through it, and added a four-provider singleton-identity regression.
- Evidence integrity found that T045/T049 had narrative totals but no exact-head manifest, and that
  “rejects an EF store” overstated an absence assertion. The remediation added the hash-bound
  [`evidence/us4-composition-results.json`](evidence/us4-composition-results.json) manifest and
  narrowed the claim to “no EF diagnostics store is registered or selected.”
- Scope/test preservation found that the shrink-only
  `tests/Elsa/Architecture/Baselines/ef-core-surface.json` still required the two removed
  diagnostics EF host references and shell keys. The prescribed full-solution restore and guarded
  baseline generator removed the 18 downstream entries made stale by that narrow host switch:
  2 direct project references, 12 transitive project-consumer paths, 2 transitive package-consumer
  paths, and 2 host-configuration entries. It preserved every EF project/package entry and every
  Identity/OpenIddict entry. The generated baseline passed its exact-match guard 1/1.

The first full remote check independently reproduced that ratchet failure and exposed a second
architecture blocker: six new diagnostics registration descriptors were valid immutable
application-lifetime resources but were not present in the exact documented-exception inventory.
The two deployment-manifest contracts alias immutable application-wide schema values. Each of the
four provider session factories retains only immutable provider configuration and returns a fresh
scope-bound, deterministically disposable session on every invocation. The lifetime guard now
records those six exact paths, service types, and rationales rather than weakening its suffix scan
or changing the application lifetime behind the singleton diagnostics stores. The focused lifetime
suite passed 4/4 and the complete architecture suite passed 286/286 after remediation.

The correctness reviewer withdrew a provisional fresh-schema concern after re-verifying the
ratified pre-start deployment contract: runtime sessions must admit already-deployed storage and
must not materialize or repair schema. After both architecture remediations, three independent
adversarial reviewers inspected the exact final candidate on correctness/mechanism, evidence
integrity, and scope/test preservation. All three returned clean; no finding was waived. Identity,
OpenIddict, the frozen EF performance oracles, non-diagnostics shell settings, and existing tests
remain intact.

## T056 diagnostics documentation checkpoint and independent review (2026-07-29)

Task T056 updates the Structured Logs and OpenTelemetry domain READMEs and extension-point catalogs
to the current composition truth: in-memory stores remain the defaults without persistence,
`DiagnosticsGroundworkPersistence` is the first-party durable feature selected by the reference
`Elsa.Server`, and the diagnostics EF implementations remain temporary #646 comparison/oracle and
compatibility paths until #647 deletes them. This checkpoint does not claim current four-provider
promotion, a #646 performance verdict, EF removal, package cleanup, or completion of T047,
T050-T055, or T057.

Three adversarial read-only reviewers inspected frozen candidate range
`5be8d0c72d703b6b7f9de0331804705f65086609..e1a879eddfed16750c5ca6b6a28ec84665d6cfe6`:

- Correctness/mechanism found that the Structured Logs README attributed wake-hint publication to
  `GroundworkStructuredLogStore`; production code assigns that responsibility to
  `StructuredLogSink` after the store returns a committed append.
- Evidence integrity found that the existing four-case `DiagnosticsDocumentationTests` suite does
  not read any of the four T056 domain documents. Its 4/4 pass is retained only as an auxiliary
  regression result and is not cited as T056 verification. T056 is supported by direct comparison
  with the production feature, store, shell-composition, and retained-EF sources plus the exact-range
  reviews.
- Scope/test preservation found that the initial rewrite removed the only operator-facing details
  for enabling and safely running the retained OpenTelemetry EF oracle. Removing those details
  before #646 finishes would have weakened the live compatibility path.

Remediation commit `7168dc28a5992feb587236c512061f8f1d6deeaf` assigns wake publication to
the sink and restores concise EF enablement, feedback-loop protection, factory lifetime,
migration/drain startup, and shutdown-flush guidance while explicitly marking EF temporary and
non-reference-host. The originating correctness and scope reviewers re-inspected the remediation
and returned **PASS**. `git diff --check` is clean; the auxiliary documentation suite passed 4/4
after a targeted restore, with existing Groundwork migration warnings and no container or provider
execution.

## Preview.102 provider remediation checkpoint (2026-07-31)

The preview.102 import changed cursor paging evidence in two truthful ways.
Provider-native page plans request one lookahead row (`Take + 1`) so the runtime
can emit a continuation while returning at most `Take`, and cursor-only
retention routes reject the former offset-capacity probe. The current
`DiagnosticsBoundedExecutionTests` therefore require an exact native `Page`
command, its one-row lookahead, and provider-applied order. Capacity evidence
inspects the production first cursor page at the real 1,000-row retention
bound.

The same recertification exposed one nondeterministic PostgreSQL catalog race.
Groundwork's unconditional upsert may report `ConcurrencyConflict` when a
concurrent equivalent writer wins. The adapter now reads back that winner and
accepts it only when document kind, schema version, and canonical content are
byte-exact. A divergent winner is still translated as unavailable and is not
retried or overwritten.

Root verification on branch base Elsa `d16590059a897e83c7098ab20c4909c97602b9c4`
and Groundwork package family `0.0.1-preview.102` was rerun on the clean exact
implementation head `6e1b4e1a43740c36cbce724d3d5d5d2b9b37fdc0`. The retained
[preview.102 evidence manifest](evidence/preview102-provider-remediation-results.json)
binds the implementation tree, configuration and source hashes, resolved
package/tool family, provider topology, exact commands, totals, skips, and
temporary TRX digests:

```bash
dotnet test tests/Elsa/Diagnostics/OpenTelemetry/Persistence/Groundwork/Tests/Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork.Tests.csproj \
  --no-restore \
  --filter 'FullyQualifiedName~Equivalent_catalog_winner_reconciles_an_ambiguous_concurrency_conflict_without_rewriting|FullyQualifiedName~Divergent_catalog_winner_remains_a_conflict_and_is_not_overwritten' \
  --logger 'console;verbosity=minimal'
# 2 passed

dotnet test tests/Elsa/Diagnostics/OpenTelemetry/Persistence/Groundwork/Tests/Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork.Tests.csproj \
  --no-build \
  --logger 'console;verbosity=minimal'
# 78 passed

dotnet test tests/Elsa/Diagnostics/Persistence/Tests/Elsa.Diagnostics.Persistence.Tests.csproj \
  --no-restore \
  --filter 'FullyQualifiedName~DiagnosticsGroundworkProviderConformanceTests|FullyQualifiedName~DiagnosticsScopeAndRetentionConformanceTests|FullyQualifiedName~DiagnosticsBoundedExecutionTests|FullyQualifiedName~DiagnosticsProviderLifecycleSmokeTests' \
  --logger 'console;verbosity=minimal'
# 54 passed
```

The exact real-provider concurrent-writer theory passed 4/4 across SQLite, SQL
Server, PostgreSQL, and MongoDB. The complete 54-case rerun then passed with
zero failures or skips.

The initial exact-range review blocked this checkpoint twice. Correctness found
that the provider-order assertion required only a nonempty sequence, so a
reversed or incomplete cursor order could pass. Implementation head `6e1b4e1a`
now compares every provider-applied field identifier, direction, and
identity-tie-break marker to the admitted plan. Evidence integrity found that
the preview.102 counts and four-provider claim existed only in prose; the
hash-bound manifest above now retains their exact-source receipt. Both
originating reviewers must re-verify the final evidence commit before this
draft can be promoted.

This checkpoint repairs current-family correctness and native-plan evidence
only. It does not import a #646 performance verdict, delete either diagnostics
EF oracle, complete T047/T050-T055/T057, promote a final evidence generation,
or authorize #642 closure.

## Final dependency audit

```bash
rg -n "EntityFrameworkCore|Persistence\.EFCore|DbContext|Migration" src/Elsa/Diagnostics tests/Elsa/Diagnostics
rg -n "Groundwork" src/Elsa/Diagnostics/*/Core
```

The first command may match temporary oracle code until the removal phase; it must have no diagnostics EF implementation/dependency matches at completion. The second command must remain empty throughout.

## Delivery rule

Work on the feature branch, implement by user-story slice with tests first, obtain an independent review, push the organization-owned branch, merge the reviewed PR, and leave the repository with all required checks green.
