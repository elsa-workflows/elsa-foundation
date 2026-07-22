# Quickstart: Validate Groundwork Design Persistence

**Work unit**: `093-groundwork-design-persistence`

This guide is the executable evidence checklist for issue #641. Commands assume the repository root and Release configuration.

## Prerequisites

- .NET 10 SDK.
- Docker or compatible provider containers for SQL Server, PostgreSQL, and MongoDB.
- A MongoDB replica set or sharded deployment for the transaction-required suite.
- One binary-compatible Groundwork version across `Groundwork.Core`, `Groundwork.Documents`, all four provider packages, and `Groundwork.Tool`.
- Provider connection values supplied through environment variables; never commit or pass secrets in process-visible command arguments.

## Baseline record (T001 complete; T004 deferred to T025)

The work unit is based on `origin/main` commit `d1548991f`, captured on 2026-07-20.
The repository currently pins the Groundwork library family, including SQL Server and
MongoDB, and `Groundwork.Tool` to `0.0.1-preview.72`. The public preview feed reports
`Groundwork.Tool` `0.0.1-preview.73` as its newest release; `0.0.1-preview.75` is not
available. This records the actual baseline for T001. T009—not T001—selects and pins
the later released binary-compatible library/tool family that provides the required
physical-storage and query APIs.

The captured provider images are SQLite (embedded), SQL Server
`mcr.microsoft.com/mssql/server:2022-CU21-ubuntu-22.04`, PostgreSQL
`postgres:17.6-alpine3.22`, and MongoDB `mongo:7.0.24`. One existing PostgreSQL
unified-host test still uses `postgres:16-alpine`; T053 selects and records its fixture
image as part of the provider contract work.

The pre-extraction EF behavioral suite inventories are 354 workflow-design tests and
467 activity-design tests. Their focused restore and discovery succeeded at this base;
full-solution Release discovery found 6,588 tests without executing them. The shared
fixture scaffold currently has 8 deterministic-fixture tests. See [baseline
evidence](evidence/baseline.md) for the commands and precise evidence scope.

### T021–T024 shared contract-suite scaffold

The shared project now contains abstract, executable-by-inheritance workflow and activity suites.
They exercise readiness, scoped lifecycle reads and writes, point-read scope isolation, wrong-scope
non-disclosure and write rejection, scope-local duplicate identities, true reusable-activity-draft
OCC, the workflow draft's intentional last-writer-wins policy, single-scope restart/result-hash parity,
and Groundwork-target cross-scope same-identity restart isolation; they also exercise draft and version
layout round-trips, clone provenance, validation rejection, descriptor serialization,
SemVer identity, missing outcomes, permanent-delete outcomes, and reconciliation idempotency. T023 adds
provider-neutral atomicity fault contracts for `AfterStagedWrite`, `BeforeProviderDecision`, and
`AfterDurableDecision`, with throw, cancellation, and non-success actions. Its canonical operation request
carries a caller-stable operation/idempotency key separately from the canonical request fingerprint. The
shared suite proves partial-staging rollback, non-success rollback, cancelled-token propagation, durable-result
reconciliation after acknowledgement loss, exact replay, same-key/different-fingerprint conflict without
mutation, scope-bound operation ledgers, and duplicate-outcome suppression. The scenarios reference only
Elsa core contracts plus `IDesignPersistenceContractFixture`; their atomicity snapshots expose canonical
aggregate-state and authoritative durable-result fingerprints for replay and conflict assertions. The reusable
test assembly is guarded against provider SDK assembly
references. T025 and T051–T054 must therefore place concrete EF-oracle and provider fixtures in separate
provider-specific test projects that reference this shared project; provider SDK references must not enter the
shared assembly. T024
currently asserts non-cancellation conflict classification plus unchanged state; T033/T035 own the
final domain-scoped write-conflict exception taxonomy and direct adapter mapping coverage. Fixture
staging is explicitly non-persisting; the activity suite proves both the candidate definition and
version are absent before it resolves and invokes the real `IActivityVersionReconciler`. A
provider-neutral behavioral harness executes that inherited scenario and proves that the resolved
reconciler is invoked on both idempotency passes. Captured domain events assert clone, validation,
discard, and reconciliation identities and outcomes, including empty validation errors. Canonical
restart snapshots contain the authored workflow state and activity descriptor, and assert each
against the deterministic input before comparing restart hashes.

The shared-contract scaffold completed with `96` passed tests and one intentional legacy-EF N/A skip
(`97` total); the skipped probe proves that N/A profile evaluation happens before fixture creation. T025's executable
`LegacyEfOracle` profile runs exactly five of these rows: partial-staging failure, cancellation, same-scope
duplicate identities, workflow-draft last-writer-wins, and the single-scope restart snapshot. Its ten explicit
N/A rows are provider non-success decision; acknowledgement loss, exact replay, key-reuse conflict, and duplicate
delivery; same identities across scopes; foreign reads and writes; reusable-activity-draft OCC; and cross-scope
same-identity restart isolation. The profile records a
nonempty reason per N/A row. Test-only EF fault injection is permitted for partial staging and cancellation;
T025 must not add a production EF operation ledger, scope boundary, or reusable-draft OCC shim. T033/T035's
exception-taxonomy deferral remains unchanged.

### T004/T025 EF SQLite oracle baseline (2026-07-20)

`Elsa.Persistence.Groundwork.DesignConformance.EFCore.Tests` is a separate provider-leaf test project.
It applies both existing SQLite migration chains to one temporary file database, recreates the composed
service provider on restart, uses public design commands/stores plus the real activity reconciler, and
keeps its partial-staging/cancellation faults in a test-only EF `SaveChangesInterceptor`. Its four
inherited contract suites completed with `15` passed and `10` intentional LegacyEfOracle N/A skips
(`25` total). Its two focused evidence tests pass as well, so the complete EF provider leaf reports
`17` passed, `10` skipped, and `27` total.

The deterministic canonical result hashes captured after restart are:

- workflow draft: `8614e1ffb739e65f08a47c60da7c33891fff5f88c32ef5d88eed7b9fd5800369`
- workflow version: `3fcb29139ffcac2c0fc3d5450d4e2682d3b504b86f31326b16b17f4712ceb9d4`
- activity definition/version: `8bdeb707ea537e6357a8327b2d7404deb23af9e370c65a03f2e2883b182bf3f1`

These are workload behavior hashes, not test-inventory hashes; the evidence test pins them.

### T025 Groundwork SQLite Target-profile baseline (2026-07-20)

