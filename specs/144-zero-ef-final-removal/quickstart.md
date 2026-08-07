# Quickstart: Zero-EF Final Removal

This guide is the execution and evidence checklist for issue #647. Commands that start provider containers or performance runs are intentionally deferred until the program owner releases the resource hold and the machine is otherwise idle.

## 1. Freeze authoritative inputs

```bash
git fetch origin main
git rev-parse origin/main
git status --short
```

Record:

- Elsa remote-main SHA;
- Groundwork released version and exact upstream SHA;
- #642, #643, #646, and #932 state/evidence;
- current EF surface counts;
- current Project 33 states;
- serialized shared-file owner.

## 2. Verify the temporary intake ratchets

The Identity oracle must remain byte-for-byte frozen until its last #646 consumer passes.

```bash
dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj \
  --filter "FullyQualifiedName~Frozen_identity_ef_oracle_matches_its_reviewed_content_fingerprint|FullyQualifiedName~Ef_core_surface_matches_the_reviewed_shrink_only_baseline"
```

Expected before deletion: both pass and the mechanical ratchet matches the reviewed nonzero intake.

## 3. Build the removal and test-retention ledgers

Generate the mechanical EF inventory from:

```bash
jq '.Surface' tests/Elsa/Architecture/Baselines/ef-core-surface.json
rg -l "Microsoft\\.EntityFrameworkCore|UseInMemoryDatabase|UseSqlite|DbContext|EFCore|EntityFrameworkCore" \
  tests --glob '*.cs' --glob '*.csproj'
```

Then add the reachability pass:

- open every shared fixture/host referenced by the direct-token files;
- list every test method that consumes those fixtures/hosts;
- inspect transitive test-project edges;
- record provider-neutral replacements or architect-approved removal in `test-retention-ledger.md`;
- never accept a "covered by" citation without opening the cited test.

## 4. Admit prerequisite evidence

Before deleting an EF family, verify its evidence is merged on remote `main`:

```bash
gh issue view 642 --repo elsa-workflows/elsa-foundation
gh issue view 643 --repo elsa-workflows/elsa-foundation
gh issue view 646 --repo elsa-workflows/elsa-foundation
gh issue view 932 --repo elsa-workflows/elsa-foundation
```

Do not infer performance completion from correctness checks or an unmerged PR.

## 5. Verify provider-host parity

For SQLite, SQL Server, PostgreSQL, and MongoDB:

1. Select one Groundwork provider.
2. Enable all mandatory lanes from [reference-host-matrix.md](contracts/reference-host-matrix.md).
3. Validate schema readiness.
4. Assert every durable contract resolves to Groundwork and no EF service resolves.
5. Exercise dashboard run-health/portfolio.
6. Run the required correctness/tenancy/restart suites.

Server-backed provider commands and performance runs execute only on an idle machine under the program's detached-run/monitor protocol.

## 6. Delete in dependency order

Apply one reviewed slice at a time:

1. diagnostics EF projects/tests after #642 + diagnostics #646 verdict;
2. OpenIddict EF wiring/migrations/tests after #643 + its #646 verdict;
3. frozen Identity EF oracle after the IAM #646 verdict;
4. shared `Elsa.Persistence.EFCore{,.Sqlite}` and EF-only tests/tools;
5. host/solution/package references and central versions.

After each slice, regenerate the temporary baseline using the repository-owned discovery-driven restore driver. It must independently discover every repository project, force-evaluate that exact set, and write a receipt binding the repository/worktree state, project-set fingerprint, dependency-affecting input hashes, and `project.assets.json` hashes:

```bash
bash tools/architecture/restore-zero-ef-certification.sh --force-evaluate \
  --receipt artifacts/zero-ef/restore-receipt.json
```

PowerShell uses the equivalent repository-owned entry point:

```powershell
tools/architecture/restore-zero-ef-certification.ps1 -ForceEvaluate `
  -Receipt artifacts/zero-ef/restore-receipt.json
```

Then regenerate the temporary baseline:

```bash
ELSA_UPDATE_EF_CORE_BASELINE=1 \
  dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj \
  --filter FullyQualifiedName~Ef_core_surface_matches_the_reviewed_shrink_only_baseline
```

Review that the baseline diff only removes entries.

## 7. Install the permanent absolute-zero guard

After every real category is empty:

- delete `ef-core-surface.json`;
- remove the update switch and baseline save/compare path;
- delete the frozen Identity oracle baseline/test with the oracle;
- make the scanner assertion require every category and `ProjectsMissingAssets` to be empty;
- retain fixture tests for every bypass class in [zero-ef-certification.md](contracts/zero-ef-certification.md).

Certification:

```bash
bash tools/architecture/restore-zero-ef-certification.sh --force-evaluate \
  --receipt artifacts/zero-ef/restore-receipt.json
```

Or on PowerShell:

```powershell
tools/architecture/restore-zero-ef-certification.ps1 -ForceEvaluate `
  -Receipt artifacts/zero-ef/restore-receipt.json
```

Then run the guard:

```bash
dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj \
  --filter FullyQualifiedName~Ef_core
```

Expected: every category is empty; the scanner's fresh project discovery exactly matches the receipt; and every project/input/assets binding remains current. The isolated fixtures must also reject a stale-but-present assets file or receipt and a newly discovered project omitted from the receipt.

When a transitive package remains:

