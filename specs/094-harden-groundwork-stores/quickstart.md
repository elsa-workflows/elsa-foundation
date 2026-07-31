# Quickstart: Validate Groundwork Store Hardening

This guide is the implementation/review path for feature 094. A narrow green unit test is not a readiness verdict; use the ordered gates below.

## Prerequisites

- .NET 10 SDK selected by the repository.
- Access to the package feed containing the pinned Groundwork `0.0.1-preview.103` release.
- Docker-compatible container runtime for SQL Server, PostgreSQL, and MongoDB.
- Enough local resources to run MongoDB as a replica set for transaction scenarios.
- `Groundwork.Tool` restored from the repository-local tool manifest at `0.0.1-preview.103`, matching all
  Groundwork packages.

Groundwork PR #88 is the generic version-aware codec boundary in this release; PR #101 admits sort-only index
fields as bounded residual predicates; and PR #108 adds bounded linked hydration. Elsa-specific payload policies,
legacy-stamp parsing, JSON options, and concrete upcasters must remain marker-gated in Elsa provider packages; core modules must not
reference Groundwork.

Do not use a standalone MongoDB instance for scenarios that claim multi-document atomicity.

The repository's current Groundwork family is `0.0.1-preview.103`; do not combine it with a different
`Groundwork.Tool` or provider package version. PR #88 provides the generic version-aware codec consumed by this
family. Elsa owns only its per-kind policies, legacy-stamp parsing, JSON options, and concrete upcasters behind
the Elsa provider marker.

The original checkpoint/fence attachment and its unversioned evidence paths retain the reviewed
`0.0.1-preview.80` four-provider slice as immutable historical provenance. The later
`0.0.1-preview.81` slice lives under `versions/0.0.1-preview.81/`; the coverage ledger retains that
versioned attachment by tuple as prior-generation provenance. The `0.0.1-preview.86` checkpoint/fence slice
lives under `versions/0.0.1-preview.86/` as immutable prior-generation provenance. The latest retained
`0.0.1-preview.88` slice lives under `versions/0.0.1-preview.88/` and was imported mechanically by tuple. The
`preview.102` generation was never published. The current `preview.103` generation remains intentionally absent until the exact clean-source four-provider publisher
and mechanical importer complete. All rows remain below
`evidence-complete`: this narrow 36-record slice cannot close the full provider-evidence gate.

