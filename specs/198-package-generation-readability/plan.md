# Implementation Plan: Protect Package Generations

**Branch**: `2339-package-generation-readability` | **Date**: 2026-10-08 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/198-package-generation-readability/spec.md`

## Summary

Track a shell's package-generation readability from before its catalog read through exact feature-snapshot selection and confirmed provider teardown. Keep the existing shared `ISupersededAssemblySource` as a nondisposable nonparticipant and add a root-only sealed lease adapter for the upstream CShells build-participant contract. Reevaluate reports from queued lifecycle and committed-catalog evidence, comparing the complete retired set in both directions. Preserve the existing lifecycle facade as a conservative fallback only for shells with no build lease.

## Technical Context

**Language/Version**: C#, .NET 10.0 (Foundation project and current test project target).

**Primary Dependencies**: Existing `CShells.Abstractions`, `Nuplane.Loading.Abstractions`, `ISupersededAssemblySource`, `IClusterMembership`, and `AssemblyLoadContext`; the new upstream participant and optional catalog-commit contracts are supplied by the accepted CShells release.

**Storage**: In-memory host lifetime state only; package inventory and feature selections remain owned by Nuplane and CShells.

**Testing**: Existing xUnit suite at `tests/essentials/Cluster/Readability/Tests/Elsa.Cluster.Readability.Tests.csproj`; retain direct coordinator/source tests and real-CShells lifecycle tests. Use a private preview copy with its own package pins and locks for `.166`/`.99` preflight; final acceptance requires the actual Foundation host against published stable upstream package references refreshed from D7.

**Target Platform**: Foundation host on .NET 10.0. The Foundation readability test project is currently .NET 10.0; this plan does not claim that the Foundation test project targets .NET 8 or .NET 9.

**Project Type**: Existing .NET library and test project; no new project or Foundation contract package.

**Performance Goals**: Keep build/readability callbacks nonblocking with respect to report publication. Catalog/package reads occur outside the short pin-state gate; report requests coalesce asynchronously. No synchronous report-persistence guarantee before feature initialization is added.

**Constraints**:
- A candidate starts with an unresolved conservative pin before any feature-catalog read, then records the exact selected feature assemblies, including disabled features and siblings that share their load context. Never pin arbitrary scanned assemblies after a snapshot is known.
- The existing shared source remains nonparticipant and nondisposable because CShells filters participant registrations from child providers. The separate root-only sealed adapter owns the build lease and notification/watcher stop path.
- Read package/catalog state before entering the short gate; snapshot the live pins under that gate. Do not hold it across awaits, membership publication, or subscriber/user code.
- Release a lease only after confirmed provider teardown, or after confirmed unwind when no provider was created. Lifecycle notifications do not release a generation already owned by a build lease.
- Preserve `BindTo` and `IShellLifecycleSubscriber` compatibility. For a shell with no build lease, retain the legacy conservative pin from its first lifecycle notification through confirmed drain, keyed by immutable shell descriptor. Remember build-owned descriptors through their terminal lifecycle notification even after the lease leaves the live-pin set; a late callback must not create a zombie fallback pin. The existing unknown-shell regression remains required.
- Subscribe to committed catalog changes before initialization, reconcile after late subscription, enqueue/coalesce callback work, and detach subscriptions at root shutdown. Custom catalogs without commit notifications retain the conservative watch fallback.
- Compare the full retired set: retirement publishes after affected provider teardown; reintroduction publishes the restored constraints without waiting for a live provider; unchanged commits stay quiet.
- Current committed package pins are CShells `.159` and Nuplane `.94`; they do not supply the new build/commit APIs. A current-pin compile is therefore not a valid API gate. Preview validation must use an isolated copy with `.166`/`.99` and private locks; no preview pins or locks are committed. Stable actual-host adoption remains a merge/Done gate.
- Register one canonical adapter singleton and expose the upstream participant contract as an alias of that instance. The root `IShellLifecycleSubscriber` factory must resolve that singleton first, then return the existing source's `BindTo(root)` facade. This makes both CShells participant discovery and the existing test host binder establish root ownership. The adapter's root-container disposal stops/unsubscribes it; `BindTo` must not resolve the adapter, avoiding a constructor/factory cycle. A direct legacy `BindTo` call outside the registered root remains best-effort and must not start a notification subscription that can outlive that owner.
- Keep the project EF-free and use upstream abstractions only. Do not change Nuplane runtime/store behavior, Foundation host composition, unload or pruning policy, or release/publish state.

**Scale/Scope**: One root tracker per host, with independent leases for overlapping candidate and active shell generations. Scope is the existing readability source/registration, the build-lease adapter, deterministic tests, and validation guidance.

## Constitution Check

*Gate reviewed before Phase 0 and again after design against both constitutions.*

- **§2.6 — contract-level composition: PASS.** Use the published CShells build and catalog-commit contracts and the existing Elsa readability contract. Keep the shared source distinct from the participant; do not couple by relying on a concrete observer side effect.
- **§2.21.1 — golden rule of refactoring: PASS.** Preserve every current readability test's subject and objective. Update fixtures/wiring for leases, but do not delete cases; specifically retain the no-initializer lifecycle-fallback objective and exact report-transition assertions.
- **§2.23 — unit-test discipline and visibility: PASS.** Add registration resolution coverage and branch-focused tests for the sealed logic adapter and tracker without reflection or `InternalsVisibleTo`. A deterministic fixture baseline/quiescence step must isolate expected report-count deltas; do not weaken assertions or add sleeps to accommodate initial queued work.
- **Dependency and package boundaries: PASS.** No new project or direct Nuplane runtime dependency; keep the readability library's existing abstractions-only and EF-free dependency envelope.
- **Open validation gate**: The current default pins do not contain the new upstream APIs. Preview preflight and final published-package host qualification remain separate; neither is claimed passed by this plan. Final merge/Done evidence also includes the affected architecture guard and generated-map check against the qualified head; neither is claimed passed here.

## Design Decisions

1. **Separate responsibility from the shared source.** Leave the source instance as the shared report/guard query object. Register a root-only, public sealed participant adapter that begins and releases candidate leases; do not put the participant interface on the source, since child-provider descriptor filtering would remove it from those providers.
2. **One release authority per generation.** Build-participant leases own all normal candidate and active generations through confirmed disposal. Preserve the public lifecycle fallback without letting lifecycle events release a leased generation. When a build unwinds before any shell was observed, release its descriptor marker immediately: no terminal shell notification will arrive. For observed shells, retain ownership through terminal notification and safe lease resolution, with a weak shell association to reject late callbacks without retaining every historical descriptor indefinitely.
3. **Conservative snapshot accounting.** Begin before catalog access with an unresolved pin. Query package and feature catalogs outside the state gate; after the exact selected feature snapshot is available, atomically narrow the pin to its non-default load contexts. Read failures leave conservative state.
4. **Root-owned registration and notification lifecycle.** Register one canonical root singleton adapter and alias the upstream participant service to it. The root lifecycle-subscriber factory resolves that same singleton before returning the existing `BindTo(root)` facade, so both real CShells participant discovery and the test host binder establish root ownership. The adapter's singleton disposal owns notification unsubscription and cancellation/stopping of any custom-catalog fallback watch. `BindTo` does not resolve the adapter; direct legacy binding cannot start a root subscription.
5. **Coalesced full-set reporting.** Queue evaluation on Begin, selection, release, and committed catalog change. Compare complete retired-assembly sets; publish on changes in either direction, with retirement timing after provider teardown and reintroduction timing after commit. Never publish inside CShells disposal or synchronously from the initializer path.
6. **No new Foundation wire contract.** The implementation consumes upstream contracts and keeps its local lease representation internal to the tracking flow. No `contracts/` artifact is needed.

## Project Structure

### Documentation (this feature)

```text
specs/198-package-generation-readability/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── checklists/requirements.md
└── spec.md
```

### Source Code

```text
src/essentials/Cluster/Readability/
├── EfSchemaReadabilityServiceCollectionExtensions.cs
├── NuplanePackageGenerations.cs
└── NuplanePackageGenerationBuildParticipant.cs

tests/essentials/Cluster/Readability/Tests/
├── SupersededPackageGenerationTests.cs
├── SupersededPackageGenerationShellTests.cs
└── existing readability registration and source tests
```

**Structure Decision**: Keep the behavior inside the existing readability library and its existing test project. Change DI wiring there so the source stays shared/nonparticipant and the adapter is resolved only from the root. A singleton factory exposes the same adapter through CShells' participant contract; the root lifecycle-subscriber factory first resolves that canonical singleton, then returns `NuplanePackageGenerations.BindTo(root)`. The host container therefore owns one adapter instance and its stop/unsubscribe lifetime; the source facade does not resolve the adapter and cannot create a constructor cycle. Retain the real-CShells test project as the lifecycle-integration boundary. Synthetic providers use a test-owned wrapper that disposes the provider before signaling lease completion; fake drains control the confirmation signal explicitly. Never use a production initializer disposal as a proxy for provider disposal.

## Complexity Tracking

No constitutional violations or project-count exceptions are proposed.
