# Implementation Plan: Studio shared loader integration

**Branch**: `agent/codex/3356-studio-shared-loader` | **Date**: 2026-10-26

**Spec**: [spec.md](spec.md) | **Issue**: [#3356](https://github.com/elsa-workflows/elsa-foundation/issues/3356)

## Summary and technical context

Keep Web as the composition root while replacing its loader glue with the qualified released
CShells.Nuplane adapter. .NET 10 / C#, xUnit, real directory-feed NuGet packages and the existing
HostingReuse probe fixtures provide the narrow validation path. No browser code changes are needed.

Studio owns a separate central package file; extension Studio projects import it. Replace old
Elsa.Platform manifest namespaces/package IDs everywhere reached by that graph, update the matching
feed mapping, and regenerate reached locks using the repository restore graph workflow. Other versions
remain unchanged.

## Constitution check

Reviewed the spec before planning. Pre-design and post-design checks:

- Framework §2.1/§2.6: use public upstream adapter/catalog contracts; no backend implementation
  dependency, new domain model or production friend grant.
- §2.5.1: the shared adapter owns its application-lifetime coordinator. Studio adds no detached task,
  static reload semaphore, duplicate ownership or independently disposed singleton aliases.
- §2.21/§2.23: risk-based tests cover catalog freshness, generation replacement, retained scope,
  failure/retry and cancellation; existing module/manifest tests cover metadata compatibility.
- §2.25: delete the three wrappers and their mirror-only tests after replacement behavior passes.
- Elsa §E5: dependency convergence only; no release train, publishing or package extraction.
- No new pattern or reliance on provisional constitution sections; no exception is required.

## Design

`StudioFeatureAssemblies.WithStudioFeatureAssemblies` remains the current Web assembly roster seam
until the thin-host task. It selects the real adapter with `ChangedOrPending` and `AutoReload=true`.
Standard dependency-aware options configuration supplies a logger to `OnReloadResults`; log each
original exception with shell name and a successful-reload count. The adapter owns pending retries,
refresh ordering, post-loading admission and cancellation. Do not copy those algorithms.

`ElsaFeatureManagementApi` reads `IRuntimeFeatureCatalog.GetSnapshotAsync` directly. Its configuration
store, apply semantics and routes remain for the separately scoped administration task.

Existing root Console Stream registrations stay in Program. Update its stale observer comment.
Manifest generator hints/reader move together to `Elsa.Specifications`; verify generated manifests,
not merely successful compilation.

## Structure and validation

- `Studio/Directory.Packages.props`, `Studio/nuget.config`, reached `*.csproj`/`packages.lock.json`:
  selected versions, new adapter reference, source-only generator and contract identity changes.
- `Studio/src/apps/Elsa.Studio.Web/{Program,StudioFeatureAssemblies,ElsaFeatureManagementApi}.cs`:
  actual adapter composition and direct catalog use; remove the three obsolete wrappers.
- `Studio/tests/Elsa.Studio.Tests/`: update catalog stubs/discovery for public upstream signatures;
  add behavioral integration tests exercising the same profile and real package lifecycle.
- `tools/studio/HostingReuse.Probe/{First,Second}`: existing fixture projects may be referenced by
  integration tests; do not nest new projects or silently substitute source for released dependencies.

Run affected Studio tests, actual generated-manifest assertions, normal/disabled-auto-reload/restored
qualification, locked solution restore, architecture tests and map drift checks. Capture exact commands,
head, outcomes and limitations on the PR. CI must pass on the reviewed head before delivery.

## Artifact review

The spec, design and planned tasks agree with #3356 and the merged contract. The scope excludes
protected asset streaming and physical cleanup, which remain #3359; no legacy directory pruning
claim is made by this dependency adoption.
