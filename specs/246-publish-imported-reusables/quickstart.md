# Quickstart: verify spec 246

Run from the worktree root. Build and test only these projects; leave the whole solution to CI.

## 1. Publisher (requested version id, SC-006)

```bash
dotnet test Essentials/tests/Workflows/Publishing/Api/Tests
dotnet test Essentials/tests/Workflows/Publishing/Persistence/EntityFrameworkCore/Tests
```

Expect: the existing tests pass unchanged, and the new tests for a requested id (used, blank, in use, fingerprint)
pass.

## 2. Conversion and compile proof (FR-015, FR-018, SC-003)

```bash
dotnet test Extensions/Elsa3/tests/Mapping/Tests
```

Expect:
- every reusable case in `Elsa3ReusableTerminalTests`, `Elsa3CollectionLoweringTests` and
  `Elsa3FlowchartConversionTests` passes `GraphProviderProof.ValidateAsync` and `CompileAsync` with zero errors;
- caller `OutcomeNames` equal the target's `BoundaryOutcomes` names;
- the leaf start-trigger reusable is refused with `ELS3-REUSABLE-UNSUPPORTED-START-TRIGGER`.

## 3. Import persistence (FR-004, FR-012)

```bash
dotnet test Extensions/Elsa3/tests/Activities/Design/Import/Persistence/EntityFrameworkCore/Tests
```

Expect:
- the commit writes no catalog version for a reusable version, and a new definition has a null head and one draft;
- the receipt round-trips its warnings, and a 1.0.0 row without warnings still reads.

## 4. Import → publish → run (FR-001 to FR-016, SC-001 to SC-005)

```bash
dotnet test Extensions/Elsa3/tests/Activities/Design/Import/Integration/Tests
```

Expect:
- After apply, every reusable version has an active publication under the plan's id, with a template and
  `elsa.outcomes`.
- An Elsa 4 caller receives the reusable's output, a flowchart caller follows the non-`done` outcome, and the wrapper
  runs.
- If one version is refused, independent versions are still published, its dependants are reported as
  `NotPublishedDependency`, and a reapply after the fix publishes only the rest.
- A reapply of a fully published plan publishes nothing and returns the same warnings.
- Without `workflows/publishing:manage`, the apply returns 403 and writes no row.

## 5. Guards and maps

```bash
dotnet test Essentials/tests/Architecture/Tests
dotnet run --project tools/maps/Elsa.Maps.Generator -- all
git diff --stat docs/
```

On Windows, `-- check` always reports every map as stale (CRLF). Use `-- all` and review the diff instead, then stage
each changed map by path, `docs/maps/manifest.json` included.
