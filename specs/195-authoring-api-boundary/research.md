# Authoring boundary research

Status: source-backed design evidence for #2459; no actor execution or implemented API/profile claim. Foundation source at merge `0e7e7f4b23ba9c55bdb6d9062348f12cf25e4101` has the same code as inspected branch64e1b317f. Studio inspected at `c3be2553650e2158616bc874c97ad2b47077bd20`, leaving unrelated untracked modules untouched. Refresh both heads before implementation.

## Decision 1 — Remove the implicit HTTP edge, retain internal publication support

Decision: replace Publishing → RuntimeTriggers → RuntimeApi with endpoint-free publication support, sharing owned registration with the full Runtime root. Supply activation coordinator and its mandatory root-write lease manager/options/time, not only the trigger index.

Rationale: Publishing currently claims endpoint-free behavior but its static dependency mounts Runtime HTTP and composes `AddWorkflowRuntime`, including execution/alteration background work. Preflight/publication/export require artifact/activation/trigger contracts, not that full composition. Artifact backend descriptor ownership must survive extraction so EF can safely replace defaults.

Alternatives: retain the old authenticated API and document it (explicitly rejected by owner); call full Runtime without mapping routes (still installs dispatch/pumps); raw duplicate TryAdd stores (breaks backend ownership/order safety).

