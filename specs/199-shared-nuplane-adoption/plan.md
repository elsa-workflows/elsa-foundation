# Implementation Plan: Shared Nuplane Adapter Adoption

**Branch**: `2340-shared-nuplane-adoption` | **Date**: 2026-10-09 | **Spec**: [spec.md](spec.md)
**Status**: Draft — source/discovery design prepared. The upstream exception-observability correction is delivered and has public `.171`/Nuplane `.99` consumer proof on .NET 8/9/10. Foundation host implementation, its actual-host proof and final stable adoption remain outstanding.

## Summary

Replace the two host package providers/observers with the public upstream adapter. Retain app-local standard monitored-option mappings and Elsa diagnostic callbacks. Preserve existing test assertions and final stable adoption gates. See [research](research.md) for source evidence and the preview-only qualification boundary.

## Technical Context

**Language/Version**: Existing C#/.NET 10 host projects; upstream family targets .NET 8/9/10.
**Primary Dependencies**: CShells.Nuplane, Nuplane autoload, standard configuration/options/change-token and logging abstractions. No new Elsa feature-management dependency in Foundation.Host.
**Storage**: No new store or state machine. Existing upstream coordinator owns pending freshness/reload state.
**Testing**: xUnit Modularity regressions, Cluster/EF real-host refusal tests, architecture guards, backend E2E and `tools/demo/rehearse.sh` Acts1/2. Preserve existing test subjects/objectives and assertions when rewiring.
**Target Platform**: Existing supported host platforms; package assets retain all three upstream frameworks.
**Project Type**: Two existing deployable host applications using one optional upstream integration package.
**Performance Goals**: No performance measurements; owner retired that program. Keep Workbench changed-or-pending and deferred cold freshness behavior.
**Constraints**: Final source merge/adoption requires exact stable-family pins/locks/maps and actual rebuilt-host evidence. Interim source preparation/isolated previews are not acceptance.
**Scale/Scope**: Foundation #2314 only; no startup/readiness, readability, host-library, unloading or pruning implementation.

## Constitution Check

- Framework §2.7: upstream optional adapter isolates reusable behavior; Elsa-specific refusal interpretation remains local.
- Framework §2.16/§2.17: no new project; preserve feature-free host closure and accept a few intentionally different profile-mapping lines.
- Framework §2.21.1 and Elsa §E1: existing regression methods/assertions retained; repair fixture composition, not expected outcomes. The upstream Error/original-exception logging correction is qualified; Foundation tests must preserve existing Error assertions while recording Nuplane's additional dispatcher Warning. Direct caller cancellation remains separately tested without error logging.
- Framework §2.23: exercise each new logic-bearing options setup through its public surface and actual DI composition, including all default/invalid/configuration-reload and refusal branches. Use public sealed setup implementations in nonpackable app projects; no new feature class.
- Test cadence: no derived application constitution was found that declares TDD or another cadence. Framework §2.21.1 requires preserving refactored test subjects/objectives; it does not require test-first ordering. New tests should follow implementation dependencies and prove the completed behavior without claiming a required fail-first run.
- Existing host boundaries: preserve feed/base-path setup, observer order, host-provided assemblies, unrelated sharing and per-host startup policy. Do not relax architecture allowlists to admit a convenience helper dependency.
- Draft framework §2.24 and Elsa §E2.9 are not relied on as new ratified gates.

**Pre-design assessment**: no product-policy ambiguity in this observer-only scope. **Post-design assessment**: the upstream correction is delivered and qualified through the actual public adapter and Nuplane dispatcher on three runtimes. Foundation source, profile tests and real-host adoption remain unverified. The canonical preparation branch remains documentation-only with `.159`/`.94` pins and locks unchanged. Final stable-family adoption remains a separate gate.

## Project Structure

### Documentation

`specs/199-shared-nuplane-adoption/`: spec, plan, research, data model, contracts, quickstart and tasks. Tasks distinguish preview preparation from gated final stable adoption.

### Source Code

- `src/apps/Elsa.Foundation.Host/Program.cs`, host project and app-local options/diagnostic setup under `Shells/`.
- `src/apps/Elsa.Workbench/Program.cs`, host project and app-local setup under `Modularity/`.
- Remove duplicated `src/apps/Elsa.Foundation.Host/Feed/NuplaneAssemblyProvider.cs`, `src/apps/Elsa.Workbench/NuplaneAssemblyProvider.cs` and old observer implementations after composition and tests prove replacement. Preserve `ShellReloadFailure` and existing refusal models/endpoints.
- `tests/essentials/Modularity/Tests/WorkbenchShellCatalogRefreshTests.cs`, `HostOwnedServicesAreSharedWithShellsTests.cs`, focused profile/reload-token tests; retain relevant `tests/essentials/Cluster/EntityFrameworkCore/Tests/FoundationHostReloadRefusalTests.cs` and affected architecture coverage.
- Final adoption additionally updates `Directory.Packages.props`, reached `packages.lock.json`, applicable Docker restore graph and generated maps together after stable releases.

**Structure Decision**: host-specific options/diagnostics stay app-local; only reusable discovery/coordination is shared upstream. The existing feature-projection integration remains untouched. See [research](research.md).

## Validation Sequence

1. Prepare the candidate source and coherent `.171`/`.99` central package overlay in the separately owned qualification worktree only; regenerate reached lockfiles there. Keep the canonical preparation branch's `.159`/`.94` pins and locks unchanged.
2. Implement the two host compositions and app-local monitored option profiles. Preserve autoload/observer order, one coordinator identity, Foundation.Host's feature-free boundary, Workbench's existing feature catalog, feed setup, and unrelated shared-service behavior.
3. Preserve and adapt the existing regression suite through the qualified public adapter. Add actual options-DI/configuration Reload coverage for both legacy defaults and invalid values; prove that a Workbench setting change alone schedules nothing and a later eligible completion uses the new snapshot. Test coordinator identity/order, cold-build freshness, callback semantics, refusal/redaction, ordinary exceptions through the actual dispatcher, and direct cancellation.
4. Build and run only touched projects in the qualification worktree through the shared build-slot wrapper; run affected Modularity and Cluster/EF tests and actual rebuilt Host/Workbench checks available there. Record exact preview package/archive/cache/source identities and each command result. Do not call these stable acceptance.
5. Root reviews the complete candidate diff and runs appropriate reversible causal mutations for live option invalidation and coordinator alias/order. Preserve unsuccessful attempts honestly; keep stable merge/adoption unchecked.
6. After stable family publication, replay the reviewed application-source changes into the final adoption branch; set normal centrally managed stable versions including `CShells.Nuplane`, update reached locks, applicable Docker graph and maps, then rebuild real hosts and run named regressions, backend E2E and frozen Acts1/2 rehearsal. Complete current-head and resulting-main gates before closure.

## Complexity Tracking

No constitutional exception is approved. The upstream Error logging correction is qualified; Foundation callback behavior remains to be validated. No blanket permission or review gate is introduced.
