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

Populate during `speckit-implement`. No task is complete without a one-line evidence note and an immutable command/result, artifact, review, or merge identity.