```bash
dotnet nuget why path/to/Consumer.csproj Microsoft.EntityFrameworkCore
```

## 8. Complete repository verification

Run the complete solution build/test/pack/startup and the four-provider suites required by the evidence contract. Preserve exact commands and results in this quickstart's implementation evidence section.

Refresh generated maps only after implementation inputs settle:

```bash
bash tools/maps/generate-maps.sh
bash tools/maps/generate-domain-map.sh
bash tools/maps/generate-extension-point-map.sh
bash tools/maps/generate-architecture-reference-map.sh
bash tools/maps/generate-feature-dependency-map.sh
```

Review `docs/reports/maps-v2-findings.md` and `docs/reports/maps-v1-findings.md`.

## 9. Exact-range review and Model B landing

1. Freeze the candidate base/head SHAs.
2. Run three read-only reviewers in parallel on that exact range:
   - correctness/mechanism;
   - evidence integrity;
   - scope/test preservation.
3. Remediate confirmed findings.
4. Have the originating reviewer re-verify.
5. Record verdicts/dispositions here.
6. Mark the draft PR ready only when all checks and reviews pass.
7. Merge with a merge commit.
8. Verify remote `main` contains the merge SHA.
9. Audit the immutable closure ledger for #629, #642, #643, #646, #647, #932, and every other parent-required Project 33 item; apply and verify all required final dispositions; then close #647 and #629 with the verified SHA/evidence.

## Planning Checkpoint Review

This record covers the Spec 144 planning PR only; it does not complete the implementation reviews in T089-T093.

- Frozen range: base `6751087c613b150f4c435d11230dbde00eade37e`, initial candidate `c5aad84681f3dd6e0213f8c6c1c7c86eb19cad14`.
- Correctness/mechanism reviewer `/root/review_elsa1079_correctness`: requested changes because solution-only restore could trust stale-but-present assets and T046/T066 assigned the frozen Identity oracle baseline/test twice. Disposition: `389b656db11bca3c3c6134616587a0a6bd7e0e07` added discovery-driven all-project restore receipts, binding/fixture requirements, and single T046 ownership. Originating-reviewer re-verification: PASS at `389b656db11bca3c3c6134616587a0a6bd7e0e07`; no new correctness blocker.
- Evidence-integrity reviewer `/root/review_elsa1079_evidence`: requested changes for the same stale-assets fail-open, for incorrectly treating Groundwork #50/#25 completion as package capability, and for omitting required child/Project 33 items from parent closure. Disposition: `389b656db11bca3c3c6134616587a0a6bd7e0e07` separated #141/#143 package provenance from #50 performance and #25 completion evidence and added the immutable all-item closure ledger. Originating-reviewer re-verification: PASS at that head; all three findings resolved.
- Scope/test-preservation reviewer `/root/review_elsa1079_scope`: requested changes because the plan allowed T029-T031 before the claimed final shared-file slice, then correctly rejected two partial sequencing fixes until the pre-T011 #646 coverage-ledger import appeared first in the declared order. Disposition: `389b656db11bca3c3c6134616587a0a6bd7e0e07`, `4a7b52ecb682ede4f6bfeca0ff4a3ed30ca80230`, and `519d9b4c098c500c7513afbb89c52baf75fc6ce9` define the prerequisite ledger import, gated host slice, and later solution/package slices in one strict order. Originating-reviewer re-verification: PASS at `519d9b4c098c500c7513afbb89c52baf75fc6ce9`; all 96 tasks remain sequential and well formed.
- Root verification: `git diff --check` passed after each remediation; task inventory remained exactly T001-T096; no stale `specs/141-zero-ef-final-removal` path remained; no container, provider, or performance command was run.

## Implementation Evidence

No task is complete without a one-line evidence note and an immutable command/result, artifact, review, or merge identity.

### Phase 1 intake freeze

#### T001 — authoritative state at intake

Captured `2026-07-29T22:26:10Z` from the clean `codex/647-intake-freeze` worktree.

| Surface | Verified state |
|---|---|
| Elsa remote `main` / intake head | `f769b516598eb807c9528e7c2e72085b346603e8` |
| Groundwork remote `main` | `c48b5a1d04c2664211af1f14d403e3f0391846ca` |
| Groundwork published family / Elsa consumed family | upstream `0.0.1-preview.99`; Elsa `Directory.Packages.props` pins all seven packages to `0.0.1-preview.95` |
| Elsa issues / Project 33 | #629, #642, #643, #646, #647 are Open / In Progress; #932 is Open / Todo |
| Elsa PR state | #1093 is Open / Draft / cleanly mergeable at `a86a08f6fbe48b680d9c6afc358cd0caf690a99a`; it remains a non-production foundation checkpoint pending its recorded owner gate |
| Groundwork issues | #25, #50, #141 Open; #143 Closed |
| Groundwork PR state | no open PR |
| Frozen EF surface | baseline schema 1, SHA-256 `909ff9369a0d2e2defc6f717a87580458a80fdfa2038b279307b49419581f16f` |
| Frozen Identity oracle | baseline SHA-256 `d1f114e701a9df7a66235255533de0306b75b3f08776953a8491f1c89613a7bc`; tree SHA-256 `f9dfeb17c994f17af07203b55498642da79a50ff0161cef252f28bec3a0ad17c` |

