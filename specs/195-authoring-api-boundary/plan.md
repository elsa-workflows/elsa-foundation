# Implementation Plan: Authoring-only API

**Branch**: `codex/2459-authoring-api-contract` | **Date**: 2026-10-06 | **Spec**: [spec.md](spec.md)

**Input**: Approved product boundary in #1961/#2459: decouple first and expose an Authoring-only API. This plan publishes contracts and a proof plan; no implementation or executed actor evidence is claimed here.

## Summary

Separate Publishing's required internal Runtime support from Runtime HTTP/execution composition. Add a narrow Publishing API feature using an explicit non-executing endpoint allowlist, retain the full legacy mapper, and supply Publishing-owned slot and artifact reads and publication-identity export for Studio. Preserve the existing complete atomic Runtime EF backend through a separately enrolled support feature without implicit Resumption. Publish the versioned Authoring starting profile only after actual host and client journeys pass.

## Technical Context

**Language/Version**: C#/.NET10; existing TypeScript Studio client.

**Primary Dependencies**: Existing CShells features, NativeEndpoints1.0.0-preview.6 explicit `MapEndpoint<T>`, current capability catalog, Publishing/Runtime stable contracts, existing Foundation Identity host substrate. No new dependency.

**Storage**: Existing Activities Design, Workflows Design, Publishing and Runtime contexts bound through named persistence resources. Support uses unchanged Runtime R01-R29 aggregate/migrations; all existing private cursor-key validation remains.

**Testing**: Existing Publishing API contract/scenario fixtures, Publishing EF `AuthoringCompositionHostEvidenceTests`, Workbench `AuthoringFixtureHostEvidenceTests`, relevant existing Runtime EF ownership/activation fixtures, architecture/maps and combined-host test-run controls. Actual rebuilt authenticated host plus browser/client and generated-file actors are required for behavioral delivery.

**Target Platform**: Supported .NET10 hosts and current Studio web client. SQLite named-resource restart proof is bounded to SQLite; preserve current provider gates without claiming newly demonstrated cross-provider layouts.

**Project Type**: Foundation library/API composition plus cross-repository Studio adapter/UI integration and later catalog/profile publication.

**Performance Goals**: No new benchmark or timing gate; performance measurement was retired under #1668/ADR0073. Avoid global Runtime inspection just to discover this definition's published artifacts.

**Constraints**: Zero implicit runtime/test-run HTTP routes and execution dispatch/redrive/alteration pump; full-only legacy compatibility; deterministic feature resolution, owned/idempotent registration, atomic durable activation and immutable catalog pins. No new EF suite/provider/cadence.

**Scale/Scope**: One coherent backend decoupling capability, one current-client integration capability and one generated-host profile publication capability. Profile publication depends on backend/client proof, not merely source extraction or compilation.

## Constitution Check

Pre-research and post-design assessment:

- Elsa §E2.2 / §E2.2.3: preserve Design-only, Runtime-only and combined deployment shapes. Runtime support references Runtime/core contracts, never Design; Publishing remains the existing bridge. New publication HTTP views belong in Publishing, not Runtime.
- Framework §2.10: new reads query state without execution/mutation. Existing publish/slot lifecycle command contracts remain unchanged.
- Framework §2.11 / §2.6.2: declare real static dependencies, verify actual real-scope DI closure, reject missing/conflicting provider/capability ownership before route activation. No fallback to `AddWorkflowRuntime()` to repair the narrow host.
- Framework §2.25.3: no subtraction inferred from 401 or a feature label. Prove route/capability inventory, authenticated HTTP absence and useful publication journey, retaining combined controls.
- Framework §2.12 and Elsa §E4 remain deferred; no generic settings/secrets taxonomy ratification here.
- Elsa §E2.9 and its draft-mutation material remain provisional/unratified; this work preserves current Design/publication contracts and does not promote those sections to a ratified gate.
- Shared persistence retains existing ownership, supported-layout and atomic Runtime EF constraints. No new transaction exception or reduced schema is introduced.

Result: no new constitutional exception required by the proposed design. Actual implementation must pass architecture and host/provider gates; this review is not executed proof.

## Project Structure

### Documentation (this feature)

```text
specs/195-authoring-api-boundary/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── authoring-surface.md
│   └── acceptance-proof-matrix.md
└── checklists/requirements.md
```

`tasks.md` belongs to the implementation work after contract review, not this specification publication.

### Source Code (existing owners)