`Elsa.Persistence.Groundwork.DesignConformance.Sqlite.Tests` is a separate provider-leaf baseline
project. For every scenario it creates a fresh file-backed SQLite target, drives production
`Groundwork.Tool` `validate --offline`, `plan`, and `apply --safe` commands against
`GroundworkAllFeaturesDeploymentSchema`, then builds the public
`AddGroundworkSqliteUnifiedPersistence` composition with startup auto-apply disabled. Runtime
startup only admits the applied target. Restart scenarios dispose and rebuild the service provider
against the same file without reapplying schema, and every design operation resolves from a fresh
DI scope bound through `IPersistenceAccessContextBinder`.

The immutable Target-profile catalog executed all `25` T021–T024 rows and proved the ratified
intentional-red matrix:

- `17` green lifecycle, reconciliation, scope, OCC, last-writer-wins, and restart rows;
- `7` classified red atomicity rows:
  `design-atomicity-operation-ledger-absent`;
- `1` classified red same-scope duplicate row:
  `same-scope-identity-rejection-absent`.

The runner fails if any expected green becomes red, any expected red becomes green, or a red row
does not match its narrow classification. It also derives and pins the loaded Groundwork Core,
Documents, SQLite, and local-tool package versions plus the exact target and plan fingerprints;
schema-tool processes have a bounded timeout and are killed and reaped on timeout or cancellation.
The run used the Groundwork library/tool family
`0.0.1-preview.77`, target fingerprint
`b87b33275b2f9f52a8756eeb039bb8227a93083a6adacb95cd6a16c2445253d3`, and plan fingerprint
`d8717f5ebca1755d8aa24473c37da3a2dc823f70d069ec38fa4de40ff2b012f8`.
The T030 reference-composition change intentionally advances both fingerprints by adding the
scope-bound, dedicated `designOperation` document table and its atomic-commit route; the Target-profile
behavior matrix remains `17` green and `8` narrowly classified intentional-red rows until T031–T032
route the workflow and activity commands through that ledger.
It also exposed and fixed target correctness defects: promotion now copies the validated
scope-bound draft's `TenantId` to both the immutable workflow version and its layout, and
submission stamps the active scope onto every newly created aggregate member.
Both provider fixtures dispatch through Elsa's scoped event pipeline; the EF cancellation probe
pins the same linked token at the public command, event handler, EF interceptor, and exception.

### T030 shared atomic-write coordinator evidence (2026-07-20)

Implementation candidate `10469b79902b9cb2235f41dcddd1c148c5c7d898` centralizes stable
operation identity, canonical request fingerprints, replay/conflict inspection, rollback,
cancellation, marker-race handling, and independently bounded acknowledgement reconciliation in
`GroundworkDesignAtomicWrite`. Workflow and activity design registrations share one scoped helper
and one append-only, scope-bound `designOperation` manifest source. The marker uses a dedicated
physical document table, joins the mutated document kinds in one `CrossUnitAtomic` commit scope,
and requires both the `AtomicCommit` capability and the `multi-document-transactions` topology.
Prior successful deliveries return `Replayed` so callers suppress duplicate post-commit outcomes;
a successful commit recovered after a lost acknowledgement returns `Reconciled` so the current
attempt can complete its post-commit outcome phase exactly once, just as it does for `Committed`.
The internal marker unit is explicitly tracked by the architecture inventory without changing the
frozen spec 094 ALL32 public-contract denominator.

The exact implementation candidate passed:

- `GroundworkDesignAtomicWrite*`: `18/18`;
- full Groundwork querying: `112/112`;
- workflow design Groundwork: `60/60`;
- activity design Groundwork: `51/51`;
- Groundwork composition: `58/58`;
- Groundwork unified host: `23/23`;
- Groundwork core persistence: `637/637`;
- full architecture ratchets: `261/261`;
- SQLite Target-profile schema and 25-scenario behavior baseline: passed, preserving `17` green
  and `8` narrowly classified intentional-red rows;
- full `Elsa.Server.slnx` Release build: `0` errors and `147` accepted existing warnings;
- the exact CI-generated container-free filter: all `77` test projects passed.

An independent read-only review was clean after verifying terminal rollback observation against
Groundwork preview.77's normative `IDocumentUnitOfWork` contract. T031–T032 still own command
adoption, so this slice does not advance any of the seven intentional-red atomicity rows.

### T031–T035 design-command adoption evidence (2026-07-20)

Every workflow definition/version/draft/promote/submit/delete command and both activity
definition/version commands now route through the typed atomic facade. Public contracts require
an explicit caller-stable `DesignOperationKey`; operation identity remains separate from the
canonical request fingerprint, and generated definition/version/draft identities come back from
the durable authoritative result. Source reconciliation uses separate materialization commands so
API allocation policy does not leak into source-owned projection.

Groundwork reads and writes now surface provider-neutral `DesignPersistenceException` outcomes
with explicit Workflow/Activity and Provider/Serialization classifications. Only positively
classified provider, corrupt-payload, marker/result corruption, or explicitly scoped
serialization operations are mapped. Cancellation, readiness, validation, operation-key
conflict/rejection, uncertain-commit, and other domain outcomes retain their existing contracts.
Repeated provider composition resolves every public store/command, the shared atomic helper, and
each manifest source exactly once per scope.

Lost-acknowledgement reconciliation now detaches post-commit lifecycle-event enqueue from the
caller token that may have been cancelled after the durable commit. Create, update, and discard
tests prove one enqueue after reconciliation and none on the subsequent replay, preserving the
established best-effort/at-most-once deferred-event policy without claiming durable event
delivery. Promotion performs its marker lookup before source reads and locks, so an exact replay
returns the authoritative version even after the source draft is discarded. Submit validation
runs only after a marker miss, so mutable activity-structure projections cannot invalidate an
already committed exact replay. Clone owns a distinct operation kind whose canonical request is
the immutable source-version identity. Its marker lookup runs before source expansion, identity
allocation, locking, staging, or event publication, so exact replay returns the authoritative
draft even after the source version and layout are removed; reusing the key with a different
source identity conflicts before reading that source.

The SQLite Target profile now executes all seven provider-transaction atomicity scenarios through
a real scoped `GroundworkDesignAtomicWrite` over two admitted workflow document kinds plus the
durable marker. Its fixture-local post-commit observation proves retry/replay control flow only;
it is not a domain-event publication. Separately, the composed SQLite host invokes the public
`ICreateDraftCommand`, starts Elsa's real background event dispatcher, captures
`OnDraftCreated` in its ScopeA pipeline handler only after verifying the draft through
`IWorkflowDefinitionDraftStore`. Its test-only fault seam injects a throw after one staged write,
a rejected provider decision, cancellation, and a post-durable lost acknowledgement. Exact replay
uses the same logical operation identity in two storage scopes, proving that isolation comes from
the storage boundary rather than from scope
material embedded in the key. `OnDraftCreated` intentionally does not carry a storage scope and
the background dispatcher opens a fresh scope, so this lifecycle-event evidence is explicitly
limited to the ScopeA fixture; it does not prove cross-scope event-context propagation. Evidence
schema `3` advances the immutable
catalog from `17/8` to `25` green and `0` intentional-red scenarios while retaining target
fingerprint `b87b33275b2f9f52a8756eeb039bb8227a93083a6adacb95cd6a16c2445253d3`
and plan fingerprint `d8717f5ebca1755d8aa24473c37da3a2dc823f70d069ec38fa4de40ff2b012f8`.