Evidence commands: `git ls-remote origin refs/heads/main`, `git rev-parse HEAD`, `rg -n 'Groundwork' Directory.Packages.props`, `gh issue view`, `gh pr list`, and `gh project item-list 33 --owner elsa-workflows --format json --limit 500`. No issue or project state was advanced.

Evidence note T001: the table above freezes both remote heads, package-family drift, all required issue/PR states, and Project 33 states at the recorded timestamp.

#### T002 — categorized EF surface

`ef-removal-inventory.md` (SHA-256 `777473fb1e1ccca9b58eb3a37fc4a3fc819801f14bf98b99ca839afe3ecfee9d`) binds baseline schema 1 and its SHA-256 to 308 entries across all 14 scanner categories. Its deterministic first-match classifier maps every entry exactly once: OpenTelemetry 54, Structured Logs 55, host/package 33, Identity oracle 31, OpenIddict 27, shared EF substrate 28, and test/oracle 80; `unknown` is zero. The inventory includes tools and the temporary benchmark-oracle dependency, records the deletion DAG, and records T063's reconciliation of `EfFreeBoundaryViolations` into the common category map while leaving T064's production-assertion switch open.

Evidence note T002: the checked-in classifier was rerun against `tests/Elsa/Architecture/Baselines/ef-core-surface.json` and returned `total: 308`, `unknown: 0`, with the exact family counts above.

#### T003/T004 — method-level test retention

`test-retention-ledger.md` (SHA-256 `37bdb12c656557da4e22d7fde25854e6219851ddcba7a60f9501b7f9be4cfd6b`) records 299 direct-token method rows from 40 xUnit-bearing files, nine support-only sources, and a disclosed 20-method token-free shared-fixture/host addendum. All 319 method dispositions are preliminary: 136 direct rows are `Preserve`, 128 are `Convert`, 35 are `RemovePending`, and every addendum row is `Convert`. No `RemovePending` row is architect-approved. A broad literal search also finds one surviving Groundwork-only negative assertion that names two already-retired EF feature aliases; the ledger records why it is outside the removal-affected denominator and assigns its audit to T054.

Evidence note T003: independent re-enumeration of every Fact/Theory method in the 40 admitted files returned 299 identities, with zero source-to-ledger differences and zero path/line mismatches; the capture export SHA-256 is `cba7777a71b56e84540c21898d3974e66b0437384ed0146b6b68aa7708a6c311`.

Evidence note T004: fixture tracing proved 10 `TokenEndpointFixture`, one `AspNetCoreIdentityFixture`, and nine `OpenIddictIdentityFixture` token-free methods reach EF; the strict addendum matcher returned 20.

#### T005 — FR/SC evidence ownership

| Requirements | Task and evidence owners |
|---|---|
| FR-001, FR-007 | T009-T017 prerequisite admission and `ef-removal-inventory.md`; T041-T052 dependency-ordered deletion |
| FR-002-FR-005 | T006 and T018-T034; `contracts/reference-host-matrix.md`, unified-host resolution/readiness tests, and provider evidence |
| FR-006 | T002-T004/T016; mechanical inventory plus direct-token/shared-reachability test ledger, including tools and temporary benchmark oracles |
| FR-008 | T010/T044/T074/T078/T096; #643 evidence, OpenIddict removal, decision-map correction, and closure ledger |
| FR-009-FR-011 | T003-T004/T014/T035-T040; method-level retention ledger, opened replacement evidence, and architect-approved removal rows |
| FR-012-FR-013 | T041-T055/T063-T071; source/project/package/alias/initializer cleanup and permanent certification |
| FR-014-FR-020 | T059-T071; omitted/imported/conditional/restored/stale/missing evidence fixtures, all-project restore receipt, absolute-zero guard, and baseline/oracle retirement |
| FR-021 | T076/T081/T085; owning operational documentation, its validation command, final audit, and startup/schema-readiness evidence |
| FR-022 | T032-T033/T057-T058/T069/T082-T087; complete build/test/pack/startup/provider/performance evidence |
| FR-023-FR-024 | T072-T080; constitution, ADR-linked guidance, program records, operational docs, generated maps, and findings disposition |
| FR-025 | T088-T093; three exact-range adversarial reviews, dispositions, and originating-reviewer re-verification |
| FR-026-FR-028 | T078/T094-T096; Model B push/checks/merge/main-presence proof and immutable issue/Project 33 closure ledger |
| SC-001 | T041-T055/T063-T071; zero category counts in the final certification |
| SC-002 | T061/T063-T064/T068-T071; exact project/input/assets receipt binding for every discovered project |
| SC-003 | T003-T004/T014/T035-T040/T057; 100% method-level dispositions and passing EF-independent replacements |
| SC-004 | T009-T010/T012/T018-T034/T058/T086; four-provider all-lanes evidence |
| SC-005 | T011/T087; accepted coverage-ledger verdicts bound to final fingerprints |
| SC-006 | T059-T062/T069; all defined bypass fixtures pass |
| SC-007 | T055/T057/T069/T082-T086; search, restore, build, test, pack, and startup audits |
| SC-008 | T088-T093; no unresolved review blocker |
| SC-009 | T094-T096; merge commit present on remote `main` before closure |
| SC-010 | T072-T080/T096; governance, maps, issues, and Project 33 converge on the verified state |

