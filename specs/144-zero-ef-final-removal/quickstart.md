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

After each slice, regenerate the temporary baseline using a complete evaluated restore:

```bash
dotnet restore Elsa.Server.slnx --force-evaluate

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
dotnet restore Elsa.Server.slnx --force-evaluate

dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj \
  --filter FullyQualifiedName~Ef_core
```

Expected: every category is empty and every discovered project has current restored assets.

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
9. Close #647, then #629, and update Project 33 with the verified SHA/evidence.

## Implementation Evidence

Populate during `speckit-implement`. No task is complete without a one-line evidence note and an immutable command/result, artifact, review, or merge identity.