**2026-07-31 preview.103 integration preparation**: Groundwork merge
`b9ba0249eed0a00da9b6d37575f39383c22ae2c9` published the coherent
`0.0.1-preview.103` package/tool family through
[Groundwork PR #157](https://github.com/valence-works/Groundwork/pull/157). The patch repairs MongoDB
fixed-assignment and transition selector-mirror persistence across shared documents, dedicated document
tables, and physical entity tables, including reopen durability. This serialized Elsa checkpoint aligns
the repository package/tool family and current-version guards only. It imports no provider evidence,
advances no coverage-ledger status, records no performance verdict, and completes no Spec 094 task.

The upstream `Publish NuGet Packages` run
[`30618297992`](https://github.com/valence-works/Groundwork/actions/runs/30618297992) passed for the
exact merge SHA and its feed log names the `preview.103` packages. Root restored the complete
`Elsa.Server.slnx` graph with `--force-evaluate`, restored `Groundwork.Tool 0.0.1-preview.103`, and
verified the following container-free gates serially:

- current ledger/importer architecture guards: **88/88**;
- focused current-version conformance: **7 passed / 1 publication-only skip / 0 failed**;
- benchmark protocol and comparison-integrity guards: **266/266**;
- SQLite design target/package/tool version gate: **1/1**.

An initial parallel test attempt shared transitive build outputs and hit `GenerateDepsFile` file locks;
it supplied no accepted result. The serial reruns above supersede it. No database-server container,
provider-evidence publisher, mechanical importer, or benchmark timing process ran during this
alignment.

After freezing candidate `5f56c34cac44c65cffec81f9959c34eb07db7e30`, root also passed the
complete architecture suite **351/351** and the OpenIddict Groundwork unit/manifest suite **36/36**
against the same implementation and package graph.

Three read-only reviewers were briefed adversarially to assume green-washing and inspected exact
range
`77d6109f6a4dac1f6b3994635e17e5a2342ab045..5f56c34cac44c65cffec81f9959c34eb07db7e30`:

| Axis | Verdict and disposition |
|---|---|
| Correctness/mechanism | **PASS** — all seven packages, the tool, current-version guards, and generated package/dependency maps coherently resolve `preview.103`; no production API/schema behavior changed and remaining `preview.102` references are historical. |
| Evidence integrity | **PASS** — upstream PR #157, merge SHA, and publisher run provenance are valid; the ledger contains zero `preview.103` records, immutable historical attachments are unchanged, and no status/verdict claim advances. |
| Scope/test preservation | **PASS** — ledger content other than `groundworkVersion` is unchanged, status counts remain 30 implemented / 4 externally blocked / 1 planned, performance-verdict count remains zero, and no task, EF ratchet, historical evidence, issue, or Project state changed. |

Requested Luna reviewer capacity was unavailable; all three reviews used the documented GPT-5.6
Terra High fallback. No finding required source remediation. The final record/map commits were
separately re-verified as documentation-only before merge.

**2026-07-31 preview.102 integration preparation**: the seven Groundwork packages and
`Groundwork.Tool` align to the public `0.0.1-preview.102` release built from Groundwork merge
`68e7c344163c199024aed00ccdcaa2deb51ef5bb`. This exact family includes the provider-applied
ordering-tail work from PR #154, the provider-internal SQLite materialization proof from PR #155,
and the immutable physical-form baseline registry/control plane from PR #156. The latter two are
reviewed checkpoints inside Groundwork #141 and #50, not completion of either issue: ordinary
fence/prune and non-SQLite relationship evidence remain for #141, while #50 still requires the
controlled four-provider synchronized-contention matrix, reviewed population and activation of the
registry, and final physical-form reports. This preparation imports no provider evidence, advances
no coverage-ledger status, records no performance verdict, and completes no Spec 094 task.

Pre-freeze verification restored the exact package/tool family and passed:

- the complete Release solution build (zero errors; existing warnings retained);
- the complete architecture suite (348/348);
- benchmark protocol/integrity tests (266/266);
- Activities Design Groundwork (72/72), SQLite design conformance (57/57), Identity persistence
  Groundwork (74/74), Groundwork querying (108/108), and ASP.NET Core Identity Groundwork
  (133/133);
- the focused runtime/IAM-secrets/Identity evidence contract slice (9 passed, four publication-only
  tests explicitly skipped without opt-in); and
- offline resolution of the reference composition through
  `GroundworkAllFeaturesWithIdentityDeploymentSchema` for SQLite, PostgreSQL, SQL Server, and MongoDB,
  each with status `ready` and zero diagnostics. This preparation check did not include the diagnostics
  manifest sources.

The first preview.102 Identity resolution exposed twelve `GW-PHYSICAL-025` diagnostics: every
non-unique scale-bearing offset route lacked the provider identity ordering tail. Offset's
`id_comparison_key` would make the widest SQL Server key 2,406 bytes, beyond the 1,700-byte limit.
The remediated shape uses cursor paging and the fixed-width envelope `id_lookup_key` tail (1,088
bytes at the widest route), migrates exhaustive callers to the bounded continuation pager, preserves
the 100,000-document materialization ceiling, and adds direct resolver, ordered-index, pager-limit,
and source-policy regression tests. No historical Identity evidence was rewritten; preview.102 still
requires a fresh exact-source four-provider generation.

Hosted CI against the initial frozen candidate
`a462a17ac9546bb5b85894591554f06ccf8ca27d` then exposed the diagnostics omission above. Preview.102
rejected eight OpenTelemetry catalog routes because their non-unique scale-bearing physical indexes
lacked provider-applied identity ordering tails. Adding offset tails would have exceeded SQL Server's
1,700-byte key limit on the widest catalog indexes. The replacement makes those scale-bearing routes
cursor-paged, appends the fixed-width `id_lookup_key` tail with the exact declared sort direction, and
changes catalog-capacity cleanup from offset skipping to bounded continuation traversal. A direct
regression proves cleanup sends no `Skip` and advances through a non-null continuation. The same CI run
also exposed three stale test expectations around preview.102 offset tails in Activities Design
temporal management, runtime pinned-artifact history, and Secrets. The first remediation updated
those assertions to the new `id_comparison_key` shape; the later exact-range correctness review below
proved that resolver-green shape was not SQL Server-deployable and superseded it with bounded unique
tuples plus a provider-native validator.

Root verification of that remediation passed the complete OpenTelemetry Groundwork suite (76/76), the
Unified Host schema-activation class (8/8), the three formerly stale focused tests (1/1 each), and a
Release build of `Elsa.Server.csproj` with zero errors. Offline validation of the complete
`GroundworkAllFeaturesWithIdentityAndDiagnosticsDeploymentSchema` returned `ready` with zero diagnostics
for all four mandatory providers:

| Provider | Physical target fingerprint | Diagnostic-record fingerprint |
|---|---|---|
| SQLite | `c63f59ee2587a499513ec75127d96f097ec9527d8673e4e89b0a59c395fb2c1f` | `ce5aaec3513730e4e7135c918e6bde3ead7abef2b0d8e74570474d6778f3900c` |
| PostgreSQL | `277ed6f01c20a6dd8abd51f6a549bdaf8802992e9e687b36369058815c2125f3` | `0871bade20996939a899577ec7d867c38c2196d4910929e4c75e8828d005d539` |
| SQL Server | `73b740c4eb3589c78af82779e3e2defa578de9a22617122407b1b857369fa28d` | `fafcd1ffb25323089aaa0b6ea538dc0c9f1a3481549eee57da3a0dd85bbbc7ec` |
| MongoDB | `a1ec0c1a99b313967bbd5d01b981222ed49f6f8d319ae0175b80c12c7a1dada7` | `d08a1e3696cd3d5c66331ad8bd5258990bf2ceb770c534f8c23d5ffc86180508` |

The first adversarial exact-range review cycle inspected
`ca818b649d85c5167e2222c0ec534e215153d473..a462a17ac9546bb5b85894591554f06ccf8ca27d`
on correctness/mechanism, evidence integrity, and scope/test preservation. Its confirmed findings and
dispositions were:

| Axis | Confirmed finding and disposition | Status |
|---|---|---|
| Correctness/mechanism | The bounded exhaustive pager could loop when a provider returned an empty page with a fresh continuation. It now rejects that impossible shape, with a two-token regression proving the guard. The originating reviewer re-verified the fix. | PASS |
| Evidence integrity | The preview evidence importer admitted a dirty source checkout. It now rejects tracked, staged, and untracked dirt, retaining only the exact validated destination-generation recovery exception. The originating reviewer re-verified the focused importer/guard suite. | PASS |
| Scope/test preservation | The source scanner could miss a forbidden direct query call when an allowed pager call appeared on the same line. It now removes only the qualified allowed invocation before scanning the remainder; a mixed-line regression proves the direct call is still reported. The generated dependency maps were refreshed from the clean remediated source and the originating reviewer re-verified both dispositions. | PASS |

The next exact-range review inspected
`ca818b649d85c5167e2222c0ec534e215153d473..8566416c2ef5b94774df39462189de4f8003aecc`.
Evidence-integrity and scope/test-preservation reviewers returned PASS, but correctness/mechanism
blocked the candidate: offline resolution did not execute SQL Server's provider-native 1,700-byte
index-key validator, and 31 non-point offset indexes outside OpenTelemetry still carried the
1,350-byte `id_comparison_key` tail. The widest examples reached 2,374 bytes.

The remediation adds a permanent no-I/O construction test over the complete
`GroundworkAllFeaturesWithIdentityAndDiagnosticsDeploymentSchema`; constructing
`SqlServerPhysicalDocumentStore` invokes the real provider validator for every compiled route.
The initially failing `activity-definition-by-category` index reported 2,374 bytes. The corrected
manifests remove the redundant provider tail only where a bounded tuple has a storage-enforced
identity:

- Activities and Workflow Design list indexes include the projected entity ID used as the document
  ID and are now unique composites;
- reusable Activities routes retain their business-key order and add the projected entity ID as
  their final unique component;
- temporal management routes use `(sort key, valid-from)` or
  `(valid-to, resource ID, valid-from)`, preserving public offset semantics while distinguishing
  retained revisions;
- Secrets reuses the already-enforced `(tenant, normalized name)` uniqueness with status added; and
- the pinned-artifact route includes the workflow execution ID used as the runtime document ID.

The immutable preview.95 fixtures then caught a proposed runtime shortcut before freeze. Marking the
existing collection, artifact-ID, and execution-ID projections required would have changed their
historical nullable definitions and produced `GW-SCHEMA-003` on every provider. The final form keeps
those projections and the legacy non-unique index byte-for-byte, adds a unique `-v2` index, and adds
explicit `IS NOT NULL` residual predicates for the artifact and execution IDs. That combination is
additive, preserves the public offset/latest-per-key contract, and lets SQL Server legally use the
provider-generated filtered index hint. The exact historical planner passes 4/4 and the isolated SQL
Server B7 native route passes 1/1 on this form.

OpenIddict has one deliberately different disposition. Its earlier authorization compound index
would require a 2,048-byte SQL Server key, above the 1,700-byte limit. The immutable preview.95
fixtures contain no OpenIddict state, and Spec 106 defines this unreleased adapter as greenfield with
no data-migration obligation, so the impossible unmaterialized authorization declaration is removed
instead of preserved. The current subject route uses the bounded
`subject + id_lookup_key` cursor form; the safe token legacy compound index remains alongside its v2
route. Any manually materialized old authorization schema is not auto-upgradeable and is outside this
greenfield contract. OpenIddict manifest tests pass 36/36 and its complete four-provider capability
probe passes 16/16.

The provider-native full-reference validator now passes. Root verification also passed Activities
Design Groundwork **74/74**, temporal projections **12/12**, Workflow Design Groundwork **97/97**,
Secrets **88/88**, runtime Groundwork **739/739**, and Unified Host **70/70**. The SQLite cold-start
schema-operation baseline is **963**, and the continuation-cycle fixture now returns non-empty pages
so it exercises the repeated-token guard rather than the earlier empty-page guard. The complete
conformance project passes **239 passed / 18 intentionally skipped / 0 failed (257 total)**, including
both enabled SQLite acceptance-scale native-plan suites. The design baseline retains the accepted
preview.81 fingerprints and pins preview.102's still-unaccepted drift as target
`8e475dc3097262805b2913ba9ecab8f4447129c1d5525b0e81aea3aff2b04b97` and plan
`81b67c2ff3588bea311e2098286e7ee93b3be6c94c502560f2953837b96e9535`; its exact focused test
passes 1/1 without enabling evidence output. The full architecture suite passes **351/351** and the
Release reference-server build succeeds with **0 errors** (24 pre-existing legacy/obsolete API
warnings).

### Preview.102 package/schema-alignment review disposition

Three adversarial read-only reviewers inspected the exact initial frozen range
`ca818b649d85c5167e2222c0ec534e215153d473..af54125de82af0ea64d9fc1a271d43a688344cb0`
on correctness/mechanism, evidence integrity, and scope/test preservation. The reviewers were
briefed to assume the implementation had green-washed its evidence. Confirmed findings and final
dispositions are:

| Axis | Confirmed finding and disposition | Status |
|---|---|---|
| Correctness/mechanism | The historical planner checked only the expected `-v2` creates. It now derives every preview.95 `CreatePhysicalIndex` identity from each immutable applied state, requires the current provider target to retain the complete set, and allow-lists only the known additive/validation operation kinds so a future destructive kind fails closed. The focused planner passes **4/4**; the originating reviewer re-inspected the remediation and returned PASS. | PASS |
| Evidence integrity | Fixture hashes and prose did not independently prove that the payloads came from the claimed Elsa/package source. The new replay creates a detached `ca818b649…` worktree, verifies tree `82433ec…`, all seven preview.95 packages plus `Groundwork.Tool`, every package's Groundwork commit `d297147…`, regenerates the four canonical applied states through an isolated production-composition reproducer, and compares their decompressed SHA-256 values. Root ran the complete replay successfully. | PASS |
| Evidence integrity | The first replay trusted mutable caller fixture/harness files. It now rejects tracked, staged, or untracked candidate-worktree dirt before reading or copying those bytes. Root proved the dirty rejection, committed the guard, reran the complete clean replay on `d5b3b8b84f8769d4cd31699c4839246d1dc601dd`, and the originating reviewer returned PASS. | PASS |
| Evidence integrity | The cold-start report retained one stale 922-operation sentence while the executable baseline and current report outcome were 963. The sentence now says 963; the reviewer found no remaining contradiction. | PASS |
| Scope/test preservation | The final source changes made the generated map manifest stale. Root ran the authorized full map generator; `5bf66b775fa169f082fffbc9f7314ecbb47376f3` refreshes the input fingerprint and the truthful 1,159→1,160 direct-reference delta from the UnifiedHost ProcessProbe edge. The originating reviewer re-inspected the generated files and returned PASS. | PASS |
| Scope/test preservation | No test was removed or skipped, OpenIddict remains explicitly greenfield/excluded from the historical fixture, the coverage ledger changes only its package-family label, and no row, performance verdict, provider evidence, or Spec 094 task advances. The affected architecture guards passed **95/95**. | PASS |

Correctness and evidence reviewers confirmed their final PASS on
`ca818b649d85c5167e2222c0ec534e215153d473..d5b3b8b84f8769d4cd31699c4839246d1dc601dd`.
The scope/test-preservation reviewer confirmed final PASS on
`ca818b649d85c5167e2222c0ec534e215153d473..5bf66b775fa169f082fffbc9f7314ecbb47376f3`.
The later quickstart-only commit records these already-completed dispositions and changes no
implementation, fixture, package, generated map, ledger, task, or test.

Hosted Linux CI on the reviewed `f304e4b1d1beb97a01af01744115b0df846de11f` head then found a
cross-runtime evidence defect that the macOS verification could not expose. The immutable
preview.95 applied states carry Groundwork's exact runtime Unicode identity algorithm. Darwin
produces
`groundwork-unicode-ordinal-ignore-case-v1-3206f759667cb9cc764ec243dfb3d322a39970184efab619e80163c36d86818f`;
Ubuntu Noble produces
`groundwork-unicode-ordinal-ignore-case-v1-124ca0d0d2b045d7be0e6aea8f07f74fbc0428a13c53a47d8a7d41db71b5ec5f`.
The original fixture family contained only the Darwin identity, so the production planner correctly
rejected it on Linux rather than silently accepting a comparison policy from another runtime.

The remediation preserves that fail-closed boundary. The Darwin fixtures remain immutable at their
original paths, and a second immutable Noble family lives under
`Fixtures/schema-evolution/preview.95/unicode-identity-124ca0d0d2b0/`. The test selects a family only
for one of the two exact reviewed algorithm identities and throws for every unknown identity; it
does not normalize, substitute, or weaken Groundwork's runtime fingerprint. The clean replay command
uses the host runtime when invoked without arguments and accepts `--runtime linux-noble` for the
container replay; Noble execution uses the digest-pinned
`mcr.microsoft.com/dotnet/sdk:10.0-noble@sha256:ed034a8bf0b24ded0cbbac07e17825d8e9ebfe21e308191d0f7421eaf5ad4664`
image and the same exact Elsa/package/tool/upstream-source tuple as the Darwin replay. Both modes
restore packages and tools into fresh replay-local caches with cache reuse disabled. The pinned
Noble mode additionally requires the generated identity to equal the exact Noble algorithm before
selecting any fixture family.

At implementation head `cba61a1e00537082198c9c3e8132e645737b6183`, root reproduced the complete
Noble replay and exact canonical hashes for SQLite `80e1fde0…`, SQL Server `a38f2233…`,
PostgreSQL `35762019…`, and MongoDB `a232f105…`. An independent verifier archived that exact clean
commit, confirmed the pinned image digest and Noble algorithm/family selection, and passed the
focused historical planner **4/4** in the disposable Linux checkout. Root independently passed the
same planner **4/4** on Darwin and the real four-provider historical physical-upgrade test **4/4**
in 3m56s. The SDK emitted its existing workload-verification advisory during the container restore;
the focused Linux test itself emitted no warning. These results supersede the Linux-hosted failure
without converting it into a provider-evidence or performance claim.

The replacement three-axis review inspected exact range
`ca818b649d85c5167e2222c0ec534e215153d473..1f2f6ea4fd10761a242198bd71f4844caa683d09`.
All earlier verdicts are historical and superseded for this cross-runtime remediation.

| Axis | Confirmed finding and disposition | Status |
|---|---|---|
| Correctness/mechanism | The first Noble mode would have accepted either known algorithm and could have mislabeled an unexpected Darwin result as Noble. It now requires the exact Noble algorithm before fixture selection. The first cache-bypass remediation also used unsupported `--no-cache`; both restore surfaces now use their verified `--no-http-cache` option. The originating reviewer passed script syntax, strict invalid-argument cases, CLI option checks, and the focused historical planner **4/4** on the replacement head. | PASS |
| Evidence integrity | The first host replay inherited global NuGet/tool caches, so matching nuspec metadata did not independently exclude substituted cached binaries. Host and Noble now use fresh replay-local package/CLI homes with HTTP-cache reuse disabled. The reviewer recomputed all eight compressed hashes, inspected the algorithm identities in every payload, verified source/package/tool/upstream tuples and the Noble image digest, scanned for sensitive material, and found the corrected invocation, provenance, counts, and nonclaims truthful. | PASS |
| Scope/test preservation | The new Noble family is additive and leaves all Darwin fixtures immutable. No test was removed or skipped; the ledger changes only its package-family label, and no task, provider evidence, performance verdict, or status advances. Focused architecture guards passed **95/95**. The final generated map manifest is fresh and follows the last map input. | PASS |

Root then reran both complete clean replays at `1f2f6ea4`: host/Darwin and digest-pinned Noble each
restored into isolated caches and reproduced all four canonical fixture payloads. The lane and main
worktrees were clean, and remote `main` remained the reviewed base `ca818b649…`.

Hosted checks on the later record-sealed head
`9ff56e9eb79178f9765a71632bafe8596e6034e7` proved the cross-runtime fixture remediation:
Unified Host passed **70/70** on Linux. The same Build & test job then exposed two independent
integration defects. First, the EF surface ratchet scans every repository project and correctly
refused an incomplete restore because `tools/evidence/Preview95SchemaFixtureReproducer.csproj` was
not a member of the restored `Elsa.Server.slnx`; the solution now restores that evidence tool rather
than weakening or excluding it from the scanner. Second, the SQLite Identity reopen test still
adapted the legacy `IDocumentStore` API and attempted to resolve a new physical `-v2` route through
that legacy surface. The reopen and concurrency tests now initialize the real Identity physical
schema, open physical provider clients, and require the provider's `BoundedDocumentStore`. Their
duplicated legacy query adapters were deleted. The link/delete race also no longer treats every
`InvalidOperationException` as a valid loser: it accepts only the documented missing-user,
`NotFound`, or `ConcurrencyConflict` outcomes.

Root verification of the replacement passed the EF surface ratchet **1/1**, the SQLite reopen test
**1/1**, the 100-iteration SQLite Identity race **1/1**, the complete ASP.NET Core Identity
Groundwork suite **133/133**, and the complete architecture suite **351/351**. The evidence tool is
present in `dotnet sln Elsa.Server.slnx list`, restores through that solution, and builds in Release
with **0 errors**; its ten warnings are pre-existing obsolete-API warnings in referenced production
source projects, not warnings introduced by the tool or this integration fix. Exact changed-file
format checks and `git diff --check` pass. The replay harness and immutable fixtures did not change,
so the already-recorded clean Darwin/Noble fixture replays remain the applicable evidence.

Three adversarial read-only reviewers inspected exact replacement range
`ca818b649d85c5167e2222c0ec534e215153d473..025206262d426849995a72d63e571d68225490d9`.
They were briefed to assume the hosted-CI remediation and its evidence had been green-washed.

| Axis | Confirmed finding and disposition | Status |
|---|---|---|
| Correctness/mechanism | Solution restore now covers the evidence tool without excluding it from the all-project EF scanner. Both SQLite tests physicalize the production Identity manifest, open distinct physical clients, require the provider-created bounded store, and leave unexpected transaction/runtime exceptions uncaught. The reviewer found no blocker, P1, or P2. | PASS |
| Evidence integrity | The hosted failure diagnoses, solution membership, production physical-route use, deleted adapters, narrowed exception outcomes, test counts, and replay/nonclaim boundaries match the committed implementation and retained artifacts. The first frozen head left its generated-map manifest stale after source/test/spec inputs changed; the authorized full generator now records input head `5a42fb06c`, fingerprint `0a423d0f…`, and unchanged counts. The originating reviewer re-inspected `025206262` and returned PASS. | PASS |
| Scope/test preservation | The protected solution edit is limited to the restore-required evidence tool. Only duplicated legacy test adapters were deleted; no test method or skip, EF oracle, package, shell, fixture, replay, coverage-ledger row, verdict, status, task, or provider-evidence surface changed. The same stale-map finding was remediated at `025206262`; the originating reviewer confirmed both worktrees clean and returned PASS. | PASS |

These are integration-preparation facts only. They import no preview.102 provider evidence, advance no
coverage-ledger row, issue no performance verdict, and complete no Spec 094 task.

**2026-07-25 preview.88 source alignment**: the seven Groundwork packages and `Groundwork.Tool` consume the
public `0.0.1-preview.88` release built from Groundwork merge
`6e79f7836ac7bb13c0153771531162886ca49971`. Publication ran from exact Elsa source
`b0545e166fd45aa872f265c88782a7034a09c357` (tree
`613afd96195b4ef28546a67f099d259e5ffbe448`) and passed the four-provider publisher gate
(1/1, 2m33s). The importer retained 36 records with attachment SHA-256
`f0b40406e1e5a044bb8e83e6090c3eb84b676124674cd948ed2440f227b065f2`.
Root verification passed the full architecture suite (313), focused publisher tests (9 passed,
four explicit publication tests skipped), benchmark protocol tests (60), and the OpenTelemetry adapter suite (75).
The pre-import audit recomputed all 36 artifact hashes, confirmed the exact 37-file staged set and one
exact-source provenance tuple, and found no connection-value or credential material.

**2026-07-25 preview.86 source alignment**: the seven Groundwork packages and `Groundwork.Tool` consume the
public `0.0.1-preview.86` release built from Groundwork
`fd6d1c1b3cb4ebfce03d4cd57e1420060e8c02ac`. The corrected publication ran from exact Elsa source
`2dc442ea31061971cae6a86a8e8f0a13904cbeb7` (tree
`ae590a5d927e83b9688afa878a02214ed81ee9e9`) and passed the four-provider publisher gate
(1/1, 2m20s). The importer retained 36 records with attachment SHA-256
`954a34a1bb3ce03881bedd167ba87c95d7d58d3f5abdb573e50e123361e0ef24`.
The pre-publication safety gate adds a version-neutral external-staging importer, create-new versioned
publication, exact 36-tuple/provenance/hash validation, four-provider result equivalence, physical
source-scenario execution-path binding, symlink-aware containment, post-capture source revalidation,
crash-window import recovery, immutable preview.80/preview.81 checks, and current-generation-only readiness
evaluation. Before publication, root verification passed the full architecture suite (305), focused publisher
tests (17 passed, two explicit publication tests skipped), benchmark protocol tests (60), and the design
fingerprint gate (1). After publication, an independent read-only audit recomputed all 36 result and artifact
hashes, confirmed nine equivalent four-provider scenario groups and the exact 37-file staged set, and found no
credential material before the mechanical import.

**2026-07-24 historical #646 takeover evidence**: Groundwork PR #126 / Elsa PR #1039 advanced the seven packages to
`0.0.1-preview.81` for batched schema apply. Elsa PR #1040 aligned the tool manifest and current-version
ratchets; the versioned checkpoint/fence publication below then refreshed the tuple-keyed evidence without
rewriting the `.80` attachment or artifacts.

## 1. Inspect the frozen denominator

```bash
jq '.baselineRef, (.entries | length)' \
  specs/094-harden-groundwork-stores/coverage-ledger.json
```

Review [`contracts/coverage-ledger.md`](contracts/coverage-ledger.md). Every implementation PR must list the row identities it advances. A missing/memory-only row is expected to fail readiness, not disappear.

## 2. Restore and build

```bash
dotnet restore Elsa.Server.slnx
dotnet build Elsa.Server.slnx --configuration Release --no-restore
```

Confirm `Groundwork.Core`, `Groundwork.Documents`, every selected provider, and `Groundwork.Tool` resolve to one binary-compatible version.

## 3. Run unit and architecture gates

```bash
dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj \
  --configuration Release --no-build

dotnet test tests/Elsa/Persistence/Groundwork/Tests/Elsa.Persistence.Groundwork.Tests.csproj \
  --configuration Release --no-build

dotnet test tests/Elsa/Foundation/Identity/Persistence/Groundwork/Tests/Elsa.Foundation.Identity.Persistence.Groundwork.Tests.csproj \
  --configuration Release --no-build

dotnet test tests/Elsa/Secrets/Tests/Elsa.Secrets.Tests.csproj \
  --configuration Release --no-build

dotnet test tests/Elsa/Workflows/Runtime/Distributed/Persistence/Groundwork/Tests/Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.Tests.csproj \
  --configuration Release --no-build
```

If a listed project is renamed during implementation, update this quickstart in the same commit. Required architecture results include:

- no Groundwork reference from core modules;
- no growth in the EF surface baseline;
- complete ledger/registration/manifest reconciliation;
- no ready row missing provider, route, restart, failure, or #646 evidence;
- feature-registration and direct implementation branch coverage;
- scoped lifetimes for logic-bearing persistence services, with every non-scoped exception documented and tested;
- no tenant/access context or mutable operation state shared across independently created request scopes.

### Storage-scope gate

The provider-neutral default is one scoped `PersistenceAccessContext` using the nonblank scope `default`.
Multi-tenant hosts replace `IPersistenceAccessContextAccessor` with their own scoped selector before resolving
any store. Ordinary scoped, ordinary global, privileged scoped, privileged global, and privileged across-scope
access are distinct immutable values; privileged access always has a named purpose.

Run the direct scope/session evidence:

```bash
dotnet test tests/Elsa/Persistence/Core/Tests/Elsa.Persistence.Core.Tests.csproj \
  --configuration Release --no-build

dotnet test tests/Elsa/Persistence/Groundwork/Tests/Elsa.Persistence.Groundwork.Tests.csproj \
  --configuration Release --no-build \
  --filter 'FullyQualifiedName~GroundworkStoreSession|FullyQualifiedName~GroundworkPrivilegedAccessRecorder'

dotnet test tests/Elsa/Persistence/Groundwork/Conformance/Tests/Elsa.Persistence.Groundwork.Conformance.Tests.csproj \
  --configuration Release --no-build \
  --filter 'FullyQualifiedName~StorageScopeContractTests'
```

Provider startup may retain only immutable admitted resources. SQLite, SQL Server, and PostgreSQL construct a
fresh access-bound runtime per session. MongoDB retains one validate-only admitted handle and derives fresh
access-bound stores from it without reopening a client or repeating topology/schema admission. Explicit units
of work retain one session until commit/rollback/disposal; all other adapter operations acquire and release one
session. Singleton actors and recurring pumps must open a fresh DI scope per command or tick before resolving
logic-bearing persistence consumers.

### Document scope and admitted capability gate

Runtime, Secrets, and Distributed Runtime storage units are scoped. Identity is scoped except for the deliberately
global `identityGlobalProviderConfiguration` unit; global storage still requires the matching global access and
does not convey privileged write authority. Exact current kinds, physical forms, and manifest owners are catalogued
in the four Groundwork extension-point documents; do not reconstruct a scope from document JSON.

`GroundworkProviderCapabilityAdmission` publishes a capability snapshot only after the selected physical schema
has been admitted. The Groundwork distributed leaf advertises `LeaseFencing` only when that snapshot proves
`AtomicCommit` for `checkpointCommit` / `runtime-checkpoint-commit` and the observed
`multi-document-transactions` topology. The process-local distributed fallback reports no lease fencing.
Run the focused admission evidence in addition to the general conformance suite:

```bash
dotnet test tests/Elsa/Persistence/Groundwork/Conformance/Tests/Elsa.Persistence.Groundwork.Conformance.Tests.csproj \
  --configuration Release --no-build \
  --filter 'FullyQualifiedName~ProviderCapabilityContractTests'

dotnet test tests/Elsa/Workflows/Runtime/Distributed/Tests/Elsa.Workflows.Runtime.Distributed.Tests.csproj \
  --configuration Release --no-build \
  --filter 'FullyQualifiedName~DistributedWorkflowExecutionActorProviderTests'
```

## 4. Run the shared provider matrix

```bash
dotnet test tests/Elsa/Persistence/Groundwork/Conformance/Tests/Elsa.Persistence.Groundwork.Conformance.Tests.csproj \
  --configuration Release --no-build
```

The single suite must execute file-backed SQLite, SQL Server, PostgreSQL, and transaction-capable MongoDB. Inspect the test artifacts for:

- exact provider/topology/package and composition fingerprints;
- independent-client concurrency results;
- disposal/reopen and process-restart results;
- failure-window outcomes;
- provider-native bounded-route evidence;
- matching provider-independent result digests.

Provider-specific expected domain results are a test defect.

### Publish reviewed provider evidence

The ordinary conformance run does not mutate checked-in evidence. To publish the reviewed checkpoint/fence
slice, publish first to an external staging directory, then import only that exact 36-record generation:

```bash
export ELSA_GROUNDWORK_EVIDENCE_OUTPUT="$(mktemp -d "${TMPDIR:-/tmp}/elsa-groundwork-evidence.XXXXXX")"
export ELSA_GROUNDWORK_SOURCE_COMMIT="$(git rev-parse HEAD)"
export ELSA_GROUNDWORK_SOURCE_TREE="$(git rev-parse 'HEAD^{tree}')"
export ELSA_GROUNDWORK_RUN_IDENTITY="runtime-checkpoint-fence-preview103"

# The versioned publisher independently rejects a later or dirty checkout and
# rejects output beneath this repository. These checks keep the shell flow
# fail-closed before a provider suite starts.
test -z "$(git status --porcelain)" || { echo "Refusing dirty source publication." >&2; exit 1; }

ELSA_PUBLISH_GROUNDWORK_RUNTIME_EVIDENCE=1 \
dotnet test tests/Elsa/Persistence/Groundwork/Conformance/Tests/Elsa.Persistence.Groundwork.Conformance.Tests.csproj \
  --configuration Release --no-build \
  --filter 'FullyQualifiedName=Elsa.Persistence.Groundwork.Conformance.Tests.RuntimeProviderEvidencePublicationTests.Publish_the_checkpoint_and_fence_provider_evidence_slice'

dotnet run --project tools/groundwork/Elsa.Groundwork.ProviderEvidenceImporter/Elsa.Groundwork.ProviderEvidenceImporter.csproj -- \
  --ledger specs/094-harden-groundwork-stores/coverage-ledger.json \
  --staging-root "$ELSA_GROUNDWORK_EVIDENCE_OUTPUT" \
  --source-repository "$(git rev-parse --show-toplevel)" \
  --provider-version "0.0.1-preview.103" \
  --elsa-commit "$ELSA_GROUNDWORK_SOURCE_COMMIT" \
  --elsa-tree "$ELSA_GROUNDWORK_SOURCE_TREE" \
  --run-identity "$ELSA_GROUNDWORK_RUN_IDENTITY"
```

The version-scoped publisher writes provider artifacts and one deterministic, merge-ready record set beneath
`versions/<Groundwork package version>/` so an older evidence generation remains immutable. Review and
mechanically import those records by
`(coverageEntryId, scenarioId, provider)` with `(coverageEntryId, scenarioId, provider, providerVersion)` as the
generation-retention key; do not hand-author or infer missing obligations. The importer requires exactly the
checkpoint/fence slice's 36 tuple keys, exact source commit/tree/run provenance, digest-verified staged artifacts,
and unchanged preview.80/preview.81/preview.86/preview.88 history. Publication does
not advance a row status. A row remains incomplete until every declared query, concurrency, failure, and
restart obligation is present for all four providers and the linked #642/#644 authority evidence is current.

### Preview.81 checkpoint/fence evidence refresh (2026-07-24)

Against Elsa source commit `bf452355867c8f76a11d9bca9191563a773a631a` (tree
`8b3504d52cef5f4a19ae5318fc66f46aefcfd048`) and Groundwork `0.0.1-preview.81`, the checkpoint/fence
publisher passed for SQLite, SQL Server, PostgreSQL, and MongoDB (1/1, 1m58s). It produced 36 unique
`(coverageEntryId, scenarioId, provider)` records across `runtime-checkpoint-commit`,
`runtime-execution-liveness`, and `runtime-post-commit-outbox`; every retained artifact digest was
recomputed successfully before the attachment was imported mechanically by that tuple. Each artifact is
version-namespaced and retains the raw scenario observations plus the exact commit, tree, and run identity.
The observations and compiled physical-target fingerprint now come from the same driver execution, and each
record's result hash plus exact artifact-payload comparison binds those observations and provenance.
The original `.80` attachment hash and all 36 referenced historical artifact hashes are guarded separately.

An earlier full publication reached SQL Server and exposed that the fixture's reset query could select
system sessions, which SQL Server refuses to kill. The fixture now limits the reset set to user processes;
the focused SQL Server fence scenario and this complete publication both passed after the correction.
This refresh preserves T050's historical preview.81 evidence. It does not complete
T058/T069/T076/T093/T100 or advance any ledger row status; #646 inherits the remaining provider publication
and performance-verdict obligations.

### Preview.81 checkpoint/fence review disposition

Three adversarial reviewers inspected the frozen implementation/evidence range
`78033cf1167071123cb9fe5ef38653973bd65200..df7fedbdd531bd889a7bfd72a5d436ee53f8dc8e`
on correctness/mechanism, evidence integrity, and scope/test preservation:

| Axis | Initial finding | Disposition | Final verdict |
|---|---|---|---|
| Correctness/mechanism | Version namespaces and retained metadata were not enforced strongly enough. | The publisher now rejects version/path/provenance mismatches; the validator recomputes result hashes and compares the complete retained payload. Focused publisher/capture tests and an independent 36-record audit passed. | PASS |
| Evidence integrity | Synthetic observations and a separately acquired physical fingerprint could green-wash the retained result. | One driver execution now returns the raw actual observations and physical fingerprint, rechecks the fingerprint, and binds both with provenance into the record, artifact, and digest. All nine four-provider matrices and exact ledger imports were independently recomputed. | PASS |
| Scope/test preservation | The first refresh overwrote preview.80 history, then the runnable command could label a later checkout as the retained source snapshot. | Preview.80 was restored byte-for-byte and guarded by its attachment hash; preview.81 is version-namespaced. The reproduction command now short-circuits before publication unless HEAD, tree, and cleanliness exactly match the retained source snapshot; the originating reviewer exercised later, dirty, and exact-snapshot cases. | PASS |

### Preview.86 source-alignment review disposition

Three adversarial reviewers inspected the initial candidate range
`4a5f517f293b54c370a5d0073ce7424f685bb8c5..a7fd50a7f9e089481932a3d677fcaaa2c2b0b4ad`
and the remediated imported candidate through `dba0901a565144a8322cc398e2aab358117fa0f2`. They assumed the
implementation and evidence were green-washed and reviewed correctness/mechanism, evidence integrity, and
scope/test preservation independently.

| Axis | Confirmed finding | Disposition | Final verdict |
|---|---|---|---|
| Correctness/mechanism | Publication synthesized a coverage-obligation path instead of retaining the provider driver's physical execution path; source identity was checked only before capture; lexical containment admitted symlink escapes; a post-directory-move importer crash could strand a valid generation; and the exact-generation architecture guard rejected later valid current evidence outside the closed slice. | Publication now retains and validates the driver's source-scenario/fingerprint path, rechecks exact HEAD/tree/cleanliness before each versioned write and attachment, uses symlink-aware containment, recovers only a complete exact stranded generation, and filters the permanent attachment guard to the attachment's 36 tuple keys. A regression adds a valid non-slice current record and proves both the generic validator and exact attachment guard accept it. | PASS |
| Evidence integrity | The first preview.86 attachment could be mislabeled because its retained execution path was synthesized rather than returned by the real provider driver. | That generation was deleted. The corrected four-provider publication ran from exact commit `2dc442ea31061971cae6a86a8e8f0a13904cbeb7` / tree `ae590a5d927e83b9688afa878a02214ed81ee9e9`; an independent auditor recomputed all result and artifact hashes, physical paths, provenance, and the exact file set before import. | PASS |
| Scope/test preservation | Current-generation validation originally treated any later valid preview.86 evidence outside the partial checkpoint/fence slice as corruption, and pre-import documentation overstated the candidate state. | Both the production validator and permanent exact-attachment guard validate only the closed 36-tuple slice while the generic validator still validates additional declared evidence. Documentation now calls the import partial, preserves all historical generations, and advances no status or performance verdict. | PASS |

For the performance handoff, `requiredNativeRoutes` in the versioned workload documents are exact current
`BoundedQueryDeclaration.Identity` values, not coverage-ledger `queryShapes` or descriptive “bounded” aliases.
For example the current workload set uses `list-claimable`, `list-due`,
`list-owned-live-placements`, and `lease-visible-commands-by-execution`; checkpoint commit has no bounded-query
route because its evidence is admitted atomic-path/topology evidence. The complete, canonical mapping is in
[`workloads/`](workloads/) and its interpretation is in
[`contracts/performance-handoff.md`](contracts/performance-handoff.md).

## 5. Validate the production-shaped combined host

Run the unified-host tests for each provider leaf:

```bash
dotnet test tests/Elsa/Persistence/Groundwork/UnifiedHost/Tests/Elsa.Persistence.Groundwork.UnifiedHost.Tests.csproj \
  --configuration Release --no-build

dotnet test tests/Elsa/Persistence/Groundwork/PostgreSql/UnifiedHost/Tests/Elsa.Persistence.Groundwork.PostgreSql.UnifiedHost.Tests.csproj \
  --configuration Release --no-build

dotnet test tests/Elsa/Persistence/Groundwork/SqlServer/UnifiedHost/Tests/Elsa.Persistence.Groundwork.SqlServer.UnifiedHost.Tests.csproj \
  --configuration Release --no-build

dotnet test tests/Elsa/Persistence/Groundwork/MongoDb/UnifiedHost/Tests/Elsa.Persistence.Groundwork.MongoDb.UnifiedHost.Tests.csproj \
  --configuration Release --no-build
```

The bare unified provider matrix selects the six provider-level families: workflow runtime, secrets,
distributed runtime, workflows design, activities design, and workflows publishing. Identity is never
selected implicitly. The same matrix separately selects the Identity deployment-schema variant and explicit
Groundwork Identity feature, proving that all seven selected families share the exact admitted target. The
SQLite, PostgreSQL, SQL Server, and MongoDB provider leaves are all production-shaped host paths and must remain
in the solution/test gate; SQL Server and PostgreSQL require their real container hosts, and MongoDB requires a
writable transaction-capable replica set. Do not substitute a provider's package-registration assertion for its
provider/topology host evidence.

Also run invalid compositions: missing source, duplicate unit, unsupported route/capability, wrong MongoDB topology, and scope-policy conflict. Each must fail before serving work with a stable owner-aware diagnostic.

## 6. Exercise schema tooling

Build the concrete host schema-source assembly, then set the connection value only through an environment variable. The shipped unified leaves register
`GroundworkAllFeaturesDeploymentSchema` as their six-family runtime authority; hosts that explicitly select
Groundwork Identity use `GroundworkAllFeaturesWithIdentityDeploymentSchema` instead. Both types live in
`Elsa.Persistence.Groundwork.ReferenceComposition.dll`:

```bash
dotnet groundwork validate \
  --manifest-assembly <path>/Elsa.Persistence.Groundwork.ReferenceComposition.dll \
  --manifest-type Elsa.Persistence.Groundwork.ReferenceComposition.GroundworkAllFeaturesDeploymentSchema \
  --provider <provider> \
  --connection-env GROUNDWORK_DEPLOYMENT_CONNECTION \
  --output json

dotnet groundwork plan \
  --manifest-assembly <path>/Elsa.Persistence.Groundwork.ReferenceComposition.dll \
  --manifest-type Elsa.Persistence.Groundwork.ReferenceComposition.GroundworkAllFeaturesDeploymentSchema \
  --provider <provider> \
  --connection-env GROUNDWORK_DEPLOYMENT_CONNECTION \
  --output json

dotnet groundwork status \
  --manifest-assembly <path>/Elsa.Persistence.Groundwork.ReferenceComposition.dll \
  --manifest-type Elsa.Persistence.Groundwork.ReferenceComposition.GroundworkAllFeaturesDeploymentSchema \
  --provider <provider> \
  --connection-env GROUNDWORK_DEPLOYMENT_CONNECTION \
  --output json

dotnet groundwork apply \
  --manifest-assembly <path>/Elsa.Persistence.Groundwork.ReferenceComposition.dll \
  --manifest-type Elsa.Persistence.Groundwork.ReferenceComposition.GroundworkAllFeaturesDeploymentSchema \
  --provider <provider> \
  --connection-env GROUNDWORK_DEPLOYMENT_CONNECTION \
  --safe \
  --output json
```

The source passed to `--manifest-type` must be public, parameterless, and implement
`IPhysicalSchemaManifestSource`. Custom hosts derive a concrete source from
`GroundworkDeploymentSchemaManifestSource`, select their exact feature manifest-source types, override
`CreateStorageNamingPolicy` when names are transformed, and register that same source through
`AddGroundworkStorageComposition<TDeploymentSource>()`. The source type's built assembly is then passed
to every CLI command. Its parameterless construction must be deterministic and configuration-complete:
all manifest and host-naming inputs must be encoded by the source type so runtime and the separate CLI
process reconstruct the same policy definition and resolved target without shared in-memory state. The
shipped unified leaves do this with
`GroundworkAllFeaturesDeploymentSchema`; do not point the CLI at the constructor-bound runtime snapshot
type or at a test fixture. Groundwork's physical target fingerprint remains the value
reported by the CLI and compared by runtime admission. Elsa's separate composition fingerprint also
includes the selected feature sources, their manifest versions, durable requirements, topology
evidence, and naming-policy identity; do not substitute it for the physical target fingerprint.

Use provider values `sqlite`, `postgresql`, `sqlserver`, or `mongodb`. For MongoDB, pass
`--database <database-name>` whenever the URI supplied through `--connection-env` does not contain a
database path; for example, a replica-set URI such as `mongodb://host1,host2/?replicaSet=rs0`
requires `--database elsa`. The runtime host's configured database name and the CLI database must be
identical.

### Exact command outcomes

| Command | Exit | Outcome | Meaning and mutation contract |
|---|---:|---|---|
| `validate --offline` | `0` | `ready` | Manifest, naming, and routes compile; no connection or provider inspection occurs. |
| `validate` | `0` | `ready` | Live applied history and physical objects are compatible. The report can still list pending operations; validation never applies them. |
| `validate` | `3` | `blocked` | Compilation, history, or live physical-state validation failed. |
| `plan` / `status` | `0` | `ready` | The exact target has no pending operations. |
| `plan` / `status` | `2` | `pending` | One or more operations are pending; retain the reported plan fingerprint for review/apply policy. |
| `plan` / `status` | `3` | `blocked` | The diff is not applicable under the greenfield/additive policy. |
| `apply` | `0` | `applied` or `ready` | The authorized plan was applied, or the target already matched. |
| `apply` | `3` | `blocked` | Validation or application planning rejected the target. |
| `apply` | `4` | `authorization-required` | The exact plan needs additional safe/destructive/semantic authorization; no target state was published. |
| Any command | `5` | `invalid` | Invocation, source loading, provider selection, or connection input is invalid. |
| Any command | `10` | `failed` | Execution failed; exception details are suppressed from output. |
| Any command | `130` | `cancelled` | Cancellation was observed and unapplied target state was not recorded. |

Exit `2` is an expected result for a plan/status deployment gate, not a tool failure. Deployment
apply requires exit `0`. Destructive or semantic operations require the exact retained plan
fingerprint and exact operation approvals.

This Elsa composition's runtime admission calls the provider's read-only `IPhysicalSchemaHistoryInspector`,
computes the same Groundwork diff in memory, and never acquires an application lock or invokes schema apply. It
blocks startup with `ELSA-GW-SCHEMA-PENDING` when an applicable plan remains and with
`ELSA-GW-SCHEMA-DRIFT` when durable history or live physical state is incompatible. Operators then
run and review the CLI workflow above; runtime never silently repairs or applies schema.

## 7. Supply and consume #646 evidence

For each workload in [`contracts/performance-handoff.md`](contracts/performance-handoff.md):

1. run the correctness baseline and retain its digest;
2. hand the versioned workload definition to #646;
3. link the reproducible raw/report artifact and verdict into mapped ledger rows;
4. remediate every Redesign outcome and rerun;
5. leave Blocked or missing verdicts incomplete.

Do not time setup, schema application, or a workload whose correctness/provider gate is failing.
For `iam-normalized-lookup-update`, run the real physical Groundwork correctness path with mandatory SQLite and
the opt-in SQL Server/PostgreSQL/MongoDB matrix against Groundwork `0.0.1-preview.103` and the current Identity
storage manifest. Retain its provider identity, input/result digests, observable operations, and native route
evidence captured at 100,000 physical records. The accepted `preview.76`/`preview.77` artifacts, the earlier `preview.60` /
Identity manifest v1.0.4 matrix, and all older artifacts are immutable historical provenance, not current pass
evidence; the ledger remains unlinked until fresh exact-head artifacts exist. The committed EF contract baseline is explicitly non-executed;
#646 owns live EF execution, equality, and timing.

### #646 IAM adapter checkpoint

The #646 harness now owns one provider-neutral `iam-normalized-lookup-update` scenario and executes it
through the real ASP.NET Core Identity store contracts. On 2026-07-24, the timing-free SQLite
checkpoint ran the same fixed seed and observations through:

- `ApplicationIdentityDbContext` registered with `AddEntityFrameworkStores`;
- `GroundworkIdentityUserStore` and `GroundworkIdentityRoleStore` over the production SQLite driver.

Both targets reproduced input fingerprint
`5713ce9b09b68d368d7448041cf513907a648e53df61ccfc307a91381199a8e9` and result digest
`32b62d5597e8b03715d606be9de81af9a363fe05aa2c7bf6d3f3e4cd185ddbbc`. The focused workload and
SQLite correctness selection passed 11/11. This checkpoint does not authorize timing: EF native-plan
capture, the executable matrix target, and the remaining workload adapters are still open, so no
performance verdict or coverage-ledger row advances.

Hosted CI initially rejected the checkpoint because the benchmark test project did not follow the
domain-tree path convention and the EF comparator had been placed in the EF-free Groundwork
conformance project. The remediation moved protocol tests to
`tests/Elsa/Groundwork/StorePerformance/Benchmarks/Tests`, moved the EF-vs-Groundwork SQLite
correctness case into the already-EF-owning Foundation Identity test project, and removed the new EF
edge from Groundwork conformance. After a forced full restore, the benchmark suite passed 16/16, the
two SQLite correctness targets passed 2/2, and the project-path, Groundwork EF-free boundary, and
shrink-only EF-surface guards passed 3/3. The baseline did not expand.

After diagnostics checkpoint #1048 and the next stimuli-only main update landed, branch merge
`28b7c7d11dc3d16d18d67960a8559324f6e9e678` integrated exact `origin/main`
`ca2813d91bc5e6189615f58b5c43405e06988130` without rebasing. A forced full restore then passed;
the complete benchmark protocol/integrity/catalog suite passed **16/16**, the real IAM EF and
Groundwork SQLite correctness targets passed **2/2**, and the combined architecture plus shrink-only
EF-surface ratchet selection passed **65/65**. This remains a harness and one-workload correctness
checkpoint only: it publishes no timing, physical-form selection, coverage-ledger verdict, or #646
completion claim.

The frozen review of that candidate rejected five green-looking but non-durable mechanisms:
preexisting artifacts could be re-certified, comparison trusted stored summaries, cross-commit or
machine-mismatched targets could be compared, native-plan evidence was label-only, and matrix
admission did not consult the frozen provider/form contract. The remediated schema-v2 protocol now:

- binds two distinct four-process measurement sets to one cohort, one expected commit, and one exact
  harness-assembly SHA-256 in a hash-complete manifest;
- rejects stale planned paths, partial or mixed cohorts, changed admitted files, and unbound files;
- validates finite positive raw samples and reproduces stored summaries, while comparison and gates
  recompute their statistics from the raw samples;
- requires exact commit equality and stable machine-environment equality across targets;
- enforces the clean/exact repository HEAD and exact harness-assembly hash in both the public process
  runner and the adapter child before preparation, keeps delegate execution and manifest minting
  test-internal, binds every process to an opaque hash of the OS machine identity, and retains provider
  server version, required topology, and material configuration values so adapter setup differences
  remain visible;
- binds structured route evidence to an actual hashed JSON file and its cohort, measurement set,
  workload input, provider, adapter, physical form, scale, commit, host, provider metadata, and
  composition fingerprint, while every `(measurement set, route)` exclusively owns a distinct
  manifest-bound raw provider-plan JSON/text/XML artifact whose digest and secret-safe structured
  content are validated; and
- admits only providers and physical forms named by the immutable workload contract.

Root revalidation passed the expanded harness suite **53/53**, the real IAM EF and Groundwork SQLite
correctness selection **2/2**, the full architecture suite **285/285** (including the architecture
plus shrink-only EF-surface ratchet selection **65/65**), and a zero-warning Release harness build.
Adapter identity is not present in the immutable
Spec 094 workload schema, so exact adapter allowlisting remains explicitly open rather than inferred.
Likewise, route-specific expected limits/cardinalities require a versioned execution profile or
successor workload; this checkpoint validates that the facts are complete, bounded, target-bound,
and file-backed without mutating the frozen workload document. No timing or ledger verdict is
claimed.

The first re-review did not accept generic OS/runtime/architecture/core-count equality as proof that
both targets ran on one host, and the evidence reviewer did not accept a structured plan summary as a
substitute for the raw provider plan or a caller-supplied commit as proof of source provenance. Those
findings drove the clean-HEAD check, opaque OS-machine fingerprint, adapter-observed provider facts,
seed/input binding, per-route raw-plan retention, cross-set raw-plan ownership, and structured
JSON/XML credential rejection described above. Unit fixtures use synthetic
plans only to exercise the protocol; this checkpoint still has no executable timing adapter and
therefore retains or claims no production plan, measurement, or verdict.

### #646 IAM target-profile admission checkpoint

The executable IAM benchmark target remains deliberately unavailable until the program owner
ratifies the exact EF and Groundwork adapter/form mapping. The harness keeps that decision separate
from the immutable workload's candidate physical forms through an exact
`(workload ID, workload version, adapter, physical form)` admission key. Its authoritative IAM
mapping set is empty.

The two proposed identities
`ef-aspnetcore-identity / ef-identity-relational-schema` and
`groundwork-aspnetcore-identity / entity-type-specific-physical-tables-current-identity-shape`,
plus arbitrary identities, fail closed with
`iam.adapter-form.ratification-required`. Enforcement occurs before matrix creation can fall
through to the physical-form allowlist, before adapter preparation or provider access, before a
matrix child can launch, and before the artifact writer creates its output directory. Comparison
also rejects adversarial manifest-bound artifacts whose request identities were forged outside the
normal writer, and the gate rejects a directly constructed complete comparison.

Root verification passed the focused admission/comparison selection **17/17**, the complete
container-free benchmark-harness project **266/266**, and the Release harness warnings-as-errors
build with **0 warnings / 0 errors**. No provider suite, database-server container, native-plan
capture, timing run, performance verdict, workload JSON, package pin, coverage-ledger row, or EF
oracle changed. T069 and T100 remain unchecked.

Three adversarial read-only reviewers inspected the exact code-bearing range
`878b7a8d3a6e00da73cf4c1c95e3ce84f08a31a4..93410d4ab`. Each assumed the
checkpoint had green-washed its admission and evidence claims:

| Axis | Verification and disposition | Verdict |
|---|---|---|
| Correctness/mechanism | The reviewer traced matrix creation, direct-plan execution, process measurement, artifact creation, comparison, and gating. The empty exact-key mapping blocks both proposed profiles and arbitrary identities before any provider or child side effect; a forged complete comparison cannot bypass it. The reviewer ran the IAM suite and the broader comparison selection with no failure. | PASS |
| Evidence integrity | The reviewer reproduced **17/17**, **266/266**, and the zero-warning Release build; verified that the adversarial artifact test mutates manifest-bound files outside the guarded writer and reaches comparison; and confirmed that the mapping remains empty and every nonclaim above is accurate. | PASS |
| Scope/test preservation | The reviewer confirmed the seven-path additive range changes no provider host, project graph, EF oracle, package, solution, shell, workload JSON, coverage ledger, task checkbox, or existing test objective. Both the lane worktree and primary checkout were clean. | PASS |

### #646 reproducible workload contract-vector checkpoint

The program owner ratified new v1.1.0 successors for the ten non-Identity, non-Secret workloads on
2026-07-24. The benchmark project now owns deterministic contract-vector definitions: parameters,
operation names, expected observations, canonical serialization, and SHA-256 generation. These definitions
are not public-operation runners; future real EF and Groundwork adapter projects must execute them. Run:

```bash
dotnet run --project benchmarks/Elsa.Groundwork.StorePerformance.Benchmarks/Elsa.Groundwork.StorePerformance.Benchmarks.csproj \
  -c Release -- workload-vectors
```

The command emits the exact version, seed, input fingerprint, expected result digest, and benchmark-admission
status that `runtime.json` and `distributed-runtime.json` must contain. `WorkloadCatalog` binds every semantic
JSON input field to the code-owned parameter set, checks independent literal goldens for all twelve
then-current workloads,
and rejects drift before matrix admission. The v1.0 values remain immutable history; this checkpoint does not
claim that they were reproducible or executed.

`secret-create-read-list` remains v1.0.0 and Blocked under
`comparator.secret.real-ef-required` until #646 has a real EF Secret repository comparator. No
synthetic comparator or waiver was added. Its explicit blocked admission is enforced before matrix child
launch and again by comparison and gate, including against forged complete artifacts. It publishes no timings,
physical-form selection, performance verdict, or coverage-ledger advancement; T100 remains open.

### Ratified diagnostics workload and additive ledger denominator

On 2026-07-25 the program owner ratified `diagnostics-durable-history` as the thirteenth workload,
split into Structured Logs and OpenTelemetry suboperations. The reviewed contract vector fixes one
seed, every semantic input, the public-operation sequence, and an independent literal result digest.
SQLite retains the same-provider EF diagnostics oracle. SQL Server, PostgreSQL, and MongoDB require
independently reviewed numeric absolute budgets with correctness, native-plan, physical-form,
provider-work, queue-loss, drain, and restart evidence. No numeric budgets or executable
absolute-budget gate exist yet, so the workload fails closed under
`gate.diagnostics.absolute-budget-required`.

The same amendment adds `diagnostics-structured-log-store` and
`diagnostics-open-telemetry-store` as first-class coverage-ledger rows. The original ALL32 floor
remains immutable; the current exact denominator is 34. `runtime-diagnostics-settings` remains a
separate in-memory contract and supplies no evidence for either durable diagnostics row.

This checkpoint passed:

- workload catalog and independent-golden tests: 11/11, with the remediated full harness at 65/65;
- coverage-ledger and performance-handoff architecture tests: 69/69;
- diagnostics feature/composition tests: 11/11; and
- unified reference-host/schema composition tests: 31/31.

After `dotnet restore Elsa.Server.slnx --force-evaluate`, the complete architecture suite passed
306/306, including the restored-solution EF surface ratchet.

The digest-verified `host-selection-all34` artifact records the eight selected feature identities,
including `elsa-diagnostics`. It is composition evidence only. Both diagnostics rows remain
`implemented` with empty provider evidence and no `performanceVerdict`; no row status advanced, the
workload is blocked from matrix/comparison/gate execution, and T100 remains open.

### Diagnostics workload amendment review disposition

Three adversarial read-only reviewers inspected the frozen implementation range
`e5b8d02ed1c2499f75e33d17da409f1bf13601b6..90df018c0338ec1db5e5a4cdc8f2601f2cabe71f`
on 2026-07-25. Each assumed the checkpoint had green-washed its completion claims:

| Axis | Confirmed finding and disposition | Verdict |
|---|---|---|
| Correctness/mechanism | The initial candidate admitted diagnostics without an executable numeric absolute-budget gate, and later reviews found both direct construction of the public `MatrixPlan` and cloning the public workload record with a forged `ready` admission could bypass caller-controlled checks. Diagnostics now remains explicitly Blocked under `gate.diagnostics.absolute-budget-required`; the shared guard consults the code-owned blocked-ID registry before trusting record state, and catalog, request/artifact admission, matrix planning and execution, comparison, and gate evaluation all reject it before child execution. A regression test forges `ready`, constructs the public plan directly, and proves the child is never invoked. | PASS |
| Evidence integrity | The reviewer independently recomputed the workload source, input, and result digests, verified the exact 34-row denominator and composition artifact, and confirmed both diagnostics rows still have empty provider evidence and no verdict. No numeric budget, timing, physical-form selection, or provider result was invented; the checkpoint records only the ratified workload contract and blocked admission. | PASS |
| Scope/test preservation | The amendment is additive to ALL32, preserves immutable historical evidence and the load-bearing EF oracles, and does not claim #646 completion or advance T100. The diagnostics denominator is isolated from the existing #645 scenario catalog, while source-wide conformance requires the additive workload, both rows, and `host-selection-all34`. | PASS |

Root revalidation of the final code candidate passed the complete non-provider benchmark suite
**65/65**. The evidence reviewer independently repeated **65/65** and the workload-catalog selection
**11/11**. A broader architecture rerun was blocked at build time by the unrelated
`IncidentResolutionBatchExecutor.cs` missing `Elsa.Workflows.Primitives` reference outside this
range; the hosted candidate checks remain the integration authority for that pre-existing failure.

### #646 workload-successor review disposition

Three adversarial read-only reviewers inspected the frozen range
`c4b5fa00499e8448ecf48250fc088ba75186556a..01043fb94acceac69bac3d185235d3ec56e204aa`
on 2026-07-24 and rejected it. Their findings and the remediations included in the replacement
candidate are:

| Axis | Required finding and disposition | Candidate status |
|---|---|---|
| Correctness/mechanism | The JSON fingerprints could match while semantic input fields drifted from the code-owned parameters. `WorkloadCatalog` now compares every semantic JSON field with the code-owned parameter set before admitting the workload, and a negative test proves that recomputing the source digest cannot bypass this check. | PASS |
| Evidence integrity | Expected results were derived dynamically from the same implementation under test, so they were not an independent ratchet. All twelve then-current workloads have separately maintained literal golden vectors, duplicated independently in the conformance test, and catalog admission rejects any mismatch. | PASS |
| Scope/test preservation | The checkpoint described definitions as executable workloads even though it adds contract vectors rather than public-operation runners. The CLI and documentation now use `workload-vectors`, explicitly reserve execution for future real adapters, and remove the unsupported T099 evidence claim. | PASS |
| Evidence integrity | Secret's Blocked state existed only in prose, so synthetic or forged artifacts could still enter matrix, comparison, or gate paths. The closed workload schema now carries `benchmarkAdmission`; matrix planning, comparison, and gating reject Secret with `comparator.secret.real-ef-required`, including forged complete inputs. | PASS |
| Correctness/evidence integrity | The first replacement review found that ready IAM still checked only hashes, version, and seed; reviewed source-digest drift could therefore change its semantic inputs, scenario, or operation sequence. IAM admission now binds all six semantic inputs, scenario identity, and exact operation sequence to `IamNormalizedLookupWorkload`; three negative cases recompute the source digest and prove each class of drift fails closed. | PASS |

Root revalidation of the remediated candidate passed the benchmark harness **60/60**, the focused
workload-correctness conformance selection **5/5**, and the performance-handoff architecture
selection **7/7**. GitGuardian identified the Secret workload's literal identifier beside its two
public SHA-256 golden vectors as a generic high-entropy secret; the dictionary now references the
separately declared workload identifier, retaining the exact immutable vectors without placing a
secret-labeled token beside them. The originating reviewers re-inspected exact clean range
`c4b5fa00499e8448ecf48250fc088ba75186556a..d15d64c9b65559cb554b0371c3c8cfd114f0fe2c`
and returned PASS on correctness/mechanism, evidence integrity, and scope/test preservation. Their
independent verification included the **60/60**, **5/5**, and **7/7** selections above, the real
Identity EF/Groundwork SQLite oracle **2/2**, the full architecture/ratchet suite **285/285**, three
recomputed-digest IAM attacks, and a synthetic Secret matrix attempt that failed before child launch
with `comparator.secret.real-ef-required`.

### #646 harness review disposition

Three adversarial read-only reviewers inspected the frozen range
`ca2813d91bc5e6189615f58b5c43405e06988130..688a8066ef59384601e2c574789388c0727a2f5e`
on 2026-07-24. Each assumed the implementation had green-washed its claims:

| Axis | Required finding and disposition | Verdict |
|---|---|---|
| Correctness/mechanism | The originating reviewer rejected CLI-only source validation because direct `MatrixPlan`/delegate use or a different child could bypass it. The public runner and adapter child now validate the clean exact HEAD and executing harness assembly before launch/preparation; the harness hash is bound through request, native-plan evidence, manifest, and comparison admission; delegate execution and manifest minting are test-internal. The originating reviewer re-ran 53/53 and withdrew the blocker. | PASS |
| Evidence integrity | Successive reviews rejected stale artifact reuse, stored-summary trust, label-only plan evidence, caller-only source/host/provider assertions, shared raw plans, unsafe raw plan/configuration content, endpoint/URI leakage, and false rejection of parameterized endpoint placeholders. Schema-v2 freshness, hash/exclusive ownership, observed provenance, safe structured/raw evidence, and raw-metric recomputation now fail closed. The final reviewer passed 53/53 and a warning-as-error Release build. | PASS |
| Scope/test preservation | The checkpoint remains additive: frozen workloads, coverage ledger, EF ratchet, source projects, and the load-bearing Identity EF oracle are unchanged. All existing conformance tests remain, two SQLite correctness tests are additive, and the documentation explicitly claims no production timing, verdict, ledger advancement, or #646 completion. The reviewer independently confirmed 53 harness tests, 2 IAM tests, and 285 architecture tests. | PASS |

After `origin/main` advanced to `3940e74f280107c454a9021b4107c8e084c41bf1`, merge commit
`b68783fe8e1c78a559215bcaf60b90c0e637103c` integrated it without rebasing. All three reviewers
re-inspected exact range `3940e74f280107c454a9021b4107c8e084c41bf1..b68783fe8e1c78a559215bcaf60b90c0e637103c`
and returned PASS: the incoming scheduling scripts and `shells.json` were base-only, the harness
surfaces were byte-identical to the reviewed parent, and the integrated head again passed 53/53,
2/2, 285/285, and the zero-warning Release harness build.

When `origin/main` advanced again to `23300c68cca7840af5c8b7b4ceaa32e4c8c105af`, merge commit
`b5f762cda5e2c0ec2523267173206ceb5f8d986c` integrated the base-only `TestScripts` to `e2e-tests`
rename and agent-guidance changes. The three reviewers re-inspected exact range
`23300c68cca7840af5c8b7b4ceaa32e4c8c105af..b5f762cda5e2c0ec2523267173206ceb5f8d986c`
and returned PASS; root revalidation again passed 53/53, 2/2, 285/285, and the zero-warning Release
harness build.

### Bookmark lookup correctness-runner review disposition

Three adversarial read-only reviewers inspected the initial frozen range
`6751087c613b150f4c435d11230dbde00eade37e..d3c1db47240407e0e21bf00f477dcaea8f41b738`
on 2026-07-29. Each assumed the container-free checkpoint had green-washed its correctness or
completion claims:

| Axis | Confirmed finding and disposition | Verdict |
|---|---|---|
| Correctness/mechanism | The initial adapter accepted independent state-store and stimulus-index objects even though the production runtime requires the index to be implemented by the same store, so discarded writes plus a prerecorded index could pass. It also tested cross-scope isolation only from primary to secondary. At `312bd3cbf18266410e4d3e6fe5dbef100941108e`, each scope accepts one state-store object and derives the index from that same instance, rejects state-only/discarded-save adapters, probes isolation in both directions, and has selective asymmetric-leak tests that preserve local lookup while leaking only the opposite scope. The originating reviewer re-verified both fixes and found no new blocker. | PASS |
| Evidence integrity | The reviewer independently matched workload ID/version/seed, semantic parameters, input fingerprint, result digest, operation sequence, and observations to the frozen Spec 094 source. After remediation, the unchanged zero cross-scope observation is emitted only after both directional probes pass. The PR metadata was corrected to the exact `312bd3cbf18266410e4d3e6fe5dbef100941108e` head with 12/12 tests, a zero-warning Release build, and a clean diff check; the originating reviewer re-verified the record. No provider, EF, timing, physical-form, native-plan, ledger, or verdict evidence is claimed. | PASS |
| Scope/test preservation | The exact source delta remains limited to the provider-neutral runner, its focused tests, and the Runtime Core project reference. The public adapter contains no provider, connection, timing, manifest, ledger, matrix, or physical-form input. The same-instance and bidirectional-isolation fakes model only the confirmed correctness failures and do not advance a performance task. | PASS |

Root verification at the remediated source head passed the focused workload suite **12/12**,
the benchmark project Release build with **0 warnings/errors**, and `git diff --check`. No database
container, provider suite, timing run, physical-form selection, coverage-ledger edit, or performance
verdict was produced.

After Spec 144 landed on `main` as `ea4683674f8d3bc6961c99e7c0efda20ce819e6f`, merge commit
`4c9c9e7044d66972860a77b7608708195943f48d` integrated that base without rebasing. The runner,
tests, and project reference remained byte-identical to the passed `312bd3cbf18266410e4d3e6fe5dbef100941108e`
source checkpoint; Spec 144 was base-only. Root revalidation at the integrated head again passed
**12/12**, the zero-warning Release build, and exact-range `git diff --check`. Correctness/mechanism
and scope/test-preservation reviewers returned PASS on exact range
`ea4683674f8d3bc6961c99e7c0efda20ce819e6f..4c9c9e7044d66972860a77b7608708195943f48d`.
The evidence reviewer confirmed the code and PR metadata but required this integration paragraph
before its originating-reviewer record re-verification, then returned PASS at record head
`eab814884191446aa2009933d8eb9825d3eacef9`. Merge remains forbidden until all three reviewers
confirm the final record-only head and the hosted checks pass.

### Placement takeover correctness-runner checkpoint

The container-free #646 checkpoint executes the frozen `placement-takeover` v1.1 public-operation
vector through `IExecutionPlacementStore`. Two independently opened clients share adapter-supplied
backing; a third, distinct client opened through the adapter's reopen boundary must observe the
takeover winner. The runner
binds the complete 512-execution input universe, verifies 256 live placements and 256 unplaced
identities, exercises current-owner renewal and foreign-owner denial, advances past expiry, grants
one winner under a two-client released-together contention wave, performs the catalog-observed
takeover with monotonic tokens `1,2,3`, rejects the stale release, and reproduces the ratified input
fingerprint and result digest.

The bounded owner-list route is probed at both 64 and 256 rows. This is deliberate: a single
256-row query over exactly 256 live rows could not detect an implementation that ignored `Take` in
its observable API result. Additional post-expiry probes reject ignored owner and live-lease
filters. Fault-injectable tests also reject missing active writes, aliased or separate client
backing, missing state at the distinct-client reopen boundary, incorrect lease
identity/timestamps/tokens, dual contention grants, wrong ordering, an ignored query limit, and
stale release of the takeover winner. These source-level probes do not establish provider-side
query-plan boundedness or persistence across process/storage recreation.

Root verification passed the focused workload suite **18/18**, the complete no-container benchmark
suite **95/95**, the benchmark project warning-as-error Release build with **0 warnings/errors**,
and `git diff --check`. This checkpoint
does not execute an EF comparator or any provider matrix, start a database container, collect
timing/native-plan evidence, select a physical form, edit the coverage ledger, issue a performance
verdict, or advance T076/T093/T100. Groundwork #50 and the missing real EF placement comparator (or
a separately ratified no-oracle policy) remain later admission gates.

Three adversarial read-only reviewers rejected initial range
`cc9c76a8503c3f2313511ff6a9238193dd46f5fd..a9e55b43f7a6437c2fa3b78d1808276f8f6a88d5`:

| Axis | Confirmed finding and replacement disposition |
|---|---|
| Correctness/mechanism | `concurrentClaimants` was only compared with literal `2`; every store call was sequential, so a non-atomic claim implementation could pass. The replacement releases two independent-client contenders together against one expired lease, requires exactly one grant and one denial carrying the same winner/token, rereads the winner, and includes a deterministic dual-grant fake in which both contenders read expiry before either writes. |
| Correctness/mechanism | The initial owner-list data contained only live `worker-alpha` leases, so ignored owner/expiry predicates were invisible. Post-expiry probes now require no remaining alpha lease and no lease for an unused owner; selective ignored-owner and ignored-expiry fakes must fail. The existing 64/256 probes continue to cover observable ordering and `Take`. |
| Evidence integrity | The checkpoint called a shared in-memory dictionary “durable backing” and described a new wrapper as durable reopen evidence. The replacement describes only adapter-supplied shared backing and distinct-client reopen visibility, and explicitly reserves persisted restart evidence for later real-provider admission. |
| Evidence integrity | The checkpoint implied the API-level 64/256 probe proved provider-side bounded execution. The replacement limits the claim to detecting an ignored `Take` in observable results and explicitly reserves native query-plan boundedness for later provider evidence. |
| Scope/test preservation | The reviewers confirmed the four-path additive scope, provider-neutral adapter surface, unchanged EF oracle/ledger/tasks, and absence of new EF/provider/container dependencies. No scope remediation was required beyond correcting the durability claim and executing the frozen concurrency parameter. |

The same originating reviewers re-inspected exact replacement range
`cc9c76a8503c3f2313511ff6a9238193dd46f5fd..64bc9d6d7d164a9572c57e26f2beb2aa9cddfd19`
and returned:

| Axis | Final exact-range verdict |
|---|---|
| Correctness/mechanism | PASS — the released-together wave requires one grant and one denial with the same winner/token, binds the grant to the requesting claimant, rereads the winner, and rejects both dual-grant and persisted cross-claimant-winner fakes. The latter would pass winner/denial/re-read agreement alone because it stores and returns the same wrong contender, but fails the requester/result binding. The separate catalog-observed takeover keeps `currentOwner=worker-beta` and tokens `1,2,3` deterministic without normalizing the contention winner. Owner, expiry, order, and bound faults all fail closed. |
| Evidence integrity | PASS — independently recomputed input/result digests remain `17f22a7e7896b3842ebd771e604b13e859d1b480bc5b6093ce576f14a673e985` and `3ad65cc7ff9287f9c20a68ec6cd267bc78fa083fb775dda36062c185706fb4b4`. Source, this checkpoint, and PR metadata consistently claim shared-backing/distinct-client visibility only and reserve persisted restart and native-plan evidence. |
| Scope/test preservation | PASS — the exact delta remains four additive paths; the Runtime Distributed reference adds no EF/provider/container dependency, and tasks, coverage ledger, provider evidence, EF oracle, and existing tests are unchanged. The focused suite passed 18/18, the complete no-container benchmark suite 95/95, the warning-as-error Release build had 0 warnings/errors, and the exact-range diff check was clean. |

### Recurring schedule selection correctness-runner checkpoint

The container-free #646 checkpoint executes the frozen `recurring-schedule-selection` v1.1
public-operation vector through `IRecurringTriggerScheduleStore`. It prepares exactly 2,048 schedules
across 256 publications, performs one explicit predecessor-to-candidate publication activation while
retaining exactly 41 inactive publications, and verifies every prepared schedule through bounded
publication pages. The 64-schedule projection publication proves the 50+14 continuation boundary;
every other publication is also traversed so a partial seed, ignored continuation, wrong publication
filter, wrong activation state, or unrelated deactivation cannot pass.

The active due set contains the exact 179 `schedule-due-*` identities at the frozen instant. Separate
queries prove the cutoff, 50-item bound, complete deterministic order, and inactive-publication
filter. Two distinct clients that first prove shared-backing visibility are released together against
the same expected cursor; exactly one advance succeeds, both clients reread the stored transition,
and a stale retry is rejected. A third distinct client verifies the advanced cursor, remaining due
set, and all publication projections through the adapter's reopen boundary. This is shared-backing,
distinct-client visibility only; it is not persisted process-restart or provider evidence.

Three adversarial read-only reviewers rejected initial range
`46dba90ac876ab44ccd8fbf7a92e8ef8b20935de..5d5d8414ff1a336e4c7162fac9465324063eee35`:

| Axis | Confirmed finding and replacement disposition |
|---|---|
| Correctness/mechanism | The initial runner read only the first 50 projection records, never consumed the continuation, and could not detect loss of the 1,819 schedules not also observed by the due query. The replacement traverses all 256 publications and all 2,048 exact records, requires the 50+14 boundary, rejects missing/ignored/repeated continuations, and fails on partial backing. |
| Correctness/mechanism | Every activation passed a null replacement, so the publication transition contract was not exercised. The replacement activates one prepared candidate with an explicit active predecessor, proves the exact final state before and after reopen, and rejects both ignored replacement and unrelated deactivation. |
| Evidence integrity | `advancedScheduleId`, the due-identity digest, and stale-rejection evidence were emitted from expectations rather than the already-checked public results. The replacement derives them from the persisted advance, returned due records, and captured stale-attempt outcome. |
| Scope/test preservation | The additive two-file implementation introduced no package, solution, shell, ledger, EF, provider, container, production-registration, or shared-configuration change. No scope remediation was required. |

The originating reviewers re-inspected exact replacement range
`46dba90ac876ab44ccd8fbf7a92e8ef8b20935de..f49fe079feab234d73ce39b79e713673fda89918`
and returned PASS on correctness/mechanism, evidence integrity, and scope/test preservation. Root
verification passed the focused workload suite **23/23**, the complete no-container benchmark suite
**118/118**, formatting verification, the warning-as-error Release build with **0 warnings/errors**,
and `git diff --check`.

This checkpoint does not run an EF comparator or provider matrix, start a database container,
collect timing or native-plan evidence, select a physical form, edit the coverage ledger, issue a
performance verdict, or advance T076/T093/T100. Groundwork #50 and the missing executable EF runtime
comparator (or a separately ratified no-oracle policy) remain later admission gates.

### Trigger-binding stimulus lookup correctness-runner checkpoint

The container-free #646 checkpoint executes the frozen `trigger-binding-stimulus-lookup` v1.1
vector through `IWorkflowTriggerBindingStore` and
`IWorkflowExecutableSourceReferenceStore`. Two host-selected logical scopes each prepare all 96
publications and all 4,608 bindings, matching the established two-scope interpretation of
`tenantCount: 2`. Publication activation includes one explicit predecessor replacement. The primary
scope retains exactly 31 active exact matches, 17 inactive replacement records, one active
same-type/different-hash distractor, and one active different-type/same-hash distractor.

The runner deliberately traverses opaque continuations rather than using list-all convenience
extensions. It validates the exact stimulus result as 20+11, the complete type lookup including
the alternate-hash record, every 48-record publication projection in both scopes, and every live
Published executable-source reference at the frozen instant. Returned bindings are correlated
with returned source references by publication, artifact, and tenant facts. Both directions of
logical-scope isolation are probed. All six observations are then derived from those
validated public-store results rather than copied from the seed plan.

Before freezing the candidate, root review found that the initial fault harness injected binding
and source leakage only into the secondary scope even though the runner probed both directions. The
final harness adds the reverse-direction faults and explicit missing-prepared-binding and
discarded-source-save cases. Together with activation, replacement, predicate, projection,
pagination, ordering, count, source-live/scope/time, source-fact, and public-surface faults, the
focused suite contains 29 injected failure modes plus three positive/surface cases.

Three adversarial read-only reviewers inspected exact range
`88717fa00eda7cf95fb6a00019ce68fa0504fd83..8223e55b220b80990de59bf589ada6c8da7f0551`
and returned:

| Axis | Final exact-range verdict |
|---|---|
| Correctness/mechanism | PASS — the frozen vector, public production routes, activation/replacement state, full bounded traversals, returned-data correlations, observation digest, and 29 fault modes are coherent. The reviewer independently reran 32/32 focused cases. |
| Evidence integrity | PASS — the literal input fingerprint `4f2515dfa9549935712019f178283f79e6ac1cc9428e810524e733cfdea4cabc` and result digest `00b6651345cdb8b6724a205b094c712d383c7a19ef87dcce6fdf026bc7dd7c8a` reproduce, and no seed expectation manufactures a reported observation. No provider, EF, timing, native-plan, physical-form, persistence, ledger, verdict, or task evidence is claimed. |
| Scope/test preservation | PASS — the effective delta is exactly the provider-neutral runner and its tests, adds no project/package/host/solution/shell/ledger/generated dependency, preserves all existing tests, and introduces no hidden EF or provider dependency. |

Root verification on the integrated head passed the focused suite **32/32**, the complete
no-container benchmark suite **150/150**, and the warning-as-error build with **0 warnings/errors**.
`dotnet format` verification passed for the benchmark test project, and the exact checkpoint passed
`git diff --check`. The benchmark project's whole-project information-severity formatter remains
blocked by 16 pre-existing whitespace findings in unchanged
`Workloads/IamNormalizedLookupWorkload.cs`; this checkpoint does not claim that broader formatter
gate.

This correctness runner does not execute an EF comparator or provider matrix, start a database
container, collect timing or native-plan evidence, select a physical form, edit the coverage
ledger, issue a performance verdict, or advance a Spec 094 task. Those remain later #646 admission
and measurement responsibilities.

### Checkpoint commit correctness-runner checkpoint

The container-free #646 checkpoint executes the frozen `checkpoint-commit` v1.1 vector through
public runtime contracts only. Two independently created clients share adapter-supplied backing,
and a third distinct client exercises the reopen boundary. The runner seeds one immutable executable,
acquires and publicly validates current ownership fences for 128 executions, heartbeats the relevant
fence before each commit, and submits all 1,024 immediate checkpoint
bundles: each bundle contains one workflow-execution update, four activity changes, three inline
durable-value changes carrying the exact 512-byte deterministic payload, and two post-commit outbox
items materialized through the production `RuntimePostCommitOutboxItems` helper.

Accepted checkpoint identities and their digest are derived from returned outbox acknowledgements.
The runner then rereads every workflow, all 4,096 activities through three-item continuation pages,
all 3,072 durable values through two-item continuation pages, and all 2,048 outbox items through the
exact 16-item per-execution bound. An equivalent replay through the second client must preserve the
same 16 logical outbox items, and a conflicting request under the accepted commit ID must expose the
public replay-conflict exception. Returned pages must honor their three-/two-item bounds, and outbox
items must retain deterministic delivery order. After the second ownership client supersedes the selected execution,
two distinct stale commits are released together through the two commit clients; both outcomes and
both retries must reject the old fence, and a complete public reread must expose no stale workflow,
activity, value, outbox, or replay-marker effect. The third client repeats the full public reread.
This is shared-backing distinct-client visibility, not process/storage restart evidence.

Root verification passed the focused workload suite **35/35**, the complete no-container benchmark
suite **185/185**, the benchmark project warning-as-error Release build with **0 warnings/errors**,
path-restricted formatting, and `git diff --check`. The focused fake matrix rejects aliased clients
or component instances, split or fresh backing, response-only executable writes, unavailable
store-owned executable root leases, expired or unrefreshable execution leases, missing or synthetic
acknowledgements, dropped workflow/activity/value/outbox state, malformed or over-limit continuation
behavior, fabricated page members, altered returned identities/payload/accounting facts, reordered
outbox work, equivalent replay duplication, conflicting replay acceptance, stale acceptance, partial
stale mutation, and leaked replay markers.

Three adversarial read-only reviewers inspected initial frozen range
`4b1eadeea42984f9ebcf3134c60f07134abec7cf..ce67cfd61460fd8f6fb6131bfd3d33b9bfc9adcc`.
Their findings and dispositions are:

| Axis | Confirmed finding and disposition | Status |
|---|---|---|
| Correctness/mechanism | One-minute production execution leases could expire across 1,024 commits; the replacement validates every acquired lease, calls `EnsureCurrentAsync`, and requires a successful exact-token heartbeat before every accepted commit. | PASS |
| Correctness/mechanism | Public page bounds and outbox order were normalized rather than proved. The replacement rejects over-limit pages (the public page model also rejects them), uses genuinely fabricated-member and ignored-limit faults, and compares returned outbox order without sorting. | PASS |
| Correctness/mechanism | Equivalent replay did not reject a conflicting fingerprint. The replacement submits an altered request under the accepted commit ID and requires `RuntimeCheckpointReplayConflictException`; the fake retains canonical marker fingerprints and includes an acceptance fault. | PASS |
| Correctness/mechanism | The reviewer asked the workload caller to acquire the executable root-write lease. The production checkpoint store owns that lease around its atomic write; caller acquisition would duplicate and potentially conflict with it. The fake now models and counts the same store-owned boundary and an unavailable-lease fault. This runner does not independently prove the provider-internal lease race; existing runtime/provider conformance remains authoritative. The originating reviewer withdrew the blocker. | PASS |
| Evidence integrity | The “fabricated member” fault repeated a real member. It now returns a unique plausible activity/value record absent from backing. | PASS |
| Evidence integrity | The runner does not inject a valid-current-fence mid-commit failure or recreate storage after failure. That is a retained provider failure/restart gate, not evidence supplied by this shared-backing runner. | PASS |
| Scope/test preservation | The reviewer found the delta additive, the 179-test initial suite preserved, and no project/package/provider/EF/container/ledger/task surface change. | PASS |

The originating reviewers re-inspected exact replacement range
`4b1eadeea42984f9ebcf3134c60f07134abec7cf..860c844d72ad35947df1dd3d6446c6fe25e7a012`
and returned PASS on correctness/mechanism, evidence integrity, and scope/test preservation. Their
independent verification included the focused **35/35**, exact digest recomputation, and clean
exact-range diff; the scope reviewer also reran the complete no-container **185/185** result recorded
by root above.

This checkpoint does not execute an EF comparator or provider matrix, start a database container,
inject and recover from a valid-current-fence mid-commit failure, recreate storage/process state,
collect timing/native-plan evidence, select a physical form, edit the coverage ledger, issue a
performance verdict, independently prove provider-internal executable root-lease exclusion, or
advance a Spec 094 task. Groundwork #50 completion, current-family evidence reconciliation, real EF
comparators, provider failure/restart evidence, and the remaining workload-contract ratifications
stay in the #646 completion gate. The preview.103 exact-source publication/import remains a
prerequisite to any current provider or performance claim.

### Outbox drain correctness-runner checkpoint

The container-free #646 checkpoint executes the frozen `outbox-drain` v1.1 vector through
`IRuntimeCheckpointCommitStore`, `IRuntimePostCommitOutboxClaimStore`,
`IRuntimePostCommitOutboxClaimCompletionStore`, and `IPostCommitOutboxLookupStore` only. It seeds
all 1,024 pending records through public checkpoint commits (never the in-memory test insertion
seam), claims the exact first 32 due identities in declared order, and uses a later scoped sentinel
to release two independent claimants simultaneously without assuming which claimant wins. After
the first 32 current claims are completed as 25 delivered and seven retryable; the retryables are
absent immediately before, and exactly present at, their recorded retry time. Only then, after the
sentinel visibility expires, the non-winning client reclaims that scoped sentinel under a higher
fence. Completion using its original claim is rejected without changing the successor state. A third
distinct client point-rereads every affected identity, status, owner, and fencing token.

The focused fake matrix rejects aliased or split clients/backing, response-only checkpoint seed or
completion, ignored due/scope/limit/order behavior, a missing or wrong scoped sentinel, duplicate
current claims, wrong fence or owner (including sentinel-only owner/state fabrication), stale
completion acceptance, premature/late retry visibility, and altered public reread fences or
delivered identities. It also rejects return-only or persisted claim-time drift and distinguishes the
stable stale-claim exception from an unrelated completion failure. The focused suite passed
**29/29**. This is shared-backing
public-contract correctness only: it does not claim
provider or restart durability, native-plan evidence, timing, EF comparison, a physical-form verdict,
coverage-ledger evidence, or Spec 094 task completion; T100 remains unchecked.

Three adversarial read-only reviewers inspected initial frozen range
`52081ccf8d0adc6ac23e9899f0123e4d6ee1933b..3534f5047adf92978d42fef46a23336e959f5153`.
The review runtime admitted two workers concurrently; the scope/test-preservation review started
immediately when the first slot released. Their findings and dispositions are:

| Axis | Confirmed finding and disposition | Status |
|---|---|---|
| Correctness/mechanism | The exact public-contract flow, deterministic result digest, scoped two-client contention, retry timing, stale-fence rejection, and distinct shared-backing reopen were internally consistent with the production transition contracts. | PASS |
| Evidence integrity | The contention sentinel checked count, identity, and fence but did not prove the winning public owner or exact item/claim state. `RequireContentionClaim` now admits only either declared contender and requires exact claim/item owner, fence, claim time, visibility, and Delivering state. Sentinel-only wrong-owner and wrong-state faults cover the boundary. | PASS |
| Evidence integrity | The first wrong-state fault persisted its fabricated Pending response, so it failed on a second claim rather than exact-state validation. The fake now persists the genuine transition and fabricates only the response; the dedicated test reaches the exact-current-claim error and proves exactly one backing claim. | PASS |
| Evidence integrity | Delivered reopen reads did not compare the returned identity or cleared lifecycle state, allowing one unrelated delivered record to answer every lookup. They now require exact identity, Delivered status, cleared availability/owner/start/visibility/failure, one attempt, exact completion time, and the accepted fence. A return-only wrong-identity fault proves that check while the dedicated test verifies unchanged backing. The production model rejects construction of Delivered records with owner/start/visibility, so those explicit defensive assertions cannot be paired with a legal stale-owner fixture. | PASS |
| Correctness/mechanism | Primary, retry, successor, and reread checks admitted internally consistent but request-shifted claim timestamps. Exact checks now bind outer and persisted start/visibility times to each request's `Now` and timeout; isolated return-only and persisted-time faults fail closed. | PASS |
| Correctness/mechanism | Any `InvalidOperationException` was counted as a stale-fence rejection. The public completion contract and transition now expose `RuntimePostCommitOutboxStaleClaimException`; the runner catches only that type, and an unrelated-failure fault proves other completion failures propagate. The runtime store contract test verifies the stale exception's presented and current ownership evidence. | PASS |
| Evidence integrity | Retry-at executed at `10:01:01`, then successor reclaim moved semantic time backward to `10:01:00`. Successor reclaim now uses `retryAt`, one second after expiry, and the positive fake records every claim request time and requires monotonic chronology. | PASS |
| Scope/test preservation | The typed stale exception initially broke exact-type assertions outside the first focused slices. Twelve stale-specific assertions across runtime, dispatch projection, Groundwork store/crash/redrive, and provider-conformance tests now require the stable type; genuinely unrelated and legacy-result failures retain `InvalidOperationException`. | PASS |
| Evidence integrity | The first persisted-time fault shifted both the stored item and returned claim, so it failed at response validation instead of proving reopen detection. It now returns the genuine retry claim, persists only a shifted retry item, and must reach the reopened-retry current-claim error. | PASS |
| Scope/test preservation | The additive delta is confined to the outbox runner/tests/evidence plus the minimal public stale-claim exception, completion-contract documentation, transition classification, and its runtime store contract assertion. It does not change projects, packages, providers, EF surfaces, containers, the coverage ledger, or Spec 094 task state. | PASS |

The originating evidence reviewer re-inspected final implementation range
`52081ccf8d0adc6ac23e9899f0123e4d6ee1933b..53048926f07590f730a39cce85c8dab7d90da89f`,
independently reproduced focused **26/26**, and returned PASS with no remaining blocker, P1, or P2.
Root independently reproduced focused **26/26**, complete container-free **211/211**, and
warning-as-error builds with **0 warnings / 0 errors**. Exact changed-file formatter checks and
`git diff --check` are clean. The project-wide formatter also reports a pre-existing whitespace
defect in `IamNormalizedLookupWorkload.cs`, which is outside this exact range and was not rewritten
in this checkpoint.

A later record-complete review of
`52081ccf8d0adc6ac23e9899f0123e4d6ee1933b..0088dd409413ce40df254d6ac79383399247b0fe`
found the claim-time, exception-classification, and chronology gaps recorded above. Final
implementation range
`52081ccf8d0adc6ac23e9899f0123e4d6ee1933b..4b93bbf9eed28925dbdd4e410b71b83fd8111e26`
closes them. Root reproduced focused **29/29**, complete container-free **214/214**, the runtime
store contract **14/14**, and warning-as-error benchmark builds with **0 warnings / 0 errors**.
The runtime store project reports one pre-existing xUnit analyzer warning in
`RuntimeStartCommandSchedulingTests.cs`; the changed runtime contract test is clean.

Correctness re-verification of
`52081ccf8d0adc6ac23e9899f0123e4d6ee1933b..1626c2c4086982d4531451aa3a80d7cbed84002f`
found the exact-type preservation and persisted-fault isolation gaps recorded above. Final
preservation range
`52081ccf8d0adc6ac23e9899f0123e4d6ee1933b..5731d62521422609c9362d72f439fd132c816e0a`
closes them. Root reproduced benchmark focused **29/29** and complete **214/214**, affected runtime
classes **62/62**, dispatch projection **9/9**, Groundwork store/crash/redrive **72/72**, and the
exact outbox stale-ack contract **4/4** across SQLite, SQL Server, PostgreSQL, and MongoDB. Changed-file
formatter checks and `git diff --check` are clean; the touched cancellation-crash test also
normalizes a pre-existing seven-line initializer indentation defect without changing behavior.

### Queue drain correctness-runner checkpoint

The container-free #646 checkpoint executes the frozen `queue-drain` v1.1 vector through
`IWorkflowSchedulerWorkQueue`, `IWorkflowSchedulerWorkClaimInspection`, and
`IWorkflowSchedulerPoisonStore` only. It enqueues the fixed 128 × 32 workload, then independently
rereads every exact 32-item FIFO before reading the exact bounded first 16 workflow IDs and racing
two independently-created queue clients for each selected head. Eight winning current claims are
accepted only after claim inspection proves one exact active owner per workflow, then completed.
Five claims expire and are reclaimed under higher
fences; each old acknowledgement is attempted while its successor is still current and must return
the public `Stale` outcome. Three current claims are acknowledged before matching poison records
are written and read through the public poison contract. A third distinct client then verifies the
advanced normal/poison queue heads, the still-current successor claims, and all three poison records.

`completedItemCount = 8` deliberately describes only the normal-completion group. The three poison
claims are also acknowledged before their poison records are written; they are represented by
`poisonItemCount = 3`, not folded into that frozen normal-completion observation. This is
shared-backing public-contract correctness only: it does not claim provider or process-restart
durability, native-plan evidence, timing, EF comparison, a physical-form verdict, coverage-ledger
evidence, or Spec 094 task completion. T100 remains unchecked.

Root verification strengthened the worker result after finding that its initial enqueue check
compared only three work-item members and did not reread the unselected 112 workflows. The accepted
candidate compares every public work-item member, rereads all 4,096 items through the independently
opened client, inspects the persisted single-winner result of every contention, adds a response-only
final-seed rejection test, and compares the full poison record returned by `ListAsync`. The focused
queue suite is **17/17**, the complete benchmark-harness test
project is **231/231**, the benchmark project warnings-as-errors build is clean, both changed-code
path-restricted formatter checks pass, and `git diff --check` is clean. These are container-free
correctness checks; provider and restart evidence remain deliberately unclaimed.

The three initial adversarial reviews examined `8c1c27e..8b3cb89f`. Evidence integrity passed, but
correctness requested changes because the runner trusted returned contention claims without first
inspecting persisted active ownership, compared only claim-item IDs, and did not bind retry takeover
and stale acknowledgement to the losing and original-winning clients respectively. Scope/test
preservation confirmed the delta was additive and contained, but requested a fail-closed duplicate
active-claim mutation and replacement of the lexical provider-neutrality assertion. All findings
were accepted: persisted single-winner inspection and its mutation test were added; every claimed
work-item member is compared with a corrupted-item mutation; retries use the actual losing client
and stale acknowledgements use the actual original winner, with exact takeover counters; and the
adapter signature test now recursively permits only an explicit provider-neutral type set. The
initial evidence PASS is retained as historical review evidence but is not treated as final-head
approval.

The first final-head pass over `8c1c27e..7a675dd` produced PASS verdicts on correctness and evidence
but the scope/test-preservation reviewer found that the race discarded requester identity, allowing
a cross-claimant owner grant to masquerade as the other client’s win. That reviewer also found that
the semantic provider-neutrality check omitted inherited interfaces and base types. Both findings
were accepted, superseding all verdicts on that head: each contention result now carries the actual
requesting, winning, and losing client; the returned owner must equal that request’s owner; a
cross-claimant-grant mutation fails closed; and the explicit type traversal includes inherited
interfaces and base types (with only the records’ provider-neutral `IEquatable<T>` shape allowed).
Root’s post-freeze audit then found that initial persisted ownership inspection still used the
Secondary client unconditionally. That head was also superseded: every winner is now inspected
through its actual losing/opposing client, and the focused fake requires exactly **16** such
opposing-client inspections.

The canonical three-axis review of `8c1c27e..51373f1` passed with no blocker, P1, or P2.
Correctness/mechanism verified requester binding, actual-client propagation, opposing-client
inspection, takeover/stale provenance, full claimed-item fidelity, poison/reopen behavior, and the
frozen digest. Evidence integrity independently reran the focused suite (**17/17**) and confirmed
that all observations and nonclaims are truthful. Scope/test preservation confirmed that the delta
contains only this runner, its additive tests, and this quickstart; no frozen workload, shared
serialized file, task, ledger, EF oracle, provider, production registration, package, solution, or
shell changed, no test was removed or weakened, and T100 remains unchecked.

### Distributed command transport correctness-runner checkpoint

The container-free #646 checkpoint executes the frozen `command-send-lease-ack` v1.1 vector through
`IExecutionCommandTransport` only. Two distinct public clients share adapter-selected backing and a
third distinct client exercises reopen visibility. It sends 128 × 64 commands, with the two
barrier-synchronised sends deliberately placed in a non-primary workflow so the primary golden
batch remains `command-0000` through `command-0015`. Two concurrent leasers each request eight
items; their returned requester/result tuples must union to the exact ordered 16-item batch. After
31 seconds, successors re-lease the same batch under current tokens; stale first-generation
acknowledgements are rejected before current acknowledgements succeed. The reopened client reports
all 128 visible workflow IDs and exactly 8,176 pending commands.

This is shared-backing public-contract correctness only. It does not claim provider or process
restart durability, native-plan evidence, timing, EF comparison, a physical-form verdict,
coverage-ledger evidence, or Spec 094 task completion. T100 remains unchecked.

Root verification rejected the worker’s initial **12/12** result because send acknowledgements did
not compare the full command/envelope shape, concurrent lease responses were hashed in client order
without first establishing a canonical batch, each leaser’s eight-item bound was not enforced, and
reopen checked only an aggregate count. The accepted candidate compares every public send/lease
member, proves 8,192 unique transport identities and exact dedicated-stream sequences 63/64,
requires each leaser’s ordered eight-item share before canonicalizing the cross-client union by
sequence, verifies exact per-workflow reopen counts (48 for the acknowledged primary stream and 64
for each other stream), and separately fails closed on response-only send, corrupt item/token/order,
fabricated visible-list, and fabricated count behavior. The semantic adapter guard traverses
inherited interfaces and base types against an explicit provider-neutral type set. The focused suite
is **18/18**, the complete container-free benchmark-harness project is **249/249**, the benchmark
warnings-as-errors build is clean, changed-code formatter checks pass, and `git diff --check` is
clean.

The initial three-axis review of `286f559..8322a48` passed evidence integrity and scope/test
preservation, but correctness found that the first and successor lease generations used different
owner IDs. A stale acknowledgement could therefore be rejected for owner mismatch even if the
transport ignored its required lease token. The first remediation used one owner across both
generations and added an `IgnoreLeaseToken` mutation. On the next exact-range review, correctness
and evidence integrity passed, but scope/test preservation correctly rejected that shape because it
erased the contract's cross-node takeover semantics; the same review also found that the semantic
surface guard treated `IExecutionCommandTransport` as a terminal allowed type instead of traversing
its public members.

The accepted remediation preserves distinct client and generation owners. It first proves that the
actual first-generation owner/token tuples are stale after takeover, then pairs each successor with
the old token for the same transport item and attempts acknowledgement through the current
successor owner. That second probe isolates token fencing from owner mismatch before the current
token succeeds, and the `IgnoreLeaseToken` mutation must fail closed. The originating reviewer then
found a replacement P1: those two probes still allowed a transport that ignored the owner while
enforcing the token. The final candidate therefore also attempts each current token through its
matched first-generation owner before current acknowledgement; the `IgnoreLeaseOwner` mutation
must fail closed. Token and owner fencing are now isolated independently while the original stale
tuple still proves their combined takeover boundary. The semantic surface guard now traverses
`IExecutionCommandTransport` itself and explicitly permits only the provider-neutral types exposed
by that public contract.

The final code-bearing review of `286f559..9682f4961` passed correctness/mechanism with no blocker,
P1, or P2. Evidence integrity and the originating scope reviewer agreed that the three stale
combinations, both fencing mutations, takeover, and the semantic-surface fix were sound, but each
reported one evidence-only P2: adding `IgnoreLeaseOwner` made the committed **17/17** and
**248/248** test counts stale. Root had independently rerun the exact candidate at **18/18** focused
and **249/249** complete-project, so this documentation-only disposition records those verified
counts. No code, workload vector, task, ledger, provider, EF oracle, production registration,
package, solution, or shell changed in the disposition.

## 8. Readiness audit

Before a lane is declared ready:

- compare every ledger row with the exact branch HEAD;
- verify every mandatory provider uses an active production path;
- verify wrong-scope, privileged, concurrency, failure, and restart evidence;
- verify every scale-bearing query is bounded at the provider;
- verify #644/#660 ownership boundaries and #646 verdicts;
- rerun the EF-surface and core-dependency ratchets;
- verify all existing behavioral test objectives remain covered;
- update extension-point catalogs, program-goal/decision-map state, generated maps, and issue links.

Only then may the row become `ready`. Final EF deletion remains a later program gate across all persistence lanes.
