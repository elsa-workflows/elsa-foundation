# Data, query and authorization research

**Feature:** [#3604](https://github.com/elsa-workflows/elsa-foundation/issues/3604)
**Inspected:** 6 November 2026, repository base `e0ba87ecfc3b1e774c509ed274b19cd3f34a1124`
**Status:** Source inventory and proposals for review. Proposed contracts, metric choices and package boundaries below are not architecture agreement or implementation evidence.

## Existing owning sources

| Concern | Owning source | Current behavior and limitation |
|---|---|---|
| Fixed workflow statistics | [`WorkflowRunHealthContracts.cs`](../../Essentials/src/Workflows/Dashboard/Elsa.Workflows.Dashboard/WorkflowRunHealthContracts.cs), [`WorkflowRunHealthService.cs`](../../Essentials/src/Workflows/Dashboard/Elsa.Workflows.Dashboard/WorkflowRunHealthService.cs) | Started, outcome, incident and running counts; hourly/daily buckets; failure percentages; highest-failure definitions. These are fixed questions, not a saved-query grammar. |
| Dashboard transport and permission | [`WorkflowsDashboardApi.cs`](../../Essentials/src/Workflows/Dashboard/Elsa.Workflows.Dashboard/WorkflowsDashboardApi.cs), [`WorkflowsDashboardPermissions.cs`](../../Essentials/src/Workflows/Dashboard/Elsa.Workflows.Dashboard/WorkflowsDashboardPermissions.cs) | Read endpoints obtain the tenant from the authenticated principal and require `workflows/dashboard:read`. No question/dashboard persistence, dataset discovery or audience ACL is established here. |
| Durable run-health query | [`EfWorkflowRunHealthDataSource.cs`](../../Essentials/src/Workflows/Dashboard/Persistence/EntityFrameworkCore/Stores/EfWorkflowRunHealthDataSource.cs) | Checks persistence scope against the tenant; validates row envelopes/content; bounds buckets and source rows. It does not evaluate per-run inspection authorization. |
| Run-health projection | [`WorkflowRunHealthStateEntity.cs`](../../Essentials/src/Workflows/Runtime/Persistence/EntityFrameworkCore/Entities/WorkflowRunHealthStateEntity.cs) | Stores execution/definition identities, run kind, start time, current status and incident counts. No completion time, duration, folder or business dimension. |
| Runtime retained state | [`WorkflowExecutionState.cs`](../../Essentials/src/Workflows/Runtime/Core/Models/WorkflowExecutionState.cs), [`WorkflowExecutableSourceProvenance.cs`](../../Essentials/src/Workflows/Runtime/Core/Models/WorkflowExecutableSourceProvenance.cs) | Contains start/completion timestamps, status, tenant, run kind and pinned source identity. Root variables and system metadata are not an authorized business-query catalog. |
| Bounded history reads | [`IWorkflowExecutionStateStore.cs`](../../Essentials/src/Workflows/Runtime/Core/Contracts/IWorkflowExecutionStateStore.cs), [`WorkflowExecutionStatePage.cs`](../../Essentials/src/Workflows/Runtime/Core/Models/WorkflowExecutionStatePage.cs) | Provider-neutral pages and query-bound cursors. History date semantics differ from run-health cohort semantics. |
| Record/value inspection | [`IActivityInspectionContextAsync.cs`](../../Essentials/src/Workflows/Runtime/Api/Contracts/IActivityInspectionContextAsync.cs), [`WorkflowInstanceListService.cs`](../../Essentials/src/Workflows/Runtime/Api/Handlers/WorkflowInstanceListService.cs) | Structure and captured-value grants are separate. The authorized list path filters before forming items, counts and cursors. |
| Folder and tag organization | [`IWorkflowLibraryStore.cs`](../../Essentials/src/Workflows/Design/Persistence/Core/Stores/IWorkflowLibraryStore.cs), [`WorkflowLibraryModels.cs`](../../Essentials/src/Workflows/Design/Persistence/Core/Models/WorkflowLibraryModels.cs) | Design owns current workflow organization, folders, tags, saved views and favorites. Runtime source attribution does not pin folder membership. |
| Design query visibility | [`IWorkflowLibraryVisibilityPolicy.cs`](../../Essentials/src/Workflows/Design/Persistence/Core/Contracts/IWorkflowLibraryVisibilityPolicy.cs) | Supplies trusted restrictions and a policy revision before querying the inventory. Post-filtering an already-paged inventory is insufficient. |
| Preferences | [`StudioPreferenceModels.cs`](../../Essentials/src/Studio/Preferences/Core/Models/StudioPreferenceModels.cs), [`StudioPreferenceContracts.cs`](../../Essentials/src/Studio/Preferences/Core/Contracts/StudioPreferenceContracts.cs) | Registered bounded documents keyed by subject, tenant, host and namespace, with schema/revision checks. Preserve Home layouts here; this is not a shared-resource lifecycle. |
| Existing saved-view precedent | [`EfWorkflowLibraryStore.Views.cs`](../../Essentials/src/Workflows/Design/Persistence/EntityFrameworkCore/Stores/EfWorkflowLibraryStore.Views.cs) | Tenant-scoped owner/shared views, quotas, revision checks and keyed creates. Sharing is a tenant-wide boolean; it does not supply audience-specific dashboard collaboration. |

[`WorkflowsDashboardFeature.cs`](../../Essentials/src/Workflows/Dashboard/Elsa.Workflows.Dashboard/WorkflowsDashboardFeature.cs) selects a real in-memory run-health source only when compatible in-memory runtime stores are present; otherwise its default reports unavailable. The EF satellite supplies a separate durable reader. A composed feature or successful dependency resolution therefore does not establish dataset availability.

No declared dashboard business-dimension discovery/exposure contract was found in the inspected source. This is a source-inventory finding, not proof that every extension or external application lacks a business projection.

## Metric and time semantics that must remain explicit

Current run-health counts the cohort whose `StartedAt` lies in the inclusive/exclusive interval `[from,to)`. Outcomes are those runs' **current** statuses at query time. Failure percentage is `100 × Faulted / Started`, rounded to two decimal places away from zero. All started runs contribute to the denominator, including incomplete ones; a zero denominator currently yields zero. These facts describe the existing widget, not an approved definition for every new metric.

Excluding test runs removes only explicit `WorkflowRunKind.TestRun`. Legacy `Unknown`, published runs and background Weaver runs remain distinguishable classifications. A label such as “production runs” must not silently mean “anything except TestRun.”

The history query's effective timestamp is `UpdatedAt ?? CompletedAt ?? StartedAt ?? CreatedAt`, with an inclusive upper boundary. Its `From`/`To` filters cannot stand in for a started-cohort filter. A new query must declare its time field and boundary convention and use a reader that implements them.

“Failure rate increased this week” requires an explicit baseline, week boundary, time zone and denominator. A partial current week compared with a complete previous week can mislead. Proposed authoring should clarify or visibly confirm the comparison, such as the same elapsed portion of the previous local calendar week. No historical status-at-instant series was established: comparing cohorts using their current outcomes must be described that way.

Duration can be derived from valid retained `StartedAt`/`CompletedAt` pairs. The source must define included outcomes, missing timestamps, invalid intervals and whether pending/running work is excluded. Missing duration is not zero. An aggregate should expose the contributing count and omitted/unknown count where authorized, together with units and interpretation.

Folder enrichment is currently a Design read of **current** membership. Historical folder-at-start attribution would require new captured data. A Dashboard satellite may compose authorized Design and Runtime read contracts; Runtime itself must not acquire a Design reference. A missing or unauthorized definition/folder must not leak its label through an aggregate or be silently renamed to a normal unfiled group.

## Retention and bounded execution

The existing EF run-health source refuses more than 744 buckets or more than 100,000 source rows in each started/running traversal; it does not silently truncate. Cancellation flows to the source. These are useful bounded-read precedents, not automatically the limits for the new contract.

Retained rows do not prove complete history. [`EfWorkflowExecutionStateStore.cs`](../../Essentials/src/Workflows/Runtime/Persistence/EntityFrameworkCore/Stores/EfWorkflowExecutionStateStore.cs) deletes the run-health projection when an execution is deleted. Existing contracts expose no authoritative collection/retention coverage declaration. The earliest surviving row is not a completeness watermark.

Proposed dataset metadata/results should distinguish known complete coverage, known gaps and unknown coverage, with the available collection/retention boundary only when the module can prove it. Bound requested time, scanned rows, output groups, result rows and execution time. Exhaustion should return an actionable cost/coverage outcome; it must not present an arbitrary prefix as a complete answer.

## Proposed shared contract and module responsibilities

The proposed shared query boundary is a validated definition, with no generated SQL, scripts or executable custom widget code. Dataset-owning modules retain their semantics and data access.

1. **Discovery:** authorized stable dataset identity, owning module, schema revision, understandable description, typed fields, explicitly declared business dimensions, field sensitivity/access requirements, supported operators, module metrics, units, null/denominator rules, time basis, coverage capabilities and declared relationships.
2. **Definition:** dataset/schema reference; bounded typed filters; supported metric or aggregation; grouping; deterministic sorting; time range; typed parameters; optional supported comparison period; visualization configuration. Unknown fields/operators and undeclared relationships fail validation.
3. **Validation:** generic structural/type/cost validation plus module semantic validation. The module confirms that the requested question is representable and that metric, field and relationship revisions remain usable. A stale schema/metric returns recovery guidance rather than a substitute source.
4. **Execution:** the owning module obtains the complete authorized input universe, applies authorized field/record restrictions before aggregation, computes authoritative metrics and returns bounded results, interpretation, coverage and drill references. Shared orchestration never branches on workflow versus business dataset names.
5. **Reads and recovery:** revalidate availability/access on preview, refresh, drill-down and export where supported. Missing modules, unsupported questions, incompatible schemas, denied fields and exhausted budgets remain actionable outcomes.

An initial profile can forbid arbitrary cross-dataset joins while allowing a module to expose a reviewed relationship/enrichment it owns. Workflow-to-current-folder enrichment then belongs in a Workflows Dashboard adapter with both owning authorization decisions, not in the central assistant or Runtime.

Use separate concepts for result availability, coverage and refresh state. A valid zero, a successful empty set, unavailable source, incomplete/unknown coverage, stale previous result, failed refresh and cancellation carry different meanings. Optional explanations must reference actual returned values/interpretation and cannot invent missing fields, causes or coverage.

## Authorization, sharing and permission propagation

Saved question/dashboard definition access and data access are independent. Proposed resources need tenant/owner identity, explicit audiences and edit/copy rights, optimistic concurrency, save idempotency and revision/history. Definition access must never adopt the author's principal or transfer the author's result cache. Sharing should not automatically share conversations, prompts, preview records or explanations containing privileged values.

Data authorization must cover discovery metadata, filter/group fields, aggregate visibility and drill records. A filter on a protected field can disclose membership even when that field is absent from the output. Dataset/field denial must therefore fail meaningfully before execution, rather than filter an aggregate afterward. The existing fixed run-health reader's tenant check alone is insufficient for these guarantees.

[`AuthorizationContracts.cs`](../../Essentials/src/Foundation/Identity/Core/Authorization/AuthorizationContracts.cs) provides the canonical permission evaluator/resource-handler seams. [`IamContracts.cs`](../../Essentials/src/Foundation/Identity/Core/Iam/IamContracts.cs) provides tenant-scoped users, roles and memberships. ACLs should reference canonical identities; display names and external provider subjects are not interchangeable identity keys.

Permission propagation needs precise qualification. [`PermissionAuthorizationService`](../../Essentials/src/Foundation/Identity/Elsa.Foundation.Identity/Authorization/AuthorizationServices.cs) re-evaluates trusted normalized request claims and resource handlers. Its default claims evaluator does not reread current IAM records. `ICurrentSubjectPermissionAuthorizer` has a fail-closed `Unsupported` default; the [`AspNetCoreIdentitySubjectAuthorizer`](../../Essentials/src/Foundation/Identity/AspNetCoreIdentity/Elsa.Foundation.Identity.AspNetCoreIdentity/Services/AspNetCoreIdentitySubjectAuthorizer.cs) rehydrates current user, roles and memberships before a permission decision. [`OwnershipContracts.cs`](../../Essentials/src/Foundation/Identity/Core/Ownership/OwnershipContracts.cs) declares immediate server-side versus token-refresh propagation, with external OIDC defaulting to the latter. Endpoint reauthorization alone is not evidence of immediate role revocation.

Proposed initial cache policy: do not persist/share server result caches. Execute each refresh as the current viewer and clear client results on subject/tenant changes. Immediate saved-ACL revocation is then independent of provider-specific data-grant propagation. If result caching is later introduced, its partition must include tenant, viewer/effective scope, query/parameters, schema/metric revisions and a valid policy revision; the existing inspection-profile hash is not a complete per-record policy/cache identity.

## Independent business-module witness

Use a separate reference business module that owns explicit processing records and their normal write/read capability. It deliberately contributes `customerCategory`, valid processing timestamps/duration semantics and its authorization policy through the same discovery/validation/execution contracts. Produce or seed persisted records through that module, then independently calculate expected group counts and durations. Include a protected category/record and a second tenant to prove authorization before aggregation.

This is a proposed reference integration, not an existing Elsa customer dimension. A separate workflow adapter must prove native Elsa run data. Neither witness may be a central hard-coded dataset branch or implicit exposure of all variables. The business witness should demonstrate that adding module data requires a module contribution rather than changing assistant orchestration or the generic renderer.

## Decisions requiring review

| Decision | Why it matters |
|---|---|
| Owning package/resource boundary | A generic Dashboard contract and saved resources may belong under `Elsa.Studio.Dashboard`; Workflows and business adapters retain domain ownership. This boundary needs review before implementation. |
| First query/comparison profile | Must express a novel question and the requested period comparison without becoming an arbitrary execution language. |
| Failure-rate/cohort semantics | Denominator, incomplete runs, historical comparison and legacy run classification affect the answer. |
| Folder interpretation | Current membership and historical membership are different questions with different data requirements. |
| Explicit business projection lifecycle | Defines who exposes a field, capture/update semantics, field authorization, schema changes and historical coverage. |
| Audience/edit/copy policy | Establishes ownership, collaboration, revocation and shared-question reuse without granting source access. |
| Permission propagation support | Defines which provider configurations can prove immediate revocation and how token-refresh boundaries are represented. |
| Cost, coverage and cache policy | Prevents uncontrolled reads and false completeness while keeping ordinary refresh independent of a model turn. |

Both [constitutions](../../.specify/memory/constitution.md) apply, including the [framework](../../.specify/memory/constitution-framework.md): `.Core` contracts carry no implementation dependency; additive cross-domain discovery uses named contribution events; implementations default to internal/scoped; persistence invariants hold across providers; and Runtime gains no Design dependency. This proposal does not rely on draft/provisional activity-versioning or sanctioned-pattern sections as ratified authority.