Audit disposition: the mapping found four weak assignments. T002 now explicitly includes tools and temporary benchmark oracles; T020 requires an exact negative readiness diagnostic; T021 rejects silent omission/in-memory/EF fallback; T054 explicitly covers aliases/initializers; and T076 requires a documentation validation command. No task cycle was introduced.

Evidence note T005: all FR-001 through FR-028 and SC-001 through SC-010 appear exactly once in the grouped ownership table, and every audit gap was assigned to an explicit task before completion.

#### T006 — reference-host declaration matrix

`contracts/reference-host-matrix.md` (SHA-256 `cf79d5a216c6870425279bf193ebe546ce5f84e1074f7c202d972e3270d240d4`) now freezes the four maintained configuration declarations plus the absent SQL Server/MongoDB rows at the intake head. Default SQLite and the production overlay remain Not ready because Identity/OpenIddict retain EF. The baseline and Docker/PostgreSQL files are EF-free only by omission and make no all-lanes claim. The target rule names only existing unified provider/diagnostics/Identity/dashboard features and deliberately waits for #643 to catalog the exact OpenIddict Groundwork feature.

Evidence note T006: every current row is marked Not ready, minimal, or not created; no declaration is presented as DI resolution, schema, startup, provider, or performance evidence.

#### T007 — serialized shared-file ownership

The #647 control-room root is the sole serializer. The admitted order is:

1. prerequisite #646 mechanical `coverage-ledger.json` import before T011;
2. T029 `shells.json`;
3. T030 `shells.Production.json`;
4. T031 `Elsa.Server.csproj`;
5. T051 `Elsa.Server.slnx`;
6. T052 `Directory.Packages.props` and remaining project package references.

No Phase 1 worker may edit these files, and no later slice may advance past its prerequisite gate.

Evidence note T007: the six-step serialization order above assigns one root owner and preserves the prerequisite coverage-ledger import before any #647 host/package shared-file edit.

#### T008 — temporary ratchet verification

- Initial exact test command failed closed as designed because the fresh worktree did not yet contain assets for the complete repository restore. The frozen Identity test passed; the EF-surface test refused to compare and named every missing project instead of reporting phantom shrinkage.
- `dotnet restore Elsa.Server.slnx --force-evaluate` completed successfully. Existing restore warning: `NU1510` for `Microsoft.Extensions.DependencyInjection` in the architecture test project.
- Rerun: `dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~Frozen_identity_ef_oracle_matches_its_reviewed_content_fingerprint|FullyQualifiedName~Ef_core_surface_matches_the_reviewed_shrink_only_baseline"` — Passed 2, Failed 0, Skipped 0, duration 42 seconds. No database-server container or performance process ran.

Evidence note T008: after the required complete solution restore, both the shrink-only surface ratchet and frozen Identity-oracle fingerprint passed on the intake head.

#### T061/T063/T068 — fail-closed all-project restore receipts

The additive certification slice landed as three implementation checkpoints on draft PR #1104:
`052ea6e99896cab0a06375b35c1073164098a9b8` added the typed receipt validator and bypass
fixtures; `36c36910c034fadfb2fc1a0b027e5fbd838017aa` added sanitized `n/246` driver progress;
and `f7bcbd5795d2413a7855c53afb54e9d8270c8951` fixed clean-worktree byte hashing and added
the PowerShell self-check. Exact-range review then found two blockers: the PowerShell entry point
used APIs unavailable to Windows PowerShell 5.1, and the scanner trusted any safe repository file
whose path/hash a receipt claimed as its restore driver. Commit
`469945261af6c2f9142e795a829d0920ecb2a036` restricts driver identity to the two repository-owned
entry points, aligns config-path case handling with the host filesystem, adds a forged-driver
fixture, and replaces the incompatible PowerShell APIs. The temporary shrink-only baseline
assertion and its update switch remain intact; T064–T067 are not claimed.

Container-free test evidence:

- `dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj --no-restore --filter "FullyQualifiedName~receipt" -m:1 --disable-build-servers` — Passed 6, Failed 0, Skipped 0.
- `dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj --no-build --no-restore --filter "FullyQualifiedName~Elsa.Architecture.Tests.EfCoreSurfaceRatchetTests" --disable-build-servers` — Passed 43, Failed 0, Skipped 0.
- Bash parser plus NDJSON assembly self-check passed; PowerShell parser/help plus the explicit empty-byte SHA-256 self-check passed.
- A disposable clean one-project forced-restore smoke ran each driver twice against the same receipt
  path. It proved project/input/assets emission and existing-receipt replacement. The first
  remediated PowerShell repeat exposed that `File.Replace(..., $null)` did not preserve a null
  backup path through PowerShell binding; the final implementation uses a same-directory temporary
  backup with guaranteed cleanup and passed both repeated runs.

Repository-wide forced-restore evidence:

| Driver | Exact head | Result | Receipt SHA-256 |
|---|---|---|---|
| Bash | `469945261af6c2f9142e795a829d0920ecb2a036` | 246/246; C# scanner `isValid=True`, zero failures; clean worktree | `173a137024e22c89b81027c2a35f33f327c6500d0ddbad57c2825513d646f431` |
| PowerShell | `469945261af6c2f9142e795a829d0920ecb2a036` | 246/246; C# scanner `isValid=True`, zero failures; clean worktree | `59152c99d1ab72f1371230a1e767e6db47d112be6b6a4b21e39ea545c1275b2f` |