```text
src/essentials/Workflows/Publishing/
  WorkflowsPublishingFeature.cs
  Api/{WorkflowsPublishingApiFeature.cs,WorkflowsPublishingApi.cs,Capabilities/,Endpoints/,Models/}
src/essentials/Workflows/Runtime/
  Extensions/RuntimeCoreServiceCollectionExtensions.cs
  Api/WorkflowsRuntimeTriggersFeature.cs
  Persistence/EntityFrameworkCore/{RuntimeEntityFrameworkCoreFeature.cs,DependencyInjection/}
src/essentials/Persistence/EntityFramework/{Tooling/,ResourceResolution/}
src/essentials/Modularity/Planning/Catalogs/
src/apps/Elsa.Workbench/
tests/essentials/Workflows/Publishing/{Api/Tests/,Persistence/EntityFrameworkCore/Tests/}
tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/
tests/essentials/Workbench/Tests/
```

Studio owner: `elsa-foundation-studio`, existing `src/essentials/Elsa.Studio.Workflows/Client/src/{api/publishing.ts,workflow-editor/,ActivityDefinitionDraftEditor.tsx}` consumers. Source snapshot `c3be2553650e2158616bc874c97ad2b47077bd20`; actual implementation must refresh its branch, claims and repository instructions.

**Structure Decision**: Keep features in their owning existing module assemblies and share registration/map helpers only where repeated. No new foundation domain, generic feature framework or persistence project.

## Design and Integration Sequence

1. Extract endpoint-free publication-support defaults from current Runtime composition, preserving artifact-store backend ownership markers, activation defaults, required root-write lease/options/time, stores/readers, trigger extractor/indexer/providers, requirement checker, closure serialization and registry/catalog contributors. Full Runtime consumes the same helpers; Publishing depends on support instead of full Triggers. Resolve publishing/preflight/export services in a real CShell scope.
2. Add `WorkflowsPublishingAuthoringApi` separately from existing full `WorkflowsPublishingApi`. Share only non-executing transport registrations, serializer/error ownership, permissions, conversion-profile contributor and export target mapping. Keep test-run runners/stores/handlers and full capability declaration in the legacy full feature. Narrow routes are individually allowlisted; never scan the full assembly. Existing incompatible same-ID capability registration rejects selecting both surfaces before route activation.
3. Add publication-owned slot and artifact summary reads and exact publication export defined in the contract, retaining version-only export unchanged. Share transitive closure assembly; use an additive producer operation with fail-closed default interface behavior for custom producers. Preserve foreign slot source identity and existing publication lifecycle semantics. Do not depend on `WorkflowExecutableInspector`, RuntimeApi DTO/service registrations or execution retention scanning.
4. Add `WorkflowsRuntimePublicationSupportEntityFrameworkCore`: dependency on publication support, module/resource enrollment attributes and unchanged `AddRuntimeEntityFrameworkCore`/Runtime migrations. Preserve full existing EF feature and explicit provider ownership/order/repeat behavior. Share settings/options application where this removes repetition without changing public feature identities. Describe the complete R01-R29 schema and private signing-key requirements honestly. Persistence dispatch stores are not dispatchers; no Resumption/pump is implicit.
5. Extend existing host fixtures to prove authenticated persistent publication/reads/export and restart, plus all prohibited endpoint/capability families and combined-host controls. Include actual generated host configuration as a later profile gate; a handcrafted test host alone cannot certify generation.
6. Adapt current Studio: prefer advertised publication-owned reads, use only advertised legacy Runtime fallbacks on combined hosts, and gate the three execution controls by their corresponding execution relation. Verify actual browser/server requests as well as narrow adapter/component tests.
7. Publish a new reviewed catalog/profile version, preserving every existing v1/v2/v3 byte/pin. Authoring remains an evaluation fixture until generated configuration is consumed by a fresh authenticated host and client proof succeeds. Record explicit re-resolution rather than rewriting old accepted closures.

## Implementation Refinement Boundary

After the concrete contract is reviewed and publication gates pass, refine near-term linked implementation work for the coherent backend capability and Studio integration. Keep future profile publication coarse until backend/client evidence establishes its exact feature closure and private resource inputs. Do not create a new discovery spike to repeat completed #1991; investigate only a new bounded uncertainty that could invalidate this contract.

Produce implementation tasks with official `speckit-tasks` before code. Every behavioral child must own its real actor journey and meaningful bypass/refusal control; no issue is complete from unit mocks or route names alone. Keep one active delivery/integration item and refresh Project51/native relations at each transition.

## Complexity Tracking

No new gate exception. The complete Runtime persistence schema is intentionally retained to preserve the established activation transaction and migration boundary; a smaller family is not justified by current evidence. Narrow/full publication surfaces deliberately conflict when co-selected instead of introducing a capability-merging framework.
