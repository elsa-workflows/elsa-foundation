# Quickstart: verify spec 259

Run these from the repository root, on the implementation branch. On Windows use Git Bash and `python`, not `python3`.

## 1. The extraction changed no output (FR-007)

Before removing anything, with the shared library in place:

```bash
dotnet run --project tools/maps/Elsa.Maps.Generator -- all
git diff --stat -- docs/maps/dependency-map.json
```

Expect no diff. On Windows, the local `-- check` falsely reports CRLF staleness. Use `git diff --stat` instead, which normalizes line endings.

```bash
dotnet run --project tools/maps/Elsa.Maps.Generator -- project-graph-self-test
```

Expect exit 0.

## 2. Derived equals generated (FR-003)

```bash
dotnet test Essentials/tests/Versioning/Calculator/Tests --filter "FullyQualifiedName~DerivedDataset"
```

This holds the calculator's git-tree derivation at `HEAD` byte-equal to the working-tree serialization of a clean checkout. It also covers the synthetic `auto`, `committed` and `derived` cases.

## 3. Both sources agree on versions (FR-008; run before the removal commit)

```bash
git fetch origin main publish-state
bash specs/259-derived-dependency-map/validate-dataset-sources.sh 30
```

Expect a table of 30 rows with `same` in the versions/fingerprints column, and exit 0. Paste the table into the PR's gate evidence. Any `differs` row stops the switch. Investigate it with the per-commit dataset diff the script leaves in its scratch directory.

## 4. The file is gone and ignored (FR-010)

After the removal commit:

```bash
git ls-files docs/maps/dependency-map.json
git check-ignore -v docs/maps/dependency-map.json
```

Expect empty output from the first command, and the `.gitignore` line from the second.

## 5. Publishing still computes from a commit that has no file (User Story 2)

```bash
dotnet run --project tools/versioning/Elsa.Versioning.Calculator -- --record-ref origin/publish-state --commit HEAD
```

Expect exit 0 and the same affected set the PR's last pre-removal compute showed, apart from packages the removal commit really changes (none).

## 6. The required job still reports, and the other checks still pass

```bash
dotnet run --project tools/maps/Elsa.Maps.Generator -- check
python tools/ci/change_scope.py --self-test
pwsh tools/skills/validate-claude-wrapper-drift.ps1
dotnet test Essentials/tests/Architecture/Tests --filter "FullyQualifiedName~DependencyMapTests|FullyQualifiedName~NuGetLockFileTests|FullyQualifiedName~VersionLineOwnershipGuardTests"
```

`-- check` now checks the solution filters. The architecture filter needs a restored tree (`NuGetLockFileTests` reads `obj/project.assets.json`).

On the PR, confirm that `Generated maps fresh` reports under that name.

## 7. After merge (SC-001, User Story 4)

- Read `docs/maps/dependency-map.json` from the `generated-maps` branch at the merge commit, and compare it with a local `-- dependency-map` run at that commit.
- Check the first `packages.yml` run after the merge: only packages with real input changes advance (SC-003).
