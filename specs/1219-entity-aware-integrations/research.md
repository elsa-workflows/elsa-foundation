# Research: scoped catalog admission and disclosure

**State:** Phase 0 decisions selected for the owner-approved catalog unit; technical review and implementation remain outstanding.
**Source:** 01bfb391886aeb60080d9afe186bcd29a0851a09. The [approval receipt](https://github.com/elsa-workflows/elsa-foundation/pull/3923#issuecomment-6675758688) binds the boundary proposal at 1bf3d5955a42222a5b62e4b0d463e17c43eedd16.

## Decisions

| Decision | Rationale | Alternatives assessed |
|---|---|---|
| Activities-owned requirement and evaluator; optional source policy | Every catalog consumer needs the same decision while source credentials/semantics stay optional. The new independently composable Disclosure feature keeps implementation out of Core. | Provider-key-only gates and picker decorators leave detail/construct/options/history paths open. Expanding the activities-only Integrations SDK would mix host behavior into that package. |
| Explicit Public/Protected metadata retained with versions/publications and lineage | A reader can require the keyed policy even when its module is absent. Provenance strings/facets alone are not an enduring security signal. | SourceKind/SourceId are provenance, excluded from behavior hashing, and may change through forks; arbitrary facets can be stripped by projections. |
| Actual current-scope producer admission | Installed server composition, rather than source strings or request bodies, owns classification. Registries retain factories/metadata, never scoped source instances or authorization results. | A type/key-only match could let another instance select public admission; retaining a startup-scoped source breaks later scopes. |
| Dependency closure inherits requirements | New Public wrappers can copy private structured child metadata without a fork. Union exact transitive dependency requirements and retain them monotonically. | Provider identity and fork lineage alone cannot prevent laundering through composition. |
| Definition union and exact material requirement | Shared mutable header/head/count data may reflect a private newer child even when an old child is public. Every header-bearing projection must guard both. | Guarding only the selected version or only management summaries leaves palette/construct leaks. |
| Complete bounded authorized selection before provider count/page | Protected decisions cannot be safely dropped after paging or totals. Initial capacity is 256 non-public or undecidable resource keys per read; overflow is unavailable. | Unbounded policy queries and truncated successful counts are not supported. A larger joinable authorization index is future optimization. |
| Unavailable is the zero/default decision; explicit validated Allowed only | Missing/unknown/deserialized/default outcomes cannot grant. | An Allowed-zero enum is unsafe for a security boundary. |
| Lossless machine identity in a new historical snapshot shape | The existing assistant display projection clips IDs to 256 while persistence permits 450. A private long ID can alias a public prefix. | Rechecking only the clipped ID can authorize the wrong record; lookup-not-found is not sufficient. Legacy uncertified capture requires a fresh session. |
| No positive permission cache across passes | One HTTP stream can contain several writes/model/tool calls with intervening revocation. Existing Agent admission passes define the correct boundary. | Whole-request/session memoization extends authority too far. |
| Fresh ActivitiesDesign format 2.0.0 | The accepted pre-GA policy regenerates affected Initials and provides no real existing-preview-store upgrade path. A distinct stamp gives old-reader refusal without inventing a finalization override. | Same-version nullable scaffolding is mechanically possible but does not protect populated mandatory metadata from old readers. An offline admitted-store transition or rolling predecessor path would be separate work. |

## Storage decision and its limits

[ADR 0077](../../docs/adr/0077-a-module-upgrades-in-place-only-when-its-persisted-schema-is-unchanged.md) separates schema skew from corruption. [ADR 0078](../../docs/adr/0078-workflow-executions-are-virtual-actors-and-cluster-membership-is-a-foundation-contract.md#consequences) explicitly keeps regenerated baselines before 4.0 stable. This unit follows that current policy: only the four ActivitiesDesign Initial sets are regenerated, and qualification uses owned fresh stores.

A current-only format-2 context cannot activate an admitted format-1 store merely by changing the version constant: existing finalized and completion records remain 1.0.0. No upgrade/import, family-finalization reset or force command is selected. Existing stores remain untouched and unsupported; parked baseline-freeze #1976 is not reactivated.

Atomic format-1 rows retain request hashes, not reconstructable request JSON. Their unit-list field is not the old request. Therefore this unit does not rehash/import old receipts or try an old fingerprint after a new comparison fails. New format-2 receipts bind security material explicitly; unsupported stored material versions refuse. This is a scoped compatibility limit, not replay qualification for an old database.

Within supported format 2, missing/unclassified material remains Unspecified until a trusted exact owner inventory matches its identity, provenance, provider/consumer schemas and canonical content. Unknown custom material is never classified public by null tenancy or a CLR label. Normal new admission requires an explicit valid requirement. Classification is a separate command; reads never change data.

## Current source anchors

- [Catalog owner port](../../Essentials/src/Activities/Design/Core/Contracts/IActivityAuthoringCatalog.cs) accepts no caller principal/tenant and owns the existing broad catalog gate.
- [Palette projection](../../Essentials/src/Activities/Design/Api/Services/ActivityAuthoringCatalogReader.cs), [management projection](../../Essentials/src/Activities/Design/Api/Services/ActivityDefinitionManagementProjectionService.cs), [construct](../../Essentials/src/Workflows/Publishing/Api/Endpoints/Activities/Construct/Endpoint.cs) and [options](../../Essentials/src/Workflows/Design/Api/Services/ActivityInputOptionsAuthoringService.cs) are distinct enforcement sites.
- [Reconciliation collection](../../Essentials/src/Activities/Design/Reconciliation/Elsa.Activities.Design.Reconciliation/Handlers/CollectActivityVersions.cs) and [reconciler](../../Essentials/src/Activities/Design/Reconciliation/Elsa.Activities.Design.Reconciliation/Services/ActivityVersionReconciler.cs) currently support source-publisher and direct-store routes; both need admission.
- [Atomic write](../../Essentials/src/Activities/Design/Persistence/EntityFrameworkCore/EfDesignAtomicWrite.cs) hashes explicit request material; [publication commit](../../Essentials/src/Activities/Design/Persistence/EntityFrameworkCore/Stores/EfActivityPublicationDesignCommit.cs) has duplicate/recovery comparisons.
- [Direct version contract](../../Essentials/src/Persistence/EntityFramework/IEfSchemaVersionedContext.cs), [interceptor](../../Essentials/src/Persistence/EntityFramework/EfSchemaVersionMaterializationInterceptor.cs), [finalization check](../../Essentials/src/Persistence/EntityFramework/SchemaFinalization/EfSchemaFinalizationCheck.cs) and [module generator](../../tools/ef/generate-module-migrations.sh) establish the format/migration limits.
- [Assistant catalog display projection](../../Extensions/Agent/src/Workflows/Elsa.Agent.Workflows/Services/WorkflowActivityCatalog.cs), [owner history admission](../../Extensions/Agent/src/Workflows/Elsa.Agent.Workflows/Services/WorkflowAgentServices.cs) and [admission pass](../../Extensions/Agent/src/Elsa.Agent/Services/AgentDisclosureHistory.cs) establish the identity/refusal/lifetime changes.

## Separate feature gates

Runtime owners now recognize the no-TriggerId embedded-wait requirement and the common SPI/C2 source direction: [response](https://github.com/elsa-workflows/elsa-foundation/pull/3912#issuecomment-6674277730), [correction](https://github.com/elsa-workflows/elsa-foundation/pull/3912#issuecomment-6674842384), [shared checkpoint](https://github.com/elsa-workflows/elsa-foundation/issues/2892#issuecomment-6675158127). That is convergence, not an enabled/qualified Event route or an adopted entity ingress ABI.

Delivered Decisions supplies a current persisted-user authorization pattern; it does not supply autonomous workflow-to-source grants. Real source-use/workload policy, copied Runtime-schema disclosure, entity SDK, source credential lifecycle, events and projections remain separate. They are outside this catalog plan's Technical Context rather than unresolved assumptions used by its implementation.

The source-cookie, actual machine-source/stock HTTP and synthetic public-extension witnesses remain feasibility evidence. None qualifies the catalog security unit. Phase 1 and C01–C12 still require review and observed implementation evidence.