The integrated pre-review candidate passed:

- persistence core: `43/43`;
- Groundwork querying: `126/126`;
- workflow design Groundwork: `82/82`;
- activity design Groundwork: `68/68`;
- focused SQLite atomicity/lifecycle suite: `8/8` — `7/7` provider-transaction scenarios plus
  `1/1` public `ICreateDraftCommand`/`OnDraftCreated` durability scenario;
- SQLite 25-scenario Target-profile baseline: `1/1`, with all `25` logical scenarios green;
- shared design conformance: `96` passed and `1` intentional profile-shape skip;
- temporary EF oracle: `17` passed and `10` explicitly inapplicable skips;
- Groundwork unified host: `23/23`;
- workflow design domain suite: `358/358`;
- activity design domain suite: `490/490`;
- workflow design API: `50/50`;
- activity design API: `5/5`;
- workflow publishing API: `381/381`;
- full `Elsa.Server.slnx` Release build: `0` errors.

T036 records the exact committed candidate and independent review after this evidence is committed.

Ordinary CI writes no evidence file. Operators can opt in to the sanitized allowlisted JSON
artifact without exposing connection strings, database paths, or scope identities:

```bash
ELSA_DESIGN_GROUNDWORK_BASELINE_EVIDENCE_DIR=/path/to/evidence \
dotnet test tests/Elsa/Persistence/Groundwork/DesignConformance/Sqlite/Tests/Elsa.Persistence.Groundwork.DesignConformance.Sqlite.Tests.csproj -c Release
```

This closes only T025's Target-profile baseline. T051's broader SQLite fixture, schema-drift hooks,
and later full provider conformance remain pending. The green lifecycle rows still traverse the
existing load-all/client-evaluation read path and are not bounded-query evidence; T026–T029 remain
the authority for removing that path.

### T036 independent review and final US1 evidence (2026-07-21)

The reviewed production candidate is `b468c03e8087f9b7ce7fc16611f2f509899db060` (the six US1
commits above base `c1663ad4f`). Three independent read-only reviewers examined the exact range
`c1663ad4f..b468c03e8` on 2026-07-21 across (1) atomicity, rollback, retry/lost-acknowledgement
reconciliation, replay ordering, cancellation detachment, canonical fingerprints, and EF-oracle
purity; (2) lifecycle-event semantics and test-objective preservation against the T003 ledger
(zero test deletions confirmed); and (3) scope isolation, contract-only DI seams
(`IDesignAtomicWriter`/`IDraftOriginator`), core purity, exception taxonomy, and extension-point
catalog accuracy. The scope/DI/docs axis was clean; the marker ledger's scope binding, the
privileged-session `TenantAgnostic` gate, and the renamed exception taxonomy were each explicitly
verified.

One blocking finding was remediated: the issue-#404 workflow add-version race-rejection objective
had lost its only test when `AddVersionCommandHandler` became a delegating shell. Commit
`587334cec` (test-only, additive; the six reviewed production commits are untouched) restores it
at the Groundwork command level with a deterministic stale-latest replay through the real command
and stores, asserting `WorkflowDefinitionVersionConflictException` and zero persistence, and adds
the post-composition `Replace` registration test for `IDraftOriginator` so the workflows
extension-point catalog's replacement claim is fully covered. The originating reviewer re-verified
the remediation and declared the axis clean at `587334cec`, which is therefore the final reviewed
US1 candidate.

Recorded non-blocking findings, none of which change this candidate:

- Relational Groundwork providers surface a cancellation observed after `CommitAsync` was invoked
  as a plain `OperationCanceledException` instead of
  `DocumentCommitAcknowledgementUncertainException` (MongoDB already maps it). Elsa state still
  converges through marker replay and the deferred-event policy is explicitly best-effort/
  at-most-once, so this is routed upstream as valence-works/groundwork#118 rather than worked
  around here.
- `GroundworkDeleteWorkflowDefinitionPermanentlyCommand` performs its aggregate reads inside the
  atomic stage callback instead of `beforeAttempt`; scope checks and all-or-nothing semantics
  hold, but the pattern deviation carries provider-conditional read-during-write risk and a
  benign concurrent-race `Rejected` surface. Owned by the US2/US3 command-hardening window.
- Draft layout `AdditionalProperties` enters request material via raw JSON text, so a retry that
  re-serializes with different whitespace/key order fingerprints as a conflict; byte-stable
  retries are unaffected.
- The permanent-delete "must be soft-deleted first" guard now throws `InvalidOperationException`
  (previously `ArgumentException`) in both providers; accepted as the more correct classification
  for this pre-release codebase.
- All github-code-quality PR annotations (generic catch clauses, one empty loop body, one manual
  disposal) were triaged acceptable-by-design: cancellation and already-classified
  `DesignPersistenceException` outcomes are re-thrown before every generic wrap and no
  double-wrapping path exists.

Final counts at the reviewed candidate `587334cec`, verified 2026-07-21T01:29+02:00 (CEST):

- persistence core: `43/43`;
- Groundwork querying: `126/126`;
- Groundwork core persistence: `637/637`;
- workflow design Groundwork: `84/84` (82 plus the two remediation tests);
- activity design Groundwork: `68/68`;
- shared design conformance: `96` passed and `1` intentional profile-shape skip;
- SQLite provider leaf: `9/9`;
- temporary EF oracle: `17` passed and `10` explicitly inapplicable skips;
- Groundwork unified host: `23/23`;
- workflow design domain suite: `358/358`;
- activity design domain suite: `490/490`;
- workflow design API: `50/50`; activity design API: `5/5`; workflow publishing API: `381/381`;
- architecture ratchets: `261/261`;
- full `Elsa.Server.slnx` Release build: `0` errors, `147` accepted existing warnings;
- `git diff --check`: clean.

### T037–T049 bounded design query evidence (2026-07-21)