Both receipts use SDK `10.0.300`, bind worktree-status SHA-256
`e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855`,
and contain 246 unique projects plus 251 dependency-affecting inputs. Their project-set SHA-256 is
`72f350c264727cb46d2d603b2d14f331f2ddd44ed556dd7eaa00db3fb91afdda`;
their input fingerprint is
`65b006f239a5e0c12c9fe5e3bce89dfc0b45e0ff6e4a32571e8cb2a65085c458`.
A tuple comparison over every `(project path, assets SHA-256)` returned exact equality for all
246 projects. Fresh independent filesystem discovery also returned 246 projects.

Honest failure disclosure and disposition:

- The first detached Bash launch was reaped by the command host before driver execution: empty log, no receipt, clean worktree. A persistent lightweight parent session corrected the launch mechanism.
- The first persistent Bash attempt was intentionally terminated after proving that its empty log could not satisfy the required monitor workflow. Both drivers then gained sanitized per-project progress; the subsequent Bash run passed.
- The first repository-wide PowerShell run restored 246/246 but emitted no receipt because an empty clean-worktree byte array was unrolled to `null`, making `ComputeHash(null)` ambiguous. `f7bcbd579` preserves empty byte arrays, handles `null` defensively, adds the canonical empty-hash self-check, and passed both the clean fixture and the full rerun.
- The correctness reviewer proved that `$IsWindows`, `Path.GetRelativePath`,
  `ProcessStartInfo.ArgumentList`, and the three-argument overwrite form of `File.Move` were not a
  valid Windows PowerShell 5.1 contract. `469945261` removes all four dependencies, declares
  `#requires -Version 5.1`, and passed parser/help/self-check plus the repeated clean-repository
  smoke. Native Windows 5.1 execution remains the originating reviewer's re-verification concern.
- The scope reviewer forged an otherwise hash-correct receipt that named `NuGet.config` as the
  restore driver. `469945261` adds an exact two-entry driver allow-list and a covering rejection
  fixture; the complete 43-test ratchet class passes.
- The first repeated PowerShell smoke after those changes reached receipt replacement and failed
  because a null backup argument was rebound as an empty path. The same commit's final tree uses a
  unique same-directory backup path, removes it in `finally`, and passed two consecutive real
  replacements. No failed run produced accepted evidence.

Evidence note T061: stale/missing/changed/unbound receipt and assets cases, restored-transitive
evidence, and project-set drift are covered by the green 43-test ratchet class.

Evidence note T063: scanner discovery is solution-independent; all 14 categories are exposed; and
the real Bash and PowerShell receipts each returned `isValid=True` under the scanner.

Evidence note T068: both repository-owned entry points force-restored all 246 independently
discovered projects and produced identical project-set, input, and assets identities. No database
server container, provider suite, or performance run was started.

##### PR #1104 additive-slice exact-range review

This review checkpoint covers
`f769b516598eb807c9528e7c2e72085b346603e8..1066ef6ffb5269519aa4618f9090c15123df1d32`.
It reviews the intake-freeze and additive restore-certification slice only; it does not complete the
final implementation review tasks T088–T093.

- Correctness/mechanism reviewer `/root/review_1104_correctness`: **PASS**. Originating finding
  disposition: **resolved**. The reviewer verified the Windows PowerShell 5.1-compatible API
  replacements, clean-worktree byte preservation, atomic receipt replacement, exact driver
  allow-list, and forged-driver fixture. Static inspection found no remaining 5.1/.NET Framework
  incompatibility; no native Windows PowerShell 5.1 transcript was available, so that environment
  execution is not claimed.
- Evidence-integrity reviewer `/root/review_1104_evidence`: **PASS**. The reviewer independently
  reproduced both receipt hashes; verified the clean head, SDK, 246/251 counts, project/input
  fingerprints, every input blob, and cross-driver project/assets tuples; and confirmed that the
  receipts certify implementation head `469945261`, not the later documentation-only head. Raw
  full-run logs were not retained, so historical execution is not claimed to be independently
  replayable from artifacts alone.
- Scope/test-preservation reviewer `/root/review_1104_scope`: **PASS**. Originating finding
  disposition: **resolved**. The reviewer proved that no alternate path, case variant, or hash-only
  route bypasses the two-entry driver allow-list and opened the covering substitution fixture.
  Bash's exact canonical case policy can fail closed under a Windows alias but cannot greenwash.
  A Windows-only case-alias integration fixture remains a non-blocking coverage suggestion.

#### T015 — worktree contamination check

- #647 intake worktree started clean at `f769b5165`; only the explicit Spec 144 intake artifacts are being changed.
- Elsa main checkout is clean at local `8d519d4eb` and 127 commits behind `origin/main`; it was not edited.
- The historical Spec 144 planning worktree is clean at `6cdd887c1` on a gone branch; it was not edited.
- Groundwork primary has unrelated branch state (`codex/diagnostic-stream-auto-apply` at `a2dc8a410`) and was treated as read-only.

Evidence note T015: target, primary Elsa, historical planning, and Groundwork checkouts were inspected separately; no worker edit escaped the dedicated #647 worktree.

#### T014/T016 — replacement audit and deletion DAG

T014 opened all four candidate Groundwork replacement tests beside their original EF-backed
methods. Three are partial and one is rejected: neither OpenTelemetry candidate covers the complete
write/drain plus query objective, the structured-log complex-field candidate omits message and
drain-lifecycle coverage, and the restart candidate does not exercise trim-to-zero or stale-cursor
rejection. `test-retention-ledger.md` records the exact differences. No `RemovePending` row is
admitted for removal.