Source: [Publishing dependency](https://github.com/elsa-workflows/elsa-foundation/blob/0e7e7f4b23ba9c55bdb6d9062348f12cf25e4101/src/essentials/Workflows/Publishing/WorkflowsPublishingFeature.cs#L40), [Triggers](https://github.com/elsa-workflows/elsa-foundation/blob/0e7e7f4b23ba9c55bdb6d9062348f12cf25e4101/src/essentials/Workflows/Runtime/Api/WorkflowsRuntimeTriggersFeature.cs#L35), [Runtime owned artifact descriptors](https://github.com/elsa-workflows/elsa-foundation/blob/0e7e7f4b23ba9c55bdb6d9062348f12cf25e4101/src/essentials/Workflows/Runtime/Extensions/RuntimeCoreServiceCollectionExtensions.cs#L136), [coordinator constructor](https://github.com/elsa-workflows/elsa-foundation/blob/0e7e7f4b23ba9c55bdb6d9062348f12cf25e4101/src/essentials/Workflows/Runtime/Services/Executables/WorkflowActivationCoordinator.cs#L13).

## Decision 2 — Keep distinct narrow/full HTTP surfaces

Decision: explicit per-endpoint allowlist in new Authoring feature/mapper; preserve existing full mapper and test-run behavior. Share retained transport registrations/export helpers, not execution handlers or full capability declarations. Existing same-ID incompatible capability refusal rejects narrow+full co-selection.

Rationale: the full mapper scans all endpoint types and the static Publishing capability includes all six test-run relations. Removing RuntimeApi alone would still permit test-run dispatch. NativeEndpoints preview6 already supplies `MapEndpoint<T>`; no SDK upgrade/filtering abstraction is needed.

Alternatives: changing the full mapper to remove runs breaks combined clients; scanning then relying on authorization leaves endpoints exposed; capability-merging/route-owner framework adds unnecessary complexity.

Source: [mapper](https://github.com/elsa-workflows/elsa-foundation/blob/0e7e7f4b23ba9c55bdb6d9062348f12cf25e4101/src/essentials/Workflows/Publishing/Api/WorkflowsPublishingApi.cs#L33), [capabilities](https://github.com/elsa-workflows/elsa-foundation/blob/0e7e7f4b23ba9c55bdb6d9062348f12cf25e4101/src/essentials/Workflows/Publishing/Api/Capabilities/PublishingApiCapabilities.cs#L10), [conflict](https://github.com/elsa-workflows/elsa-foundation/blob/0e7e7f4b23ba9c55bdb6d9062348f12cf25e4101/src/essentials/Api/Capabilities/Extensions/ServiceCollectionExtensions.cs#L18). Installed primary SDK XML at `~/.nuget/packages/nativeendpoints/1.0.0-preview.6/lib/net10.0/NativeEndpoints.xml` documents explicit mapper at lines261-265. This is inspected documentation, not executed route proof.

## Decision 3 — Add Publishing-owned useful reads

Decision: definition-scoped publication slots and published-artifact summary. Preserve foreign slot source identity, current journal semantics, per-publication provenance for content-addressed artifact reuse, and legitimate pruned retired history as unavailable rows. Current incomplete serving state or identity mismatch yields a fixed409 problem, not silent empty results.

Rationale: Studio publication/slot/draft-equivalence flows currently consume Runtime slot reads. Its artifact panel and export discovery also consume global Runtime executable inspection, which depends on execution retention. The existing journal only lists by slot; authority → slot journal → source reference → artifact Find provides a narrow read using established contracts.

Alternatives: enable Runtime API solely for reads violates the approved boundary; current PublicationSlotView loses source kind/ID; global artifact/reference scanning or Runtime inspector adds execution inspection; treating missing/pruned rows as no publications produces misleading UX.

Source: [journal contract](https://github.com/elsa-workflows/elsa-foundation/blob/0e7e7f4b23ba9c55bdb6d9062348f12cf25e4101/src/essentials/Workflows/Publishing/Core/Contracts/IPublicationManagement.cs#L8), [authority](https://github.com/elsa-workflows/elsa-foundation/blob/0e7e7f4b23ba9c55bdb6d9062348f12cf25e4101/src/essentials/Workflows/Runtime/Core/Contracts/IWorkflowActivationAuthority.cs#L6), [existing Publishing view](https://github.com/elsa-workflows/elsa-foundation/blob/0e7e7f4b23ba9c55bdb6d9062348f12cf25e4101/src/essentials/Workflows/Publishing/Api/Models/PublicationManagementViews.cs#L34), [global inspector](https://github.com/elsa-workflows/elsa-foundation/blob/0e7e7f4b23ba9c55bdb6d9062348f12cf25e4101/src/essentials/Workflows/Runtime/Api/Services/WorkflowExecutableInspector.cs#L10), [Studio artifact panel](https://github.com/elsa-workflows/elsa-foundation-studio/blob/c3be2553650e2158616bc874c97ad2b47077bd20/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowExecutables.tsx#L317), [export hook](https://github.com/elsa-workflows/elsa-foundation-studio/blob/c3be2553650e2158616bc874c97ad2b47077bd20/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/useExecutableArtifactExport.ts#L68).

## Decision 4 — Retain the atomic complete Runtime EF backend

Decision: a new support EF wrapper calls the unchanged aggregate and enrolls in the existing Runtime module/resource/migration contract, without the full feature's Resumption dependency. Preserve complete R01-R29 footprint and startup private-key checks.

Rationale: artifact, source-reference, trigger projections and activation slots/switch share RuntimeDbContext and its atomic activation boundary. The existing full/artifact-only shell features depend on Resumption, which installs redrive. The aggregate itself registers durable stores/key-validation tasks; storage registrations are not execution pumps.

Alternatives: separate activation/trigger EF registrations beside an in-memory switch break the established transaction; smaller schema/family needs separate evidence/migration work; changing existing full EF feature weakens legacy explicit composition.

Source: [atomic aggregate](https://github.com/elsa-workflows/elsa-foundation/blob/0e7e7f4b23ba9c55bdb6d9062348f12cf25e4101/src/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/DependencyInjection/RuntimeEntityFrameworkCoreRegistration.cs#L34), [coupled switch](https://github.com/elsa-workflows/elsa-foundation/blob/0e7e7f4b23ba9c55bdb6d9062348f12cf25e4101/src/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/DependencyInjection/RuntimeEntityFrameworkCoreRegistration.cs#L153), [full feature](https://github.com/elsa-workflows/elsa-foundation/blob/0e7e7f4b23ba9c55bdb6d9062348f12cf25e4101/src/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/RuntimeEntityFrameworkCoreFeature.cs#L18), [resource enrollment](https://github.com/elsa-workflows/elsa-foundation/blob/0e7e7f4b23ba9c55bdb6d9062348f12cf25e4101/src/essentials/Persistence/EntityFramework/Tooling/EfPersistenceParticipantCatalog.cs#L17), [Resumption](https://github.com/elsa-workflows/elsa-foundation/blob/0e7e7f4b23ba9c55bdb6d9062348f12cf25e4101/src/essentials/Workflows/Runtime/Resumption/WorkflowsRuntimeResumptionFeature.cs#L77).

## Decision 5 — Capability-driven Studio, preserve old combined clients

Decision: prefer new publication reads, fall back only to advertised legacy Runtime reads, and gate the three run controls by their respective relation. A publication-owned DTO/adapter must not synthesize zero runtime counts.

Rationale: absent Runtime links currently fail the artifact panel/export discovery or leave Run controls actionable. Existing full hosts continue advertising the established operations; capability-based adaptation preserves them.

Alternatives: guessed route fallback ignores capability authority; hiding all artifact management removes useful authoring; interpreting401 as absence is false evidence.

Source: [slot client](https://github.com/elsa-workflows/elsa-foundation-studio/blob/c3be2553650e2158616bc874c97ad2b47077bd20/src/essentials/Elsa.Studio.Workflows/Client/src/api/publishing.ts#L501), [workflow run control](https://github.com/elsa-workflows/elsa-foundation-studio/blob/c3be2553650e2158616bc874c97ad2b47077bd20/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowEditor.tsx#L291). Resolve current activity/published Run consumers again during implementation; source inspection is not browser proof.

## Decision 6 — Reuse existing proof owners; publish profile later

Decision: extend current Authoring host/API/provider fixtures and perform actual authenticated host/client/restart/absence actors. Final profile delivery requires public CLI-generated file consumption by a fresh process, new immutable catalog version and unchanged old pins.

Rationale: completed #1991 proved a protected combined route and three context bindings; it did not execute authenticated publication or persist Runtime artifact/activation state. Current Workbench baseline explicitly selects full Publishing/Runtime. Neither a renamed fixture nor a narrow unit mapper certifies the eventual generated host.

Alternatives: add another EF suite/matrix/cadence (unnecessary); repeat completed discovery (no new question); rewrite old catalog bytes (breaks pins); claim release from a handwritten fixture (wrong producer-consumer boundary).

Source: [historical proof](../../docs/reports/runtime-composition/authoring-host-proof.md), [existing CShell host fixture](https://github.com/elsa-workflows/elsa-foundation/blob/0e7e7f4b23ba9c55bdb6d9062348f12cf25e4101/tests/essentials/Workflows/Publishing/Persistence/EntityFrameworkCore/Tests/AuthoringCompositionHostEvidenceTests.cs#L58), [Workbench baseline](https://github.com/elsa-workflows/elsa-foundation/blob/0e7e7f4b23ba9c55bdb6d9062348f12cf25e4101/src/apps/Elsa.Workbench/shells.baseline.json#L39).

## Remaining work, not unresolved product policy

Implementation must validate the full actual DI closure, registry contributors and actual host defaults; material source findings return to the owning contract before broad edits. Backend, Studio and profile evidence remain unexecuted. Reducing the complete Runtime schema, adding new provider layouts, general settings taxonomy and broader execution architecture remain outside this milestone. Six actual builder-study participants and trusted activation/recovery remain program outcomes elsewhere.

## Decision 7 — Preserve exact selected-publication export

Decision: retain existing version-only route/factory selection and add a Read-authorized publication-identity route/producer operation. Reuse closure assembly; default interface behavior for an unadapted custom producer fails closed. Pin root artifact and carried publication source-reference identity before delivering bytes. Explicit retired-history availability/nullability preserves journal facts after normal reference/artifact GC.

Rationale: review demonstrated that the current factory independently selects a Published reference from a version's records; it cannot promise an arbitrary selected historical row. The new artifact view must not download a different publication silently.

Alternatives: narrow the new history/export task to version-only behavior (does not preserve the selected artifact); replace the old route (breaks compatibility); reconstruct pruned provenance (false evidence).

Source: [version-only producer](https://github.com/elsa-workflows/elsa-foundation/blob/0e7e7f4b23ba9c55bdb6d9062348f12cf25e4101/src/essentials/Workflows/Publishing/Services/WorkflowArtifactClosureFactory.cs#L44), [root selection](https://github.com/elsa-workflows/elsa-foundation/blob/0e7e7f4b23ba9c55bdb6d9062348f12cf25e4101/src/essentials/Workflows/Publishing/Services/WorkflowArtifactClosureFactory.cs#L84), [journal facts](https://github.com/elsa-workflows/elsa-foundation/blob/0e7e7f4b23ba9c55bdb6d9062348f12cf25e4101/src/essentials/Workflows/Publishing/Core/Models/PublicationAuthority.cs#L6).
