# Catalog disclosure contracts

**Status:** Candidate signatures for technical review of the owner-approved catalog boundary; not final public ABI or approval of the whole entity feature.
**Scope:** [data model](../data-model.md), [boundary](../design/catalog-disclosure.md), [plan](../plan.md). Runtime ingress/workload grants, entity SDK and production private-source enablement remain gated.

## Ownership and composition

`Elsa.Activities.Design.Core` owns generic values, evaluator/policy and admission/owner ports, reconciliation-visible origin/candidate/wrapper values, and the named registration event. `Elsa.Activities.Design.Persistence.Core` owns canonical store-backed reference authorization and persistence CQS/store contracts. Implement orchestration in a new independently composed `Elsa.Activities.Design.Disclosure` feature, proposed shell name `ActivityCatalogDisclosure`.

API/Publishing/reconciliation consumers depend on Core contracts and declare the named feature dependency. Workbench/qualification hosts explicitly install the feature package; no unrelated implementation reference forces activation. Missing/duplicate evaluator or admission service fails startup/refuses; no default-allow or public-only fallback. Core gains no Foundation Identity/ASP.NET dependency. Identity/snapshot values and the listing result stay in Activities.Design.Core; the Persistence.Core authorizer consumes those values, avoiding a reverse Core dependency. Keep interactive gates outside the authorization-free Publishing engine; trusted write admission remains independent of an HTTP caller. No Runtime → Design dependency.

## Requirement and resource values

```csharp
public enum CatalogDisclosureKind { Unspecified = 0, Public = 1, Protected = 2 }
public enum CatalogDisclosureStatus { Unavailable = 0, Allowed = 1, Denied = 2 }
public enum CatalogDisclosurePurpose { Metadata, Construct, Options, Authoring, Historical }
public enum CatalogOwnerScopeKind { Unspecified = 0, Global = 1, Tenant = 2 }
public sealed record CatalogOwnerScope(CatalogOwnerScopeKind Kind, string? TenantId);
public sealed record CatalogDisclosurePolicyRequirement(
    string PolicyKey, string BindingSchema, JsonElement Resource);
public sealed record CatalogDisclosureRequirement(
    int SchemaVersion, CatalogDisclosureKind Kind,
    IReadOnlyList<CatalogDisclosurePolicyRequirement> Policies);
public sealed record CatalogDisclosureResource(
    string Kind, string Id, CatalogOwnerScope OwnerScope,
    CatalogDisclosureRequirement Requirement);
public sealed record CatalogDisclosureDecision(
    CatalogDisclosureStatus Status, string? AuthorizationRevision = null);
public sealed record CatalogDisclosureIdentity(
    string Kind, string? DefinitionId, string VersionId, CatalogOwnerScope OwnerScope);
public sealed record CatalogDisclosureSnapshot(
    int SchemaVersion, IReadOnlyList<CatalogDisclosureIdentity> Identities);
```

Resource kinds are a closed validated set: definition, version, draft, publication and registered builtin; unknown kinds are unavailable. Representation 1 uses explicit Public/Protected and a bounded canonical conjunction. Public has no policies; Protected has at least one. The initial profile permits at most 64 policy requirements, 128-character policy keys, 64-character binding schema keys, JSON depth 16, 16 KiB UTF-8 per resource binding and 256 KiB per canonical requirement. Exceeding a bound refuses rather than truncates. Clone JSON/collections. Missing/unknown/malformed data is unavailable; only explicit Allowed grants disclosure. Scope is canonical resource ownership, never caller execution authority. A definite Denied wins over another unavailable policy.

## Current-authority evaluation — Core