T016 is frozen in `ef-removal-inventory.md`: diagnostics, OpenIddict, and the Identity oracle are
independent leaves gated by #642/#643/#646 evidence; the shared EF substrate follows all three;
host/solution/package edits are serialized; and the permanent guard/baseline retirement comes only
after actual absolute zero.

Evidence note T014: all cited replacement pairs were opened; verdicts are Partial, Partial,
Rejected, and Partial, with no proven objective-equivalent replacement and no approved removal.

Evidence note T016: every scanner family has named gate IDs, earliest task IDs, and a dependency
edge in the recorded DAG.

### Phase 2 prerequisite admission — current blocking record

This table records the exact intake status; it does **not** pass T009–T013 or admit an EF-family
deletion:

| Task / gate | Status at Elsa `f769b5165` | Retained evidence | Missing admission evidence |
|---|---|---|---|
| T009 / #642 diagnostics | **Pending** | #1048 merge `3cb79c3940e0d665a3156cea33d1e9550163f1ed`; #1072 merge `4a5f517f293b54c370a5d0073ce7424f685bb8c5`; #1098 merge `88717fa00eda7cf95fb6a00019ce68fa0504fd83` | Exact-preview.95 four-provider/grouped-reducer promotion, #646 verdict, and final test dispositions; Spec 139 T050–T055/T057 remain open. |
| T010 / #643 OpenIddict | **Blocked** | Draft foundation PR #1093 at `a86a08f6fbe48b680d9c6afc358cd0caf690a99a`; frozen 145-member and 55-objective inventories | Public stores, production registration, four-provider black-box evidence, exact-range reviews, merge, and the still-open Groundwork #141 contract. |
| T011 / #646 performance | **Blocked** | 35 coverage-ledger rows and historical `host-selection-all35` composition evidence | Zero `performanceVerdict` objects and zero performance-complete/ready rows; the ledger names preview.88 while Elsa consumes preview.95; diagnostics provider-evidence arrays are empty; #50 has no accepted immutable baseline. |
| Frozen Identity oracle | **Freeze passes; deletion not admitted** | Baseline SHA `d1f114e701a9df7a66235255533de0306b75b3f08776953a8491f1c89613a7bc`; tree SHA `f9dfeb17c994f17af07203b55498642da79a50ff0161cef252f28bec3a0ad17c` | The IAM #646 verdict and durable import must pass before the oracle can be removed. |
| T012 / #932 dashboard | **Blocked** | Current source proves only SQLite/PostgreSQL dashboard dialects | SQL Server dialect, MongoDB aggregation, unified registration, host acceptance evidence, and final issue/Project disposition; no non-support amendment is ratified. |
| T013 / Groundwork upstream | **Blocked** | #143 closed at `f8374a9807e820824e0f03bd4a6140606ac4233b`; #141 prerequisite PR #148 merged at `b5d59f1abb080d2ae2d2d1f1bd0505da11f79f80`; Groundwork main/preview.99 at `c48b5a1d04c2664211af1f14d403e3f0391846ca` | #141 remains open; #50 remains open without an accepted immutable baseline; parent #25 remains open; no separately named completion amendment exists. |

Evidence note T009–T013: all five prerequisite tasks remain unchecked. Historical preview.86/.88
evidence and partial package checkpoints are not exact-family admission evidence.

Currency note (2026-08-03), T011 row only: the recorded package mismatch has since resolved and been
replaced by a different defect. The row's statement was accurate at `f769b5165` — the ledger declared
`0.0.1-preview.88` while `Directory.Packages.props` pinned `0.0.1-preview.95`. Both now declare
`0.0.1-preview.103`, so there is no version mismatch. The blocker recorded here on 2026-08-03 — that
`specs/094-harden-groundwork-stores/versions/` contained only `preview.81`, `.86`, and `.88`, leaving the
ledger's declared version with **no imported evidence generation at all** — was **resolved on 2026-08-06**
by publishing and importing the `preview.103` checkpoint/fence generation at Elsa commit `78e648996` (see
the 2026-08-06 checkpoint below). That closes one of the row's four findings. The other three are
unchanged and still current: zero `performanceVerdict` objects, empty diagnostics provider-evidence
arrays, and no accepted immutable #50 baseline. **T011 remains Blocked**, and the generation import did
not advance any row status.

Currency note (2026-08-04), T012 row: the two missing dashboard providers are now implemented, so the
row's "Missing" column is out of date. SQL Server is a third `GroundworkRunHealthDialect` member with
`JSON_VALUE`/`TRY_CAST`/`datetimeoffset` handling and T-SQL `TOP`/CTE syntax; MongoDB is a separate pair
of data-source classes over the aggregation pipeline, because none of the `DbConnection`-and-SQL
machinery transfers to it. Both are wired into their unified registrations — each of which was
**missing the Dashboard project reference entirely**, so neither could have composed the dashboard even
with dialect support present. Each provider has a container-backed test leaf deliberately routed to
nightly, mirroring the SQLite fixtures' data and assertions so the four providers are comparable.