US2 landed on branch `codex/093-bounded-design-queries` (draft PR #919) as reviewable
checkpoints on top of the merged US1 result `4d44cd435`:

- **T037/T038** — provider-neutral workflow and activity query-shape parity suites
  (`WorkflowDesignQueryContractSuite`, `ActivityDesignQueryContractSuite`): equality,
  membership, empty-membership match-nothing, null/empty/present optional values, the
  activity filter's substring description semantics, disjunctive search with ASCII
  case-insensitivity, semantic-version precedence and build-metadata-insensitive
  existence, batching, repeat-read stability, and restart hash parity. `18` scenarios
  green on the SQLite target and `18` on the temporary EF oracle.
- **T039** — `DesignQueryScaleContractSuite`: listing across the 500-row provider page
  stays complete and duplicate-free (501-definition seed), the definition-id batch
  projection is deterministic for duplicated/unknown/oversized inputs with one
  empty-facts row per unknown id, and existence/latest stay consistent with full
  listings. Five new profile scenarios were added to the contract-scenario enum and
  both applicability profiles; the legacy EF oracle declares the two target-only
  scenarios not applicable (it resolves only persisted definitions). SQLite `5/5`;
  oracle `3` passed plus the `2` declared skips.
- **T040** — `DesignQueryPlanContractSuite` pins a nineteen-probe (kind, route,
  operation) matrix over every selected physical entity table; the SQLite leaf
  captures native `EXPLAIN QUERY PLAN` evidence through Groundwork's public
  `IPhysicalDocumentQueryExplainer` and all `19` probes resolve through indexed
  access with no bare table scan. The EF oracle has no Groundwork routes and does
  not subscribe.
- **T041/T047** — the transitional `GroundworkReadStore` and `InMemoryQueryEvaluator`
  were deleted with their tests; both design manifests dropped every `list-all`
  bounded query, by-collection index, collection projected column, and the
  transitional route constants (writers keep stamping the collection payload
  property); the reusable-activity stores and the four Publishing design-kind
  consumers migrated to shaped `In`/equality reads and zero-clause declared
  traversals. Aux activity routes are now index-head sorted so those shapes are
  admissible. The SQLite schema fingerprints advanced accordingly: target
  `eaaf809f763873c2b9b5a750c3b5103b133716020675aca38773dadb0739ef56`, plan
  `7cded8b9102fe778b7746fd4f1d3e94c2fe548b9d52669e742fb69e433b85bdd`
  (Groundwork family unchanged at `0.0.1-preview.77`). US3 later removed the
  duplicate `activity-definition-by-search` physical index (identical
  (display_name, activity_definition_id) key pattern to the display-name index —
  rejected by MongoDB and double-paid on writes by every provider), rebinding the
  activity search route to the display-name index, removed the duplicate ordered
  current-draft index the same way (identical key pattern to drafts-by-definition),
  and bounded the projected searchable column widths (text 256, identity/sort keys
  128) so every compound index key fits SQL Server's 1700-byte limit; the auxiliary
  exhaustive routes additionally moved their deterministic tie-break from the wide
  envelope comparison key onto a bounded projected `entity_id` column (the main
  units' established pattern), and the fingerprints advanced to target
  `cb225b048807efca76cf870674373782b631682a2278bf7fdfebf7c1245cb217`, plan
  `5b90a248cfc36967c5aa2deaf38427398ebf3212d59b9ffd31b22bd30a712fdc`.
  MongoDB's strict save-time projection resolution then exposed that three activity
  management columns project canonical JSON numbers (enum kinds) but were declared
  String: `authority` (`entity.contentAuthority.kind`), `draft_status`
  (`entity.status`), and `version_lifecycle` (`entity.lifecycle`). All three are now
  `Int32` columns with matching `IndexValueKind.Number` residual predicates (the
  manifest's own semantic validator enforces the pairing), advancing the fingerprints
  to target `ed6bb6a165a08b34c8ad5a53da40f57f83ce0d2b67867abfd2e618da68473b8c`, plan
  `73f2004225f6c3ad58f57f807d2d81fcbd26e4d2603a61528c13ce36617197c4`
  (Groundwork family `0.0.1-preview.78`).
- **T042/T046** — `ElsaGroundworkQueryRoutes.MaximumMembershipCount` (`200`) is the
  declared IN cardinality; the translator rejects a larger single membership before
  provider I/O, and `GroundworkMembershipBatches` canonicalizes oversized caller sets
  into deterministic ordinal batches used by the workflow list projection, draft
  document store, and activity version store. Recorded-store evidence proves a
  450-id read issues exactly three 200/200/50 batches per document kind while
  preserving first-seen output order.
- **T043–T045** — the native searchable fields, compound existence/latest routes, and
  index declarations for both design families were already declared by the Phase-2/US1
  physicalization; this slice verified them end-to-end via the T037–T040 suites and
  native plan evidence and added the aux-route index-head ordering plus the
  authoring-state `In` declaration.
- **T048** — the §2.23 coverage ledger rows introduced by this slice are closed in
  `research.md` (transitional reader resolved as deleted; membership cap, canonical
  batching, and load-all replacements covered by direct tests).
- **T049** — `tests/Elsa/Architecture/DesignPersistenceBoundedQueryTests.cs` fails on
  any reintroduction of `InMemoryQueryEvaluator`, `GroundworkReadStore`,
  `list-all`/`by-collection` identities, or `ListAllAsync` in the design and
  Publishing design-consumer sources, and pins the deleted files as absent.

Focused verification at the T048 evidence point, `2026-07-21T13:36+02:00` (CEST):

- persistence core: `30/30`; Groundwork querying: `91/91`; Groundwork core
  persistence: `639/639`;
- workflow design Groundwork: `85/85`; activity design Groundwork: `70/70`;
- shared design conformance: `96` passed, `1` intentional skip; SQLite provider leaf:
  `33/33`; temporary EF oracle: `38` passed, `12` intentionally inapplicable skips;
- Groundwork unified host: `23/23`; workflow design domain: `358/358`; activity
  design domain: `490/490`; workflow design API: `50/50`; activity design API:
  `5/5`; workflow publishing API: `394/394`; architecture ratchets: `263/263`
  (including the new bounded-query gate);
- full `Elsa.Server.slnx` Release build: `0` errors; changed-file format
  verification (`git diff --check`): clean.

The suite-count deltas against the US1 baseline reflect the deleted transitional
reader/evaluator tests (querying 126→91, persistence core 43→30) and the added US2
suites and gates everywhere else. T050 records the exact reviewed candidate and
independent review below when this slice freezes.

### T050 independent review and final US2 evidence (2026-07-21)

Three independent read-only reviewers examined the exact range `4d44cd435..a8dcb5d64`
across (1) load-all migration fidelity and manifest soundness — including the
missing-value row-loss hazard, verified safe because every traversed route-head field
is a structurally required property — plus watermark stability and the fingerprint
re-pin; (2) IN-cardinality/batching correctness and test-objective preservation; and
(3) the new contract suites, plan gate, architecture ratchet, and documentation
truthfulness (every checked factual claim, including the T043–T045 predating claim,
verified against history).

Two majors were found and remediated in `b66c443fa`, each re-verified by its
originating reviewer:

- deleting the transitional reader's suite had orphaned the live
  `BoundedDocumentQueryPager` misbehaving-provider guards — restored as
  `BoundedDocumentQueryPagerTests` (14 facts) with a corrected ledger row;
- the SQLite plan gate proved only "no full scan" — it now also requires the
  certified plan index to be positively used (SEARCH/covering pass naming the exact
  planned identifier), closing the rerouted-index and vacuous-empty-plan holes;
  19/19 probes remain green under the stronger gate.

The same commit routed the authoring-state `ListAsync` through
`GroundworkMembershipBatches` (latent cap-rejection gap, no live callers), tightened
the T049 runtime-manifest exclusion to qualified member references only, renamed the
draft-resolution scenario to what the fixed-clock fixture proves, and removed dead
order constants plus a stale extension-doc link.

Recorded dispositions (accepted by the originating reviewers):

- The corrupt-payload inner-type / bounded-read provider-failure / no-double-wrap
  assertion granularity that was specific to the deleted transitional reader's
  boundary is a limited accepted reduction; the surviving named-store suites keep the
  core Provider/Serialization classification, cancellation pass-through, and
  readiness coverage.
- The T040 probe matrix covers the contract's Bounded Query Catalog rows; the
  management-projection and aux `list-by-*`/retention routes receive their
  provider-plan evidence in the US3 provider matrix (T063) alongside SQL Server,
  PostgreSQL, and MongoDB capture.
- Skip/take paging parity beyond page-crossing completeness is exercised
  structurally by the plan probes today and is a recorded follow-up for the US3
  conformance matrix.

The final reviewed US2 candidate is `b66c443fa`. Verification at that candidate,
`2026-07-21T13:55+02:00` (CEST): Groundwork querying `105/105` (includes the restored
pager guards), workflow design Groundwork `85/85`, activity design Groundwork
`70/70`, shared design conformance `96` passed + `1` intentional skip, SQLite
provider leaf `33/33`, temporary EF oracle `38` passed + `12` declared skips,
Groundwork core persistence `639/639`, architecture ratchets `263/263`, full
`Elsa.Server.slnx` Release build `0` errors, `git diff --check` clean; the T048
domain/API counts are unchanged from the evidence section above.

### T062 provider-materialization ledger closure and one-provider-per-host evidence (2026-07-21)

The single open §2.23 ledger row — SQLite/PostgreSQL/SQL Server/MongoDB provider
registrations, initializers, target compilers, and shell features — is closed against
suites that already exist on the branch rather than by duplicating them: per-provider
registration/composition resolution (`GroundworkRuntimePersistenceRegistrationTests`
Sqlite facts, `SqlServerGroundworkPersistenceRegistrationTests`,
`PostgreSqlGroundworkRuntimePersistenceRegistrationTests`,
`MongoDbGroundworkPersistenceRegistrationTests`, `UnifiedGroundworkHostTests`),
admission-only initializers, and exact target/route evidence (SQLite
`GroundworkTargetBaselineTests` pins the applied target and plan fingerprints at
Groundwork `0.0.1-preview.78`; SQL Server dispatch/stimulus/history route-fit and
`Production_initializer_is_admission_only_and_constructs_the_exact_physical_store`).

The one-provider-per-host guard is proven directly against the reference-host
composition surfaces. A provider-neutral abstract suite
`ProviderLeafConflictContractSuite` lives in the shared `DesignConformance/Tests`
assembly (no provider SDK reference — the `DesignContractSuiteShapeTests` purity audit
stays green) and is subclassed per leaf as
`{Sqlite,SqlServer,PostgreSql,MongoDb}ProviderLeafConflictContractSuite`. Each subclass
composes its provider in the runtime-only, design-only, and combined reference-host
shapes through the production registration methods (`AddGroundwork{Provider}UnifiedPersistence`,
`Add{Provider}GroundworkDocumentStore` + `AddGroundworkRuntimeStores` /
`AddGroundworkWorkflowsDesignStores` + `AddGroundworkActivitiesDesignStores`) and then
composes a second, different Groundwork provider leaf. The guard rejects the second leaf
at registration time (no connection opened, Docker-free) with the established
`SelectGroundworkProviderLeaf` diagnostic — `Conflicting Groundwork provider leaves were
selected: … Select exactly one concrete Groundwork provider leaf per host.` — asserted in
both composition orders (6 branches per provider).

Verification (Release, `2026-07-21`): shared design conformance `96` passed + `1`
intentional skip (purity audit included); SQLite design-conformance leaf full run
`51/51` (45 prior + 6 new conflict branches); the SQL Server, PostgreSQL, and MongoDB
leaves each pass their `ProviderLeafConflict` branches `6/6` without Docker (their
Testcontainers integration suites are unaffected). Production manifests and the SQLite
`GroundworkTargetBaselineTests` fingerprints are unchanged.

## 1. Restore and build

```bash
dotnet restore Elsa.Server.slnx
dotnet build Elsa.Server.slnx -c Release --no-restore
```

Expected: zero build errors. New warnings introduced by the feature are failures; existing accepted advisories must be recorded separately.

## 2. Run focused adapter suites

```bash
dotnet test tests/Elsa/Workflows/Design/Persistence/Groundwork/Tests/Elsa.Workflows.Design.Persistence.Groundwork.Tests.csproj -c Release --no-build
dotnet test tests/Elsa/Activities/Design/Persistence/Groundwork/Tests/Elsa.Activities.Design.Persistence.Groundwork.Tests.csproj -c Release --no-build
dotnet test tests/Elsa/Persistence/Groundwork/UnifiedHost/Tests/Elsa.Persistence.Groundwork.UnifiedHost.Tests.csproj -c Release --no-build
```

Expected: registration, serialization, command, atomic rollback, and unified-host scenarios all pass.

### T063 four-provider matrix through the reference-host compositions (2026-07-21)

The complete conformance suite ran in full on all four real providers at candidate
`94d57a3bf` plus the deployment-shape lane tests, every project green:

| Project | Result |
| --- | --- |
| `DesignConformance.Sqlite.Tests` (contracts + baseline + CLI + evolution + conflict) | 51/51 |
| `DesignConformance.PostgreSql.Tests` (50 contracts + 5 evolution + 6 conflict) | 61/61 |
| `DesignConformance.SqlServer.Tests` (50 contracts + 5 evolution + 6 conflict) | 61/61 |
| `DesignConformance.MongoDb.Tests` (52 contracts/topology + 6 conflict) | 58/58 |
| Shared `DesignConformance.Tests` (neutral suites + shape/purity audits) | 96 passed, 1 intentional skip |
| `UnifiedHost.Tests` (SQLite host, incl. lane-exclusion shapes) | 45/45 |
| `PostgreSql.UnifiedHost.Tests` / `SqlServer.UnifiedHost.Tests` / `MongoDb.UnifiedHost.Tests` | 6/6 each |

Deployment shapes are proven at the reference-host composition surfaces (the
production registration methods the `Elsa.Server` shell features invoke —
registration-time proofs, not a live boot of the app process): the
combined shape runs the full contract matrix through every leaf fixture
(production `AddGroundwork{Provider}UnifiedPersistence` compositions with startup
auto-apply disabled and admission-gated startup); the design-only and runtime-only
shapes are exercised by `ProviderLeafConflictContractSuite` in all four leaves, and
`DeploymentShapeLaneExclusionTests` proves the lane contract directly — a
runtime-only composition registers the Groundwork runtime bridges and the runtime
manifest source but no design store, design command, atomic writer, or design
manifest source; the design-only composition is the mirror image; the combined
composition registers both lanes. No provider-specific semantic, plan, or
composition remediation remained after the strict-projection parity fix — the
provider differences US3 surfaced (MongoDB duplicate index key patterns, SQL Server
index key width, MongoDB numeric enum projections, MongoDB topology gate) were each
remediated in the manifests or proven as intentional topology semantics earlier in
this story and are recorded in the fingerprint history and research reconciliation.

### T066 US3 independent review and final evidence (2026-07-21)

**Reviewed range:** `2faae0d1590d0927f3893f9a0461a0ff85e8e097..60598f7a551e158092ec3c0bd66f219a36a4e77b`
(the 14 US3 commits), by three independent read-only reviewers on distinct axes.
**Final candidate:** `820fe042e` (the reviewed range plus four dispositioned follow-ups below).

Verdicts:

- **Provider parity / physical manifests — PASS** (after disposition). The one blocker —
  "SQLite silently stores over-length projected text where the other providers reject" —
  was founded on a stale Groundwork source checkout and was formally withdrawn: Groundwork
  PR #105 (shipped since `preview.69`, present in the pinned `preview.78`) validates string
  length at the shared pre-I/O projection boundary on every provider
  (`PhysicalProjectionValueValidationException`, diagnostic `GW-PHYSICAL-037`, UTF-16
  code-unit measure). The reviewer's valid sub-finding (no conformance proof existed) is
  remediated by the `AtomicityProjectionOverLimitRejection` scenario (`820fe042e`), green
  9/9 in each provider leaf's atomicity run and explicitly N/A for the legacy EF oracle.
  Numeric enum re-typing, index rebinds, `entity_id` ordinal-collation determinism
  (binary collation on all four providers), index-key byte budgets (≤1536 B < 1700 B),
  and fingerprint pins were all judged sound.
- **Schema operations — PASS.** Readiness guard genuinely inspect-only with no bypass path;
  CLI contract keeps secrets out of argv; evolution seams honest (interruption structurally
  proven pre-completion; collision offline with fingerprint-verified non-mutation; drift
  drops real applied indexes). Advisories remediated in `086e084ae`: the evolved-route
  servable probe now writes-then-reads per its contract, the vacuous destructive-double-apply
  flag was dropped, and the CLI provenance comment reflects preview.78.
- **Test & spec integrity — PASS.** All modified tests judged legitimate contract evolution
  (several strengthened); purity audit and PR-gate CI routing verified to have teeth; tick
  notes and evidence counts verified against code; scope hygiene clean. Advisory remediated:
  quickstart now states the host-shape proofs are registration-time.

Post-range commits, each dispositioned above: `086e084ae` (R2/R3 advisory remediations),
`cc20574a1` (mechanical spec-094 checkpoint/fence evidence rotation to preview.78 required
by the coverage-ledger version ratchet — publisher re-run + tuple-keyed import, no row
status changed), `820fe042e` (over-limit rejection scenario; re-verified by the originating
reviewer, who returned the final PASS).

Final gate matrix at `820fe042e`, all green: full `Elsa.Server.slnx` Release build 0 errors;
architecture 263/263; Groundwork querying 105; workflows-design GW 85; activities-design GW
70; publishing API 394; conformance leaves SQLite 52/52, PostgreSQL 62/62, SQL Server 62/62,
MongoDB 59/59, legacy EF oracle 38 passed + 13 intentional N/A skips, shared suites 96 + 1
intentional skip; unified hosts 45/45 (SQLite base incl. deployment-shape lane exclusion)
and 6/6 on PostgreSQL, SQL Server, and MongoDB.

### T078 US4 full-gate reconciliation (2026-07-22)

At candidate `40b8bd9ce` (+ the doc-reference cleanup committed with this section):

- Full `Elsa.Server.slnx` Release build: 0 errors. `dotnet pack`: 145 packages, 0 errors
  (pre-release dependency warnings only).
- Design EF dependency audit: zero `EntityFrameworkCore`/`EFCore` tokens in workflow/activity
  design source (the only survivors were stale doc-comment references to deleted types, now
  reworded); zero direct or transitive EF packages in either design Groundwork project
  (`dotnet list package --include-transitive`); the regenerated shrink-only ratchet baseline
  carries zero design entries and the scanner asserts every design source project (incl. Api,
  Validations, JavaScript) as an EF-free boundary.
- Architecture 282/282. Focused suites: workflows-design 307/307 (EF-free, rehosted Groundwork
  test host), activities-design 465/465, Groundwork querying 105, shared conformance 96+1,
  SQLite leaf 57/57, base unified host 45/45.
- Provider matrix post-deletion: PostgreSQL 62/62, SQL Server 62/62, MongoDB 59/59; unified
  hosts 6/6 each.
- Elsa.Server composes design persistence exclusively through
  `GroundworkUnifiedPersistenceSqlite` (all four shells files; no design EF feature remains
  anywhere).

### T079 US4 independent review and disposition (2026-07-22)

**Reviewed range:** `1ff3139bd..1194a39a6` (the 9 US4 commits) by three independent read-only
reviewers on distinct axes. **All PASS, zero blockers.**

- **Performance-gate legitimacy — PASS.** Contract items 1–4/6–9 verified byte-identical across
  the amendment; the 19/19 budget verdict independently recomputed from the committed evidence;
  the contamination disclosure verified against the committed pre-remediation comparison; no
  gerrymandered thresholds (round budgets, real headroom, loud unclassified-route failure). The
  reviewer's strongest adversarial angle — budgets authored after the failing data — is disclosed
  by design: the program owner ratified the amendment with the failing measurements in hand, and
  the pre-remediation Groundwork numbers would have failed even the amended budgets, so the code
  fixes, not the amendment, produced the pass. Upstream provenance verified after a stale-clone
  false alarm: groundwork PR #124 merge `ad8ac47c7`, PR #125 merge `b7a31055a`.
- **Test-objective preservation — PASS.** 90 ledger rows + addendum reconciled method-for-method
  against the 93 deletions; all 22 converted tests verified to assert their stated objectives;
  no assertion weakened in the 12 rehosted files; the fresh-operation-key deviation proven
  required (identical resubmission must re-fire the validation gate) with replay covered by the
  atomicity suite; count arithmetic (364→307, 502→465) verified from the diffs.
- **Deletion completeness & core independence — PASS.** Zero design-EF surface at source,
  project, package, and transitive levels; ratchet baseline shrank 484→377 lines with zero design
  entries; the design-lane boundary covers all 19 design source projects with real
  direct-and-transitive teeth; Elsa.Server composes design persistence via Groundwork only;
  preserved EF lanes verified un-orphaned; spec-094 ledger rotations mechanically sound.

**Advisory dispositions (this commit):** actual review verdicts recorded in place of the
pre-written validation claims; the report's reproduction instructions now cite the harness
checkout commit `30ec15491` and the verified upstream merge SHAs; benchmark output directories
gitignored; the docker compose persistence note and the EF extension-point worked-example
reference no longer cite deleted features; ledger addendum attribution made precise. **Deferred
to #899 (its T080–T083 charter):** regeneration of `docs/maps/*` (which still lists the deleted
design EF projects) and the remaining stale extension-point cross-references — recorded here so
the Phase 7 docs audit inherits them explicitly.

### Phase 2 bounded-query substrate evidence (T019)

On 2026-07-20, the canonical in-memory Groundwork test substrate was updated to
validate the selected physical bounded-query declaration before counting provider I/O,
while retaining a legacy-declaration fallback for manifests that have not yet been
physicalized. A physical declaration takes precedence when the transitional manifest
also retains the corresponding legacy declaration, matching provider route binding.
The substrate now rejects undeclared operators, paths, disjunction, ordering, paging,
latest-per-key selection, required residual omissions, unsupported terminal
operations, and physical `Documents` queries without a positive page limit before I/O;
it also observes cancellation before I/O. The legacy-declaration fallback remains limited
to manifests that have not yet been physicalized. The ratified contract requires stable
ordering when the public query shape requests it (for example, latest-version and
catalog routes); it does not add an implicit order requirement to every otherwise
unordered document page.

```bash
dotnet test tests/Elsa/Persistence/Groundwork/Querying/Tests/Elsa.Persistence.Groundwork.Querying.Tests.csproj \
  -c Release --no-restore
dotnet test tests/Elsa/Persistence/Groundwork/Tests/Elsa.Persistence.Groundwork.Tests.csproj \
  -c Release --no-restore
```

#### Stable-candidate verification

The latest tested source/test candidate is
`d8ba8550aaab5bb88fb2af87aa10c40f1547b72e`. Verification completed at
`2026-07-20T13:11:38+02:00` (CEST, `Europe/Amsterdam`) with these exact results:

- `dotnet test tests/Elsa/Persistence/Groundwork/Querying/Tests/Elsa.Persistence.Groundwork.Querying.Tests.csproj -c Release --no-restore`
  — passed `84/84` (`0` failed, `0` skipped).
- `dotnet test tests/Elsa/Persistence/Groundwork/Tests/Elsa.Persistence.Groundwork.Tests.csproj -c Release --no-restore`
  — passed `627/627` (`0` failed, `0` skipped).
- `dotnet test tests/Elsa/Workflows/Design/Persistence/Groundwork/Tests/Elsa.Workflows.Design.Persistence.Groundwork.Tests.csproj -c Release --no-restore`
  — passed `52/52` (`0` failed, `0` skipped).
- `dotnet test tests/Elsa/Activities/Design/Persistence/Groundwork/Tests/Elsa.Activities.Design.Persistence.Groundwork.Tests.csproj -c Release --no-restore`
  — passed `48/48` (`0` failed, `0` skipped).
- `dotnet test tests/Elsa/Secrets/Tests/Elsa.Secrets.Tests.csproj -c Release --no-restore`
  — passed `88/88` (`0` failed, `0` skipped).
- `dotnet test tests/Elsa/Workflows/Runtime/Scheduling/Tests/Elsa.Workflows.Runtime.Scheduling.Tests.csproj -c Release --no-restore`
  — passed `74/74` (`0` failed, `0` skipped).
- `dotnet test tests/Elsa/Workflows/Design/Tests/Elsa.Workflows.Design.Tests.csproj -c Release --no-restore`
  — passed `354/354` (`0` failed, `0` skipped).
- `dotnet test tests/Elsa/Activities/Design/Tests/Elsa.Activities.Design.Tests.csproj -c Release --no-restore`
  — passed `490/490` (`0` failed, `0` skipped).
- `dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj -c Release --no-restore`
  — passed `251/251` (`0` failed, `0` skipped).
- `dotnet build Elsa.Server.slnx -c Release --no-restore -p:WarningsNotAsErrors=NU1603`
  — succeeded with `0` errors and `41` existing warnings.

The architecture ratchet used the complete repository assets restored earlier in the
work unit. The documentation-only follow-up changes only this quickstart. The commands
above are not rerun on that documentation commit; their evidence remains attached to
the tested source/test candidate SHA stated above.

## 3. Run the shared contracts and four-provider black-box suites

The shared project contains provider-neutral contracts and their behavioral harness:

```bash
dotnet test tests/Elsa/Persistence/Groundwork/DesignConformance/Tests/Elsa.Persistence.Groundwork.DesignConformance.Tests.csproj -c Release --no-build
```

The implementation phase adds each concrete fixture in a separate leaf test project. Those projects
reference the shared contract project and may reference only their own provider SDK:

```bash
dotnet test tests/Elsa/Persistence/Groundwork/DesignConformance/Sqlite/Tests/Elsa.Persistence.Groundwork.DesignConformance.Sqlite.Tests.csproj -c Release --no-build
dotnet test tests/Elsa/Persistence/Groundwork/DesignConformance/SqlServer.Tests/Elsa.Persistence.Groundwork.DesignConformance.SqlServer.Tests.csproj -c Release --no-build
dotnet test tests/Elsa/Persistence/Groundwork/DesignConformance/PostgreSql.Tests/Elsa.Persistence.Groundwork.DesignConformance.PostgreSql.Tests.csproj -c Release --no-build
dotnet test tests/Elsa/Persistence/Groundwork/DesignConformance/MongoDb.Tests/Elsa.Persistence.Groundwork.DesignConformance.MongoDb.Tests.csproj -c Release --no-build
```

Required provider-specific fixtures:

- SQLite, including close/reopen restart;
- SQL Server container;
- PostgreSQL container;
- MongoDB replica-set/sharded transaction fixture;
- MongoDB standalone negative-readiness fixture.

Expected: identical result hashes and domain outcomes for all public design stores/commands; isolation, OCC, atomic rollback, ambiguous acknowledgement, cancellation, restart, and schema drift scenarios pass.

The provider-specific projects must also boot the actual `src/Apps/Elsa.Server/` reference host for
this composition matrix, rather than proving only isolated fixtures:

| Shape | SQLite | SQL Server | PostgreSQL | MongoDB |
|---|---:|---:|---:|---:|
| Design-only | required | required | required | required |
| Runtime-only | required | required | required | required |
| Combined design + runtime | required | required | required | required |

Design-only and combined execute representative design create/read/update/delete/version flows plus readiness before traffic. Runtime-only proves design features are absent while runtime persistence remains operational. Each cell restarts the host and retains one host-level provider selection.

## 4. Prove bounded execution

Run the scale dataset/query suite with plan capture enabled. Evidence must cover every row in [the bounded query catalog](contracts/design-persistence-contract.md#bounded-query-catalog).

Expected for each provider/query:

- the declared query identity resolves to one certified handler;
- scope and document-kind discrimination are present;
- the intended physical entity table/index is selected;
- selective queries do not scan a shared collection or load all documents into application memory;
- count/any/first/latest operations remain server-side;
- unsupported shapes fail before provider I/O.

Store raw plan artifacts under the benchmark evidence directory defined by issue #646; do not paste provider plan blobs into this spec.

## 5. Validate schema operations

Build the assembly containing the unified `IPhysicalSchemaManifestSource`, then install the exactly matching local Groundwork tool version.

```bash
dotnet tool restore
dotnet groundwork --version
dotnet groundwork validate \
  --manifest-assembly ./src/Apps/Elsa.Server/bin/Release/net10.0/Elsa.Server.dll \
  --manifest-type ElsaGroundworkSchema \
  --provider sqlite \
  --offline \
  --output json
```

Repeat live `plan`, `status`, `validate`, and `apply --safe` for `sqlite`, `sqlserver`, `postgresql`, and `mongodb` using `--connection-env`. Accept exit code `0` or `2` for the plan gate; require `0` after safe application and live validation.

Expected:

- deterministic manifest/route/plan fingerprints;
- resolved names follow host policy then provider normalization;
- projected-field backfills are restart-safe;
- schema drift or unsupported topology yields a blocking diagnostic;
- no command silently mutates on validate/status.

The exact manifest type name is finalized during implementation and this guide must be updated if it differs from `ElsaGroundworkSchema`.

## 6. Capture EF-oracle and physical-form evidence

Using the fixed workload from issue #646, run identical seeds, payloads, query shapes, concurrency, and result hashing against:

1. temporary EF normalized design tables;
2. Groundwork shared documents plus linked indexes;
3. Groundwork dedicated document tables;
4. Groundwork physical entity tables.

The 1K dataset is the required correctness/smoke scale, 100K is the required acceptance scale for every workload and mandatory provider, and 1M is required for every scale-bearing query/form comparison on every mandatory provider. An architect-approved workload exclusion must be recorded before timing; machine capacity does not silently waive a scale.

Run every row in the [Benchmark Acceptance Catalog](contracts/design-persistence-contract.md#benchmark-acceptance-catalog). For each measured case, run three independent processes after one untimed warm-up per process. Each measured process must complete at least 100 operations and 30 seconds of steady-state work. Retain raw per-operation samples, fixed seed, payload hash, result hash, provider/server settings, machine metadata, allocation, round trips/database work, storage, write amplification, migration/backfill cost, and native plans. Compute per-run p50/p95/p99 and throughput, use the median of the three runs for the EF ratio gates, and report 95% bootstrap confidence intervals for form comparisons. Apply gates per catalog row; do not use a workload aggregate to hide a failing operation.

Exit gate:

- correctness before timing;
- p95 `<= 1.25x` EF;
- throughput `>= 80%` EF;
- p99 `<= 2x` EF;
- selected entity forms improve median p95 or median throughput by at least 10% over each other Groundwork form at both 100K and 1M, with the improvement direction present in all three runs and the 95% bootstrap confidence interval excluding zero;
- same-provider EF ratios are required wherever an EF oracle exists; MongoDB records its absolute baseline and must pass correctness, bounded-plan, and form-selection gates without a fabricated EF comparison.

## 7. Run design behavior and architecture suites

```bash
dotnet test tests/Elsa/Workflows/Design/Tests/Elsa.Workflows.Design.Tests.csproj -c Release --no-build
dotnet test tests/Elsa/Activities/Design/Tests/Elsa.Activities.Design.Tests.csproj -c Release --no-build
dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj -c Release --no-build
```

Expected: all preserved domain-objective tests pass; core-provider boundaries and the design-EF removal ratchet pass.

The exact stable-candidate results for all three commands are recorded in
[Stable-candidate verification](#stable-candidate-verification).

## 8. Audit design EF removal

After the evidence gates pass and the EF design projects are removed:

```bash
rg -n 'EntityFrameworkCore|EFCore|DbContext' \
  src/Elsa/Workflows/Design/Persistence \
  src/Elsa/Activities/Design/Persistence \
  tests/Elsa/Workflows/Design \
  tests/Elsa/Activities/Design

dotnet list src/Elsa/Workflows/Design/Persistence/Core/Elsa.Workflows.Design.Persistence.Core.csproj package --include-transitive
dotnet list src/Elsa/Activities/Design/Persistence/Core/Elsa.Activities.Design.Persistence.Core.csproj package --include-transitive
dotnet list src/Elsa/Workflows/Design/Persistence/Groundwork/Elsa.Workflows.Design.Persistence.Groundwork.csproj package --include-transitive
dotnet list src/Elsa/Activities/Design/Persistence/Groundwork/Elsa.Activities.Design.Persistence.Groundwork.csproj package --include-transitive
```

Expected: the search has no design EF implementation/mechanism hits, and none of the four retained projects resolves a `Microsoft.EntityFrameworkCore*` package. Domain documentation may mention the historical migration only where explicitly allowed by the final architecture guard.

## 9. Full verification and handoff

```bash
dotnet test Elsa.Server.slnx -c Release --no-build
git diff --check
```

Refresh the narrowest dependency and extension-point maps, review the generated findings, run an independent FR/SC audit against exact HEAD, and attach:

- test summaries and provider versions;
- schema fingerprints and CLI outputs;
- query-plan evidence index;
- benchmark raw summaries and decision;
- direct/transitive dependency audit;
- final commit and PR/check links.

Issue #641 closes only after the merged `main` state contains the removal and all evidence remains reproducible.
