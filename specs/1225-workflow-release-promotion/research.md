# Workflow releases and promotion: research

Historical source baseline: `e0ba87ecfc3b1e774c509ed274b19cd3f34a1124` (5 November 2026).
Canonical request: [#3590](https://github.com/elsa-workflows/elsa-foundation/issues/3590).
This is historical source inspection and design evidence; no cross-environment runtime qualification is implied.

12 November refresh: main `dff7c490d0e5501b22d4ba89beb389c956d4f423` contains no release subtree from #3911.
That PR was closed without merge with a request for reviewable slices. The existing native/BPMN/ETL owners remain
the integration points below; individual signatures, transaction and admission behavior must be refreshed for each
production slice. The Runtime protocol router now includes an optional admission controller. Earlier reflection
fixtures and query-budget failures are not evidence of a current-main defect; see [historical evidence](historical-evidence.md).

## Decision status and owner choices

- [ADR 0043](../../docs/adr/0043-publication-slots-define-start-authority.md) is accepted (13 July 2026).
  Its 4 September ownership amendment places start authority in Runtime, reached through the activation coordinator.
  Its 1 October convergence amendment is separately **proposed, awaiting owner acceptance**; implementation observations below do not ratify it.
- [ADR 0034](../../docs/adr/0034-workflow-definitions-reconcile-from-and-export-to-git.md) remains **proposed**.
  Its Git/promotion discussion supplies context, not an accepted release-management mandate.
- The owner selected immutable workflow/dependency content, target-owned connection/secret bindings,
  unchanged running-execution pins, and equivalent Studio/API/CLI validation/application semantics.
- Subsequent owner choices: capture mapping identities per execution while preserving normal resource/secret rotation;
  allow an explicit mixed interval during coordinated activation; recover forward and offer explicit rollback.
- The owner selected native, BPMN and ETL support in the first complete delivery, including supported reusable dependencies.
  Model-specific portability and runtime-binding seams are under investigation. Other required unsupported kinds must be visible blockers, never silently omitted.

## Existing content and portability mechanisms

- [WorkflowArtifactClosureFactory](../../Essentials/src/Workflows/Publishing/Elsa.Workflows.Publishing/Services/WorkflowArtifactClosureFactory.cs)
  selects an exact definition-version's Published reference, reads executable content, and walks every declared executable dependency.
  It checks each edge's artifact/hash, rejects cycles, aggregates missing edges, and sorts collections deterministically.
- [WorkflowArtifactClosure](../../Essentials/src/Workflows/Runtime/Core/Models/WorkflowArtifactClosure.cs) format 1 contains
  one root, executable members, source-reference provenance and trigger-binding expectations.
  Carried source references/trigger bindings are not destination rows; destination activation mints/recomputes them.
- This export is deterministic for the same store state. It is not already a multi-root immutable release manifest:
  provenance can change independently of executable content, so release identity must distinguish content from observations.
- [Studio executable export](../../Studio/src/essentials/Elsa.Studio.Workflows/Client/src/api/executableArtifactExport.ts)
  resolves the advertised capability relation and returns server content untouched; keep closure ownership on the server.
- [WorkflowArtifactClosurePlanner](../../Essentials/src/Workflows/Runtime/Reconciliation/Elsa.Workflows.Runtime.Reconciliation/Services/WorkflowArtifactClosurePlanner.cs)
  validates the envelope against itself rather than borrowing missing children from the destination store.
- [WorkflowArtifactReconciler](../../Essentials/src/Workflows/Runtime/Reconciliation/Elsa.Workflows.Runtime.Reconciliation/Services/WorkflowArtifactReconciler.cs)
  validates received canonical hashes, version conflicts, runtime requirements and binding expectations, then activates dependencies first.
  It is a mounted-source reconciliation lifecycle with source ownership/latest-version behavior, not a reviewed staging-only deployment API.
- [WorkflowExecutable](../../Essentials/src/Workflows/Runtime/Core/Models/WorkflowExecutable.cs) already carries exact dependencies,
  runtime consumer/schema requirements and storage-driver requirements. These are prerequisites, not permission to upgrade a host.
- [WorkflowExecutableCompiler](../../Essentials/src/Workflows/Publishing/Elsa.Workflows.Publishing/Services/WorkflowExecutableCompiler.cs)
  reads exact activity versions, publications and executable templates for reusable activities.
  The current closure envelope has no separate authored-definition/template collections; assess their required portable representation explicitly.
- [Git reconciliation/export](../../Essentials/src/Workflows/Design/Reconciliation/Git/README.md) already distributes immutable
  authored workflow versions over the operational catalog, using canonical content and fast-forward export.
  It does not by itself transfer the complete executable/reusable/module closure or provide deployment history.

## Existing activation and runtime behavior

- [IWorkflowActivationCoordinator](../../Essentials/src/Workflows/Runtime/Core/Contracts/IWorkflowActivationCoordinator.cs)
  is the runtime-owned activation/deactivation entry point; publication records follow the slot rather than select traffic.
- [WorkflowActivationSlot](../../Essentials/src/Workflows/Runtime/Core/Models/WorkflowActivation.cs) owns one definition/slot,
  active activation identity, source owner and revision. No inspected contract promises a transaction across several slots/domains.
- [WorkflowActivationCoordinator](../../Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Executables/WorkflowActivationCoordinator.cs)
  currently treats the same artifact and tenant as already active, subject to ownership checks.
  [PublishWorkflowRequestHandler](../../Essentials/src/Workflows/Publishing/Elsa.Workflows.Publishing/Handlers/PublishWorkflowRequestHandler.cs)
  also returns an already serving artifact. Binding-only activation requires both equality paths to consider binding identity.
- [PublicationActivator](../../Essentials/src/Workflows/Publishing/Elsa.Workflows.Publishing/Services/PublicationActivator.cs)
  completes journal state from the serving slot and records a losing same-artifact candidate separately.
  Reuse this separation when distinguishing activation success from incomplete release-wide coordination.
- [WorkflowExecutionState](../../Essentials/src/Workflows/Runtime/Core/Models/WorkflowExecutionState.cs) retains exact executable,
  source provenance, partition and authority. Activation/rollback must preserve these execution facts.
- [WorkflowStartDispatcher](../../Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Executions/WorkflowStartDispatcher.cs)
  resolves live/saved/retained/model authority before serializing durable start work; keyed duplicates can resolve before current eligibility.
  Deployment bindings must be captured with that selected authority and participate in duplicate validation.

## Existing target, authorization and resource behavior

- [Studio Backends](../../Studio/src/essentials/Elsa.Studio.Backends/README.md) supplies configured/source-owned and personal
  profiles, independent authentication, shell-relative discovery and backend-qualified resource URLs. Reuse it for selection.
- [StudioBackendProfiles](../../Studio/src/essentials/Elsa.Studio.Backends/StudioBackendProfiles.cs) scopes personal metadata
  by subject, tenant and host, with conditional revision writes. A personal profile ID is not a durable destination identity.
- [StudioBackendConnection](../../Studio/src/essentials/Elsa.Studio.Hosting/Client/src/sdk/backendConnection.ts) freezes
  backend/identity lifetime, binds requests to the selected shell, aborts identity changes and avoids automatic write retries.
- [BackendConnections](../../Studio/src/essentials/Elsa.Studio.Backends/Client/src/connections.ts) prepares target authentication
  for discovery, but switching reloads Home and retires the source document. Promotion needs a supported prepared-target transfer
  operation or portable import flow; do not create an arbitrary remote proxy or forward source credentials.
- [API capabilities](../../Essentials/src/Api/Capabilities/Elsa.Api.Capabilities/README.md) are caller-neutral versioned
  contracts. Availability/dormancy, feature configuration, action permissions and target readiness remain distinct.
- [Connections API](../../Essentials/src/Connections/Api/ConnectionsApi.cs) derives tenant from the authenticated principal.
  Reading/picking uses Workflow Design read or Connections manage; administration and connection tests have separate permissions.
- [IConnectionPermissionGate](../../Essentials/src/Connections/Core/Contracts/IConnectionPermissionGate.cs) deliberately leaves
  workflow binding under Workflow Design manage; do not invent a parallel Connections use permission.
- [ConnectionDraftValidator](../../Essentials/src/Connections/Design/Validators/ConnectionDraftValidator.cs) blocks missing/type
  mismatches but warns for authorization needed. It runs on draft promotion, not every immutable ingest route.
- [ConnectionReference](../../Essentials/src/Activities/Runtime/Core/Models/ConnectionReference.cs) contains only name/type.
  [HttpConnectionApplicator](../../Essentials/src/Connections/Workflows/Services/HttpConnectionApplicator.cs) resolves that name directly in the execution partition.
- [SecretValueRuntimeResolver](../../Essentials/src/Secrets/Workflows/SecretValueRuntimeResolver.cs) passes authored references unchanged;
  [SecretLifecyclePolicy](../../Essentials/src/Secrets/Elsa.Secrets/Services/SecretLifecyclePolicy.cs) chooses the latest active secret version.
- [IResolvedConnection](../../Essentials/src/Connections/Core/Contracts/IResolvedConnection.cs) exposes revision/incarnation,
  but public [ConnectionMetadata](../../Essentials/src/Connections/Core/Models/ConnectionModels.cs) omits them.
  [IConnectionRepository](../../Essentials/src/Connections/Core/Contracts/IConnectionRepository.cs) has current lookup/CAS, not historical lookup;
  [EF Save](../../Essentials/src/Connections/Persistence/EntityFrameworkCore/Stores/EfConnectionRepository.cs) overwrites the current payload.
  A revision observation cannot reconstruct old connection settings. Pin target incarnation and retain ordinary rotation/revocation.

## Proposed seams and qualification risks

- Direct BPMN's visible publication/root path and ETL's standalone catalog require their own exact import/selection
  behavior; see the source-linked [model artifact contract](contracts/model-artifacts.md). Native-only export is insufficient.
- Existing Modularity `IFeatureManagementService` catalog reports package ID/version/manifest hash and live availability,
  but its shell revision is not a package/capability inventory revision. Hash the observed non-secret prerequisite projection;
  distinguish installed, loaded/descriptor-available, enabled, dormant and operation-qualified states. Host-bundled or unknown
  package provenance must remain explicit; assembly versions are not package-version evidence.
- Existing Publishing permission keys are declared by its Design-bearing API. The Runtime-safe target API must reuse
  the canonical permission catalog without duplicate contributor keys or dragging source implementations into its package graph.
- Add a bounded immutable runtime-owned mapping snapshot to activation source selection and durable execution state,
  separate from executable content identity. Resolve mappings without runtime dependencies on release/design catalogs.
- Propagate it through start command/checkpoint payloads, saved stimulus recipients, child dispatch records/outbox payloads,
  and supported model/native worker bridges. Null denotes legacy direct-name behavior, never lookup of the latest deployment.
- [ActivitySecretInputResolver](../../Essentials/src/Activities/Runtime/Elsa.Activities.Runtime/Services/ActivitySecretInputResolver.cs)
  already reads execution state to enforce partition/tenant agreement; use a typed mapping read seam before module resolution.
  Connections/Secrets must verify expected target incarnation on the actual resource read; mappings cannot select another tenant.
- [DispatchWorkflow](../../Essentials/src/Activities/DispatchWorkflow/Runtime/Activities/DispatchWorkflow.cs) and
  [ChildStartExecutor](../../Essentials/src/Activities/DispatchWorkflow/Runtime/Services/ChildStartExecutor.cs) must retain parent mappings
  across waiting/fire-and-forget dispatch, retries and restart. Reusable activities in one execution share its mapping snapshot.
- Same content plus changed bindings creates a new activation through existing owners; equal content/bindings can converge as no-op.
  Coordinated mixed intervals, per-member completion and supersession require durable observable recovery, not a claimed group transaction.
- [CLI](../../Essentials/src/Cli/Elsa.Cli/ElsaCli.cs) currently exposes persistence/composition; add workflow release commands
  over the same deployment API rather than using the package/schema migration worker.
- Coordinate [#3588](https://github.com/elsa-workflows/elsa-foundation/issues/3588) and [#3892](https://github.com/elsa-workflows/elsa-foundation/issues/3892)
  for exact-candidate evidence; no shipped suite-result schema or mandatory universal test gate is assumed here.
  Content evidence and target/binding integration evidence must remain distinguishable; controlled substitutes prove only their exercised boundary.
- Follow [Studio UX acceptance](../../docs/contributing/studio-ux-acceptance.md), using Workflow Definitions/Connections
  and [shared module controls](../../Studio/docs/module-ui.md). Cover reopen, denied/read-only, stale, failed/partial/recovering,
  no-op, narrow/themes, keyboard/focus and actual two-target composed journeys with downloadable current-commit visual evidence.
- Unverified: portable authored/reusable retention completeness, mapping incarnation enforcement, binding-only activation,
  cross-provider recovery, target identity discovery and all acceptance journeys. Source inspection is not delivery proof.