Running SQL Server against a real container immediately caught a defect no SQLite-only test could see:
SQLite and PostgreSQL both accept `GROUP BY` on a select-list alias and T-SQL does not, so the
top-failures query failed outright. Fixed by grouping on the full expression, which is valid in all three.

What the row still correctly requires is unchanged: **host acceptance evidence and the final issue/Project
disposition for #932 are not supplied by this work.** The gate remains open until those land; only the
"no implementation exists" half of the blocker is resolved.

Currency note (2026-08-03), T010 row: the row describes PR #1093 as a "Draft foundation PR". It is
**merged** (`gh pr view 1093` → `state: MERGED`, 2,997 additions), so the OpenIddict Groundwork
foundations — storage manifest, records, serializer/codec, generic-query rejection, session factory,
failure mapper — are on `main`. What remains open is unchanged and is the substantive part: all four
store implementations (145 members across `IOpenIddictApplicationStore`, `IOpenIddictAuthorizationStore`,
`IOpenIddictScopeStore`, `IOpenIddictTokenStore` per
`specs/106-openiddict-groundwork-stores/contracts/openiddict-member-ledger.md`), the production
registration, the atomic-mutation and relationship coordinators, four-provider evidence, and the still-open
upstream Groundwork contracts #141/#143. Spec 106 Phase 3 onward is entirely unstarted.

Currency note (2026-08-03), T009 row: #642's remaining work is **not code**. The Groundwork diagnostics
stores are complete — no stub, TODO, or `NotImplementedException` anywhere under
`src/Elsa/Diagnostics/*/Persistence/Groundwork/` — and spec 139 has T001–T049 and T056 checked. What is
open is an evidence chain: T050/T051 import a #646 verdict that does not yet exist, T057 runs a
four-provider certification at the current Groundwork version (as of 2026-08-06 a `preview.103` generation
exists, but it is the 36-record checkpoint/fence slice only — it certifies nothing in the diagnostics
family), and T053–T055 then delete the EF projects mechanically. Treating T009 as an engineering task will
mis-plan it.

Oracle-availability note, T011 scope: #646's EF comparison can only cover contracts that have an EF
implementation. That set is `IStructuredLogStore`, `IOpenTelemetryStore`, the four Elsa IAM contracts,
and the two ASP.NET Core Identity framework contracts — SQLite only, because EF Core has no PostgreSQL
or SQL Server wiring in `src/`. No runtime-family row has ever had an EF comparand, so runtime rows
cannot receive an EF-ratio verdict and must be graded on absolute budgets and restart-recovery
evidence instead. See [the zero-EF decision map](../../docs/decision-maps/zero-ef-groundwork.md)
(`oracle-inventory`) for the full inventory and its derivation.

### T018/T019 — #932 dashboard acceptance intake

Audit head: Elsa `origin/main` `f769b516598eb807c9528e7c2e72085b346603e8`, 2026-07-30.

- `GroundworkRunHealthDialect` contains exactly `Sqlite` and `PostgreSql`.
  `GroundworkWorkflowRunHealthDataSource` and `GroundworkWorkflowPortfolioDataSource` use that
  two-way relational dialect model; no MongoDB aggregate implementation exists.
- SQLite and PostgreSQL unified registration add both dashboard data sources. SQL Server and
  MongoDB unified registration add neither.
- The sole concrete run-health provider test,
  `SqliteAdapterPagesPastOneHundredExecutionsAndReturnsTheirIncidentsExactly`, is SQLite-only. It
  persists 125 executions and one incident on `run-124`, then expects started count 125,
  incident-bearing count 1, and incident count 1.
- The companion concrete portfolio provider fixture,
  `GroundworkSqliteReturnsTheCompleteIdenticalPortfolioFixture`, is also SQLite-only. It expects
  base counts 105/50/30, 30 drafts, and non-null state for every draft.
- The provider-neutral service oracle additionally expects, for 125 authorized runs, 100
  succeeded, 25 failed, two incident-bearing runs, three incidents, one running run, and
  deterministic top-failure counts 13/12. It is not SQL Server/MongoDB execution evidence.
- #932 is Open / Project 33 Todo with no implementation PR. No SQL Server/MongoDB dialect,
  deterministic aggregation, unified registration, or provider acceptance evidence is merged.

Gate disposition: **Blocked**. T019's verification action is complete, but the #932 gate it records
prevents T022–T026/T029–T034 from passing until #932 lands a real SQL Server dialect plus MongoDB
aggregation and host-level acceptance evidence, or the program owner separately ratifies an
explicit non-support amendment.

Evidence note T018: every existing concrete and provider-neutral dashboard oracle was opened; the
absence of SQL Server/MongoDB acceptance tests is recorded rather than converted into an inferred
pass.

Evidence note T019: current production source and unified registrations prove the #932 gap remains;
the mandatory blocker is retained with its exact exit condition.

### T035–T039 readiness audit — no conversion admitted

The method ledger was grouped against current target fixtures before any EF-backed test was moved:

| Conversion task | Rows | Current readiness |
|---|---:|---|
| T035 Identity | 57 | Groundwork store/manager candidates exist, but the 12 Groundwork HTTP rows still compose EF OpenIddict and the frozen Identity oracle remains load-bearing for #646. |
| T036 OpenIddict | 38 | No Groundwork OpenIddict production source or target fixture exists at this head; blocked on #643. |
| T037 diagnostics | 76 | Groundwork SQLite suites exist, but the four T014 objectives remain partial/rejected and four-provider promotion remains blocked on #642. |
| T038 shared EF substrate | 26 | Current query/save/upsert/WAL/migration tests are EF-mechanism-specific or lack one-to-one Groundwork objective evidence; no removal classification is admitted. |
| T039 Modularity/shared-host | 0 additional (20 disclosed rows already counted in T035/T036) | The shared-host trace must not create a duplicate method denominator; no removal is defensible. |

