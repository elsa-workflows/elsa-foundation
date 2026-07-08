# PRD: Build Warning Remediation

## Problem Statement

The solution build succeeds, but it emits dependency and compiler warnings that hide real maintenance signals. One warning is a high-severity vulnerability advisory for `Microsoft.OpenApi`; the remaining warnings are dependency-hygiene, nullable-boundary, API-clarity, constructor-capture, and test-infrastructure maintenance issues. The repository needs warning cleanup that removes the root causes without suppressing diagnostics or mixing unrelated runtime W12 work into the warning remediation.

## Solution

Resolve the current build warnings as small, reviewable slices grouped by risk class: dependency/security cleanup, source compiler-warning cleanup, and PostgreSQL test-infrastructure cleanup. Each slice must remove its warning root, preserve existing behavior, and leave the full solution build clean for the warning categories captured on 2026-07-08.

## User Stories

1. As a maintainer, I want the solution build to complete without known dependency vulnerability warnings, so that security risk is visible and actionable.
2. As a maintainer, I want direct package references to reflect actual source needs, so that package pruning warnings do not become background noise.
3. As a runtime/domain engineer, I want activity input names to avoid inherited member hiding, so that activity APIs remain unambiguous.
4. As a serialization maintainer, I want JSON island handlers to honor non-null return contracts, so that nullable assumptions are explicit at the boundary.
5. As a persistence maintainer, I want EF Core store constructors to avoid duplicate state capture, so that primary-constructor usage remains clear and warning-free.
6. As a test maintainer, I want Testcontainers fixtures to use supported constructors, so that infrastructure tests do not depend on obsolete APIs.
7. As a reviewer, I want each warning family delivered in a focused PR, so that review can apply the right security, correctness, and architecture lens.
8. As an agent worker, I want one issue per warning family, so that I can implement a bounded change without conflicting with other workers.
9. As a release steward, I want verification commands recorded with the work, so that the remediation can be audited later.

## Implementation Decisions

- Treat `NU1903` as the highest-priority slice. The remediation must use patched `Microsoft.OpenApi` bits according to the GitHub advisory, without broad dependency upgrades unless required by package compatibility.
- Treat `NU1510` as dependency discipline. Remove unnecessary direct references only after confirming source imports and project references still compile.
- Treat `CS0108` as model/API clarity. Prefer renaming the activity input to a domain-specific input name over adding a hiding modifier.
- Treat `CS8603` as a nullable boundary. The implementation should make the non-null return explicit and avoid silently masking invalid input unless existing island serialization behavior requires that.
- Treat `CS9107` as constructor structure cleanup. Preserve the EF Core draft store's current query behavior while removing duplicate parameter capture.
- Treat `CS0618` as test infrastructure maintenance. Use the Testcontainers PostgreSQL builder API that accepts an image parameter, keeping the existing PostgreSQL image and credentials.
- Keep warning remediation separate from the active runtime W12 Speckit implementation unless a warning directly appears in W12-touched code.

## Testing Decisions

- The highest seam is the full solution build: `dotnet build Elsa.Server.slnx`.
- Dependency-security verification should include `dotnet list Elsa.Server.slnx package --vulnerable --include-transitive`.
- Dependency-hygiene verification should include building the identity test project and the full solution.
- Source compiler-warning fixes should include narrow builds for the touched projects plus the full solution.
- Test-infrastructure changes should include narrow builds for both PostgreSQL test projects. Running those integration tests may require Docker; if Docker is unavailable, record that explicitly and rely on compile verification plus the full build.

## Out of Scope

- Converting the repository to warnings-as-errors.
- Broad package modernization beyond the warning roots captured in the build.
- Runtime W12 behavior changes.
- Refactoring unrelated test fixture infrastructure beyond what is needed to remove the obsolete constructor warning.
- Adding new architectural governance around warnings.

## Further Notes

- Source warning evidence is recorded in `docs/reports/build-warning-remediation-2026-07-08.md`.
- Build artifacts for the initial run are under `.scratch/warnings/`.
- GitHub advisory reference: https://github.com/advisories/GHSA-v5pm-xwqc-g5wc.
