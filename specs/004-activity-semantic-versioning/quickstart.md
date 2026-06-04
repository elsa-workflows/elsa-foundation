# Quickstart — Verifying Activity Semantic Versioning

How to exercise and validate Unit 3 once implemented. xUnit only (no FluentAssertions).

## Build
```
dotnet build Elsa.Server.slnx
```
Green build with **no `int`-typed version member remaining** on the catalog model, read contract, projections, reconciliation model, or API surface (SC-005).

## Author-controlled version (US1)
1. A test activity with **no** `[Version]` in an assembly versioned `2.1.0` → reconcile → persisted `Version == "2.1.0"` (assembly inherited).
2. A second activity `[Version("3.0.0")]` in the same assembly → reconcile → persisted `Version == "3.0.0"` (override wins).
3. Re-run reconciliation against unchanged sources → **zero** new rows (idempotent, SC-003).

## Semver ordering (US3 / SC-002)
Persist `1.0.0`, `2.0.0`, `10.0.0`, `1.2.0` for one definition; query the ordered listing → order is `10.0.0, 2.0.0, 1.2.0, 1.0.0` (precedence, not lexical). Patch case: `1.0.1, 1.0.10, 1.0.2` → `1.0.10` highest. Assert the SQL `ORDER BY` runs on `SemVerSortKey` (DB-side), not after client materialisation.

## Exact-version resolution (US4 / SC-006)
`(DefinitionId, "2.1.0")` returns the one record; `(DefinitionId, "9.9.9")` returns none (exact, not nearest). `1.0.0` and `1.0.0+build` resolve as the **same** logical version (build metadata ignored, FR-013).

## Mis-versioned source fails loudly (US5 / SC-004)
Reconcile at `1.0.0`; mutate content without changing the version → re-reconcile → `ActivityVersionHashMismatchException` reporting `"1.0.0"`. Never a silent overwrite.

## Resilient scan (FR-023)
Point the CLR source at a folder containing (a) an activity DLL, (b) a non-activity DLL, (c) a DLL with unresolvable references. Reconciliation discovers activities from (a), silently skips (b), logs-and-skips (c), and completes. A discovered activity with an **invalid** `[Version]` still throws a domain-scoped exception.

## Constitution / test-discipline gates
- `Elsa.Activities.Design.Tests/Unit/ReadContractSurfaceTests.cs` updated to pin `Version : string`.
- Registration tests resolve every service for the reshaped reconciliation feature and the new `.Clr` feature (§2.23.1).
- Branch-covered unit tests for: `SemVer`/`SemVerComparer`, the sort-key normaliser, `ActivityVersionResolver` (attribute / informational-version / 4-part fallback / unresolvable), the scanner's resilient-scan paths (§2.23.2).
- All pre-existing reconciliation + catalog tests pass unchanged in subject/objective (§2.21.1).

## Constitution update (in-unit, FR-016)
`.specify/memory/constitution.md` §E2.8 reworded: activity version is an author-controlled SemVer 2.0.0 string sourced from the declaring assembly's version, optionally overridden by `[Version]`, read by the assembly reconciliation source; any `int`-version / integer-lookup wording reworded for string semver. If the `Elsa.Activities.Runtime.Core` extraction is adopted, module-decomposition wording updated too.