The remaining 122 rows are permanent guard/current non-EF target evidence (59) and benchmark/oracle
or protocol evidence (63). The #646 oracle rows remain frozen; protocol/integrity rows remain
preserved. This audit authorizes no deletion or rehome and leaves T035–T039 unchecked.

### T059/T060 — additive anti-bypass fixtures

Four focused tests were added without changing the temporary baseline or scanner behavior:

- an EF project explicitly omitted from `Elsa.Server.slnx` is still discovered, and its direct
  project/static-transitive package edges are reported;
- declared direct and central EF package inputs are both inventoried;
- a real imported props file contributes its EF package to both the importing project and the
  shared-build inventory;
- a conditional EF package applies to the matching project and not an unrelated project.

The existing Windows-style project-reference and shared-build package fixtures remain part of the
same gate.

Evidence note T059: the new omitted-solution test plus the existing Windows-reference test passed
2/2 in Release; the scanner used raw repository project discovery rather than solution membership.

Evidence note T060: the three new dependency-input tests passed within the 4/4 new-test run, and
the existing Windows/static and shared-build fixtures passed 2/2; commands used `--no-restore`,
build servers disabled, and no database-server container.

### 2026-07-31 preview.102 prerequisite checkpoint

Groundwork merge `68e7c344163c199024aed00ccdcaa2deb51ef5bb` published the coherent
`0.0.1-preview.102` package/tool family. The serialized Elsa integration aligns all seven packages,
the tool manifest, and the Spec 094 current-family guard to that release. This family includes the
provider-applied ordering-tail work from PR #154, the provider-internal SQLite materialization proof
from PR #155, and the immutable physical-form baseline registry/control plane from PR #156.

This checkpoint is prerequisite preparation, not deletion admission: the preview.102 four-provider
evidence generation has not yet been published or mechanically imported; Groundwork #50, #25, and
#141 remain open because their remaining controlled matrices and provider obligations have not
passed; T009–T013 and T017 remain unchecked; no coverage-ledger status, performance verdict, EF
bucket, issue state, or Project 33 completion state advances.

### 2026-07-31 preview.103 prerequisite checkpoint

Groundwork merge `b9ba0249eed0a00da9b6d37575f39383c22ae2c9` published
`0.0.1-preview.103`, adding the MongoDB fixed-assignment and transition selector-mirror reopen repair
from PR #157. The serialized Elsa integration aligns the seven package pins, tool manifest, Spec 094
current-family ratchet, and current-version guards. Preview.102 remains an immutable no-generation
checkpoint; preview.103 provider publication and tuple-keyed mechanical import were still required at the
time of this checkpoint and completed on 2026-08-06 (below).

This alignment does not admit EF deletion: it advances no provider-evidence status or performance
verdict, leaves Groundwork #50 and Elsa #642/#643/#646/#647 open, and does not change any EF ratchet
bucket or Project 33 completion state.

### 2026-08-06 preview.103 evidence generation published (T011 evidence record)

T011 requires evidence recorded here. Recording it: the `preview.103` checkpoint/fence generation was
published and mechanically imported at Elsa commit `78e648996` (tree `17acb4c7a`), run identity
`runtime-checkpoint-fence-preview103` — 36 records across sqlite, sqlserver, postgresql and mongodb, now
under `specs/094-harden-groundwork-stores/versions/0.0.1-preview.103/`.

**This does not advance T011, and does not advance any row.** Status counts are unchanged at 30
implemented / 4 externally blocked / 1 planned, and the `performanceVerdict` count remains **zero**. What
it closes is exactly one of the T011 row's four findings — the missing evidence generation.

Two findings from the same session bear directly on T011's remaining scope, and both make it larger than
the task text implies:

1. **The #646 harness could never run.** `matrix` refuses without `--child-command`, and no
   `IBenchmarkAdapter` implementation existed anywhere in the repository — only test doubles. The child
   host was built on 2026-08-06 (`benchmarks/Elsa.Groundwork.StorePerformance.AdapterHost/`) but has **zero adapter leaves registered**, so every workload is still a blocked run.
2. **A `performanceVerdict` cannot be produced at all yet.** `Comparison.Compare` requires two measurement
   sets differing only by *physical form*, and those form labels have no binding in `src/` — Groundwork
   ships one shape per store. Tier C ("compare against the last accepted generation") is also unreachable:
   `Compare` rejects differing commits. See the corrections in
   `specs/094-harden-groundwork-stores/contracts/runtime-absolute-budget-basis.md`.

Consequently a full run of every workload that *has* an executable class would reach at most **14 of the
35 rows**: `recovery-scan` (5 rows), `due-timer-selection` (1), `diagnostics-durable-history` (2) and
`secret-create-read-list` (1) have no workload class at all, and `iam-normalized-lookup-update` (8 rows) is
blocked by the deliberately empty `RatifiedIamProductionMappings`. What *is* reachable from one measurement
set is the Tier B absolute ceiling, since it needs no comparison.