```csharp
public interface IActivityCatalogDisclosureEvaluator // replacement: exactly one per context
{
    ValueTask<IReadOnlyList<CatalogDisclosureDecision>> EvaluateAsync(
        IReadOnlyList<CatalogDisclosureResource> resources,
        CatalogDisclosurePurpose purpose, CancellationToken cancellationToken = default);
}
public interface IActivityCatalogDisclosurePolicy // keyed strategy, not a replacement
{
    ValueTask<CatalogDisclosureDecision> EvaluateAsync(
        CatalogDisclosureResource resource, CatalogDisclosurePolicyRequirement requirement,
        CatalogDisclosurePurpose purpose, CancellationToken cancellationToken = default);
}
```

Inputs to the evaluator are canonical loaded resources; output count/order must match input exactly, and null, missing, unknown or malformed decisions are unavailable. Policies receive persisted owner/ID facts so binding JSON cannot replace them; they read host-established current authority through their injected owner adapter. No authored principal, permission string, tenant-to-run-as or retained grant argument exists. No request identity means protected disclosure cannot allow. Missing policy key/schema returns unavailable even when its provider is absent. Endpoint/module permissions remain additional gates.

Use `ActivityCatalogDisclosureInitializing : IEvent`, a get-only `ICollection<CatalogDisclosureRegistration>` initialized to `[]`, and exactly one `CollectActivityCatalogDisclosureRegistrations` handler over `IActivityCatalogDisclosureRegistrationSource.Read()`. Publish Sequentially at startup and freeze metadata/factories. Reject duplicate policy/admission keys and ambiguous producer registrations. Keyed strategy dispatch uses the registry; additional fan-in must use a named event/single aggregator, following [§2.6](../../../.specify/memory/constitution-framework.md#L104).

The singleton registry never captures scoped producers, service providers or startup instances. Resolve policy implementations scoped. No new positive authorization cache; a bounded batch lasts only until the next write/model call/tool execution. Cancellation propagates; owner failures become unavailable with safe fixed outward diagnostics and retained inner cause.

## Canonical reference authorization — Persistence.Core

```csharp
public interface IActivityCatalogDisclosureAuthorizer // replacement
{
    ValueTask<CatalogDisclosureDecision> AuthorizeVersionsAsync(
        IReadOnlyCollection<string> activityVersionIds, CatalogDisclosurePurpose purpose,
        CancellationToken cancellationToken = default);
    ValueTask<CatalogDisclosureDecision> AuthorizeReferencesAsync(
        CatalogDisclosureSnapshot snapshot, CatalogDisclosurePurpose purpose,
        CancellationToken cancellationToken = default);
}
```

Load under existing trusted persistence access and ordinally validate own/global loaded ownership; never switch scope from a reference. Validate exact definition/version relationship and reject ambiguity. Every header-bearing projection evaluates **definition effective union AND selected child requirement**, including palette, construct, picker, details, dependencies, assistant and publication/review. Load header/union coherently; a Public old version cannot expose a private newer head's mutable header ([current projection](../../../Essentials/src/Activities/Design/Api/Services/ActivityAuthoringCatalogReader.cs#L77)).

Add `ValueTask<CatalogDisclosureDecision> AuthorizeEntriesAsync(CatalogDisclosureSnapshot snapshot, CancellationToken cancellationToken = default)` to the **existing** `IActivityAuthoringCatalog`. After its broad gate it delegates to the canonical authorizer; no second catalog. Existing replacement implementations must adopt the new contract, not inherit an Allowed default ([current contract](../../../Essentials/src/Activities/Design/Core/Contracts/IActivityAuthoringCatalog.cs#L10)).

## Lossless historical contract

`workflow.catalog` machine-identity/provenance snapshot version **2** preserves complete opaque definition/version IDs and owner scope. Persisted IDs support 450 characters ([mapping](../../../Essentials/src/Activities/Design/Persistence/EntityFrameworkCore/ActivitiesDesignDbContext.cs#L338)); validate/refuse beyond bounds, never truncate/normalize/prefix-match. Other machine keys remain exact within their declared bounds; presentation labels may be bounded. Builtins require their registered exact owner/identity, not a prefix exemption.

The current [projection](../../../Extensions/Agent/src/Workflows/Elsa.Agent.Workflows/Services/WorkflowActivityCatalog.cs#L114) clips IDs to 256 characters. Version 1/uncertified snapshots must therefore **required-refuse/fresh-session**, even if the clipped prefix resolves to a Public ID. Version 2 is certified by owner construction, the engine-stamped contributor and verified recorded disclosure provenance, not a caller-written version flag or digest alone. Add a required owner-produced DisclosureSnapshot value to the existing Core catalog listing result; it carries a complete CatalogDisclosureSnapshot for every allowed catalog row contributing entries, total/truncation/omitted-entry-size metadata. The Workflow owner retains its canonical JSON in the existing References dictionary under the fixed owner key workflows.catalog.disclosure.v2; every projected entry must match that complete identity set. Recheck all contributors even when zero rows rendered; remove the old entries-only DisclosedEntries shortcut. The engine already freezes References, hashes complete canonical snapshots and verifies reload. Existing formatters omit References, but API attachment responses include it: this is retained owner provenance, not secret storage. Retained copies obey existing authenticated session ownership/access/retention; source revocation blocks fresh catalog reads and model/tool reuse, and does not erase retained copies or grant source invocation. Real source policies may require stricter retention before their production profile is enabled. Missing/unrecoverable IDs, incomplete count/header/derived-text provenance or ambiguous identity also required-refuse. Never fall back to broad `CanReadAsync()` or silently drop an attachment and continue replaying derived text. Preserve [required refusal](../../../Extensions/Agent/src/Workflows/Elsa.Agent.Workflows/Services/WorkflowAgentServices.cs#L313) and [pass lifetime](../../../Extensions/Agent/src/Elsa.Agent/Services/AgentDisclosureHistory.cs#L192). Keep the existing 28 KiB display-entry budget and 32 KiB Content candidate constraint, preserving full machine IDs. Independently cap the UTF-8 serialized enclosing attachment, including the escaped References value, at 1 MiB and complete identities at 10,000. If complete provenance cannot fit, refuse the whole catalog contribution before disclosure with no private body/count. Keep the existing 8 MiB/64-snapshot history bounds; nothing raises them. No identity clipping. The synthetic protected fixture explicitly allows authenticated session-retained schema copies; actual source production retention remains a separate qualification gate.

## Trusted write admission — Core

```csharp
public sealed record CatalogAdmissionOrigin(string RegistrationId, object ScopeBinding);
public sealed record CatalogAdmissionCandidate(
    IActivityDefinition Definition, IActivityDefinitionVersion Version,
    CatalogOwnerScope DefinitionOwner, CatalogOwnerScope VersionOwner,
    CatalogDisclosureRequirement Inherited);
public interface IActivityCatalogAdmission // replacement; provisioning, not caller authorization
{
    ValueTask<CatalogDisclosureRequirement> AdmitAsync(
        CatalogAdmissionOrigin origin, CatalogAdmissionCandidate candidate,
        CancellationToken cancellationToken = default);
    ValueTask ValidateAsync(
        CatalogAdmissionOrigin origin, CatalogAdmissionCandidate candidate,
        CatalogDisclosureRequirement expected, CancellationToken cancellationToken = default);
}
```

Canonical candidate scopes come from actual server clones/persisted definition/version ownership under the qualified store scope; [the version interface has no tenant](../../../Essentials/src/Activities/Design/Core/Contracts/IActivityDefinitionVersion.cs#L6). Registry producer profiles declare Global or CurrentTenant, and commit must verify agreement with both actual owners before writes. CLR uses explicit Global; private concrete publication consumes #3917's qualified scope. No general entity tenant model is introduced.

`ICatalogProducerScope.GetBinding(registrationId)` memoizes a producer binding in the current scope; `GetOrigin(registrationId)` returns that binding reference. Concrete/source/interface aliases resolve the same producer. Distinct registrations use distinct keyed scoped aliases. Validate `ReferenceEquals(origin.ScopeBinding, currentScope.GetBinding(origin.RegistrationId))` and the actual producer alias; IDs/types alone cannot establish origin. Reject unsupported transient composition and foreign-scope origins. Singleton registry holds factories only; binding identity memoization is not an authorization cache.

Trusted registration forces a minimum protected policy key/schema; its owner classifies canonical candidate material and the coordinator unions immutable inheritance plus every exact transitive catalog dependency whose structured material is copied/closed. A newly authored Public wrapper of a private child becomes protected even without a fork; removing the child later cannot silently declassify retained material. Public/missing output from a forced protected producer refuses. Origin is server-only, not persisted/wire data; explicitly exclude it from serialization and request/receipt fingerprints. Persist canonical requirement/binding facts, never scoped object identity. The Core-owned reconciliation event collection uses Core-owned admitted CanonicalCandidate+Origin pairs, preserving actual owner scope through factory/clone paths; carry mandatory origin through direct create/add-version and source/Design commit paths, including the no-publisher branch. Validate before duplicate shortcuts and all authoritative writes. Authoring/source/facet strings cannot select a Public owner.

## Persistence and query contracts

Retain requirement on versions/publications, immutable inheritance on authoring/drafts/forks, and effective definition union on canonical/header projections. Extend factory/clone/projection, atomic material/review/fork fingerprints and duplicate/recovery equality. Different requirement at one immutable identity conflicts independently of unchanged executable hashes. Publishing engine enforces this trusted material invariant without obtaining interactive authority.

Management/picker query selection must include complete allowed definition and child resource keys before counts/pages/cursors. Inventory every row not satisfying the canonical valid-Public predicate, including Unspecified, unknown and malformed states; these must not vanish from the candidate set as if denied. Missing selection/evaluator refuses. At most **256 non-public or undecidable resources per read** initially; excess is unavailable, not truncated totals. Bind continuations to trusted current authority, snapshot/filter and exact allowed-resource/owner-revision fingerprint; changes invalidate continuation. No second catalog or generic grant/capability platform.

**Receipt-family inventory:** format-2 atomic/fork material belongs to ActivitiesDesign only. ActivityPublicationReceipt keeps its existing members and PublishingLedger stamp "1"; PublishingSnapshotReview remains flat and unchanged. Their existing opaque request/candidate hash fields bind canonical security material through a new explicit domain/version tag in every fingerprint producer, replay and commit-side recomputation. No new foreign-family JSON member or physical migration is selected, and ActivitiesDesign's stamp does not protect a standalone old ledger reader.

Applied publication receipt projection/replay must load its exact Outcome definition/version, compare canonical publication requirements and authorize the current definition union before returning data. Non-applied diagnostics require recoverable exact DraftId/revision/head/review-bound material and its dependency closure; changed/deleted/unrecoverable material refuses safely. An old review token cannot bypass the new request-fingerprint domain; a mismatch conflicts without writes, with no old-hash fallback. Apply this to every early replay branch and direct GetReceipt, not only new publication.

ActivitiesDesign format **2.0.0**, fresh stores only: regenerate only four ActivitiesDesign Initials/snapshots through the normal module filter under [ADR 0078](../../../docs/adr/0078-workflow-executions-are-virtual-actors-and-cluster-membership-is-a-foundation-contract.md#L254). Preserve old stores; no ActivitiesDesign-v1 receipt/import/in-place/rolling upgrade, finalization bypass or CAS override. Supported-2.0 missing metadata stays Unspecified until exact trusted inventory classification. Track changed public interfaces/models in public API and computed preview versioning; do not hand-maintain literal package versions. Production private profiles remain off until all reader/Runtime-inspection coverage is qualified.

## Technical review

The names, registration descriptors, bounds and provenance profile above are concrete review targets. Validate them against source and cross-artifact behavior before task generation; a review finding is not implicit approval of a different authority model. Entity SDK, source-use/field rights, workload/retained grants and Runtime copied-schema inspection remain independent gates. Format/fresh-store policy is resolved; no legacy-upgrade compatibility is pending or implied by this unit.
