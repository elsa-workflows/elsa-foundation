# Implementation Plan: Worker HTTP starting profile

**Branch**: `codex/2326-worker-http-profile` | **Date**: 2026-10-02 | **Spec**: [spec.md](spec.md)

**Input**: Whole delivery task [#2326](https://github.com/elsa-workflows/elsa-foundation/issues/2326), under #1961 / program1959. Baseline e7032680dc48e2077631e01e2c961093b0c5ef4d. Completed normalization prerequisite #2308 is main-qualified and released.

## Summary

Publish `worker-http@1` in catalogv3, retaining byte-identical v1/v2 resources and exact-pin loading for all three. Extend the existing Worker actor to create its candidate through actual CLI init/plan/interactive accept/generate, then start its existing child from those files. Remove the child static feature list and in-memory feature/settings authority. Prove exact19 execution/event/resumption/revocation/restart and exact18 ControlFlow removal plus alternate-audience capabilities read. No new product authentication behavior, command schema, suite, provider matrix or CI cadence.

## Technical Context

**Language/Version**: Existing C# net10.0; repository SDK/build-slot wrapper.
**Primary Dependencies**: Existing Modularity.Planning, Cli/file bridge, CShells, Identity.Oidc/IAM, Runtime EF and WorkerOidcHost fixture. No new external package.
**Storage**: Named Runtime SQLite resource and separate explicitly configured IAM SQLite target, existing migrations and real store controls.
**Testing**: Existing Planning tests, CLI tests and Runtime EF actor project. Link existing `DotnetElsa.cs` and `PseudoTerminalCli.cs` into Runtime EF tests and add the production CLI project build reference; no test-project reference or copied process implementation. PTY actor gate executes on Unix with Python3; a Windows early return is not acceptance evidence.
**Target Platform**: From-source macOS/Linux single-host actor, loopback real issuer/key/metadata and real Kestrel/SQLite; external IdP deployments remain unverified.
**Constraints**: Candidate activation/settings authority, old-pin immutability, secret-safe bounded receipts, deterministic async teardown, no root authentication substitute, one merge lane.

## Constitution Check

Load both [framework](../../.specify/memory/constitution-framework.md) and [Elsa](../../.specify/memory/constitution.md) constitutions. §2.5/2.6: preserve feature boundaries and contract coupling; membership is catalog data, not a new feature hierarchy. §2.21.1: preserve all existing test subjects/objectives; fixture setup changes are allowed, deletion is not. §2.23: loader branches get direct tests; no new feature class or service extension point is introduced. §2.12 settings classification is deferred and is not ratified by this profile. Draft §2.24/E2.9 material is not relied on as an enforceable gate. Elsa naming/dependency rules remain unchanged.

Pre-design and post-design: no new gate exception, package family, authentication policy or architecture amendment. Existing host-owned IAM isolation and accepted planner/file-bridge contracts are retained.

## Project Structure

```text
specs/191-worker-http-profile/
  spec.md, plan.md, research.md, data-model.md, quickstart.md, tasks.md
  contracts/worker-profile.md
  checklists/requirements.md
src/essentials/Modularity/Planning/
  Catalog/FoundationSelectionCatalog.cs
  Catalogs/foundation-selection-catalog-v3.json
  Elsa.Modularity.Planning.csproj
tests/essentials/Modularity/Planning/Tests/FoundationSelectionCatalogTests.cs
tests/essentials/Cli/Tests/CompositionInitCliTests.cs (and affected catalog assumptions)
tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/
  Fixtures/WorkerOidcHost/Program.cs
  Tests/WorkerOidcHostFixture.cs
  Tests/WorkerOidcHostTests.cs
  Tests/WorkerProfileCandidate.cs
  Tests/Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.csproj
docs/reference/worker-http-profile.md
```

## Phase 0: Research

See [research.md](research.md). Source-reviewed seams support a complete Worker slice without a pending owner decision. The separate Authoring API choice and real-human UX participants cannot invalidate this task. Actual secondary activation is still an execution gate, not assumed from source.

## Phase 1: Design

The [contract](contracts/worker-profile.md) fixes immutable membership, candidate authority and actor proof. Existing planner/file bridge retains its format. Host source JSON prepopulates OIDC/IAM/locking/runtime feature objects so generation preserves local settings; portable authored settings remain empty. The child loads candidate appsettings, base shell and selected overlay using the same CShells provider, with no additional Features provider. Root tenant and fixture-only store controls remain unchanged.

Parent and child compute file hashes independently. Startup receives only candidate directory/shell/environment and ordinary host inputs through stdin; readiness contains bounded identities, enabled IDs and safe observed booleans. The primary restart reuses exactly the same candidate files and built DLL. Discovery assemblies cannot select features. The secondary candidate explicitly removes ControlFlow, keeps Events, binds alternate audience and uses a legitimate durable capabilities-read rule. Static-selection and stale-audience mutations must fail their own decisive assertions; restore source before final gates.

## Implementation Sequence and Delegation

1. Root approves spec/contracts/checklist and records issue/project ownership.
2. Bounded catalog worker owns catalogv3/loader/Planning and CLI catalog assumptions in an isolated worktree; no heavy tests until integration.
3. Bounded actor worker owns candidate helper/Runtime EF project reference and existing child/parent/tests in a separate worktree; no production OIDC changes, new endpoints or testsuites.
4. Root reviews both deltas and source ownership, integrates into the single branch, runs scoped compile/focused gates serially, corrects findings and executes real actor/mutations.
5. Root updates user documentation/evidence/maps/filters and runs final affected suites, architecture, maps and diff review, followed by exact-head external review/hosted gates and resulting-main source qualification.

## Integration and Release

One coherent implementation PR references `Fixes #2326`; spec becomes Approved after root and independent contract review. Implementation status is recorded only after verified actor and final gates, according to [spec lifecycle](../../docs/reference/spec-lifecycle.md). T028 in completed Spec190 and the canonical program pointer are synchronized here from its final public closure; do not create another evidence-only PR.

Optional SpecKit commit hooks are configured disabled in the local extension catalog; coherent unsigned commits use explicit staging. AGENTS managed context points here; existing instruction content is preserved. Generated maps are deliberately refreshed and all changed outputs, including manifest, staged explicitly. Review generated findings before integration.

## Complexity Tracking

No new exception. Linking two existing shared CLI helpers is deliberate reuse; no test-project-to-test-project dependency. The non-test child executable remains the existing one. Any file-generation contract refusal is investigated rather than bypassed with in-memory settings.
