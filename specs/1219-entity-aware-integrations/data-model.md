# Catalog disclosure data model

**Status:** Candidate technical design for the approved catalog boundary; public ABI awaits review.
**Scope:** Catalog foundation only. Entity SDK, source-use policy, Runtime ingress/workload grants and Runtime-only copied-schema disclosure remain gated. Production source-private profiles stay off until every reader and inspection boundary is qualified.
**Basis:** [specification](spec.md), [approved boundary](design/catalog-disclosure.md), [contracts](contracts/catalog-disclosure.md). Source inspected at `01bfb391886aeb60080d9afe186bcd29a0851a09`.

## Formats and fail-closed defaults

| Format | Version | Meaning |
|---|---|---|
| ActivitiesDesign persisted family | `2.0.0` | Mandatory new catalog requirement/lineage format; fresh stores only |
| Disclosure requirement representation | `1` | Generic Public/Protected requirement and bounded policy conjunction |
| Machine catalog identity/provenance snapshot | `2` | Lossless exact identities; replaces uncertified potentially clipped snapshots |

`CatalogDisclosureKind` is `Unspecified=0`, `Public=1`, `Protected=2`.
`CatalogDisclosureStatus` is `Unavailable=0`, `Allowed=1`, `Denied=2`; default/unknown outcomes never allow.
Persist requirements, not permission grants. Current decisions and authorization revisions are transient.

## Core values

All values below, including admission origins/candidates and the reconciliation wrapper, belong to Activities.Design.Core. Generic admission/owner ports live there too. Persistence.Core consumes them for store-backed authorization/CQS; the Core event must never reference Persistence.Core.

| Value | Fields / invariants |
|---|---|
| `CatalogDisclosureRequirement` | Representation version, explicit kind, immutable policy list. Public has zero policies; Protected has a nonempty bounded conjunction. Missing/unknown/malformed values are unavailable. |
| `CatalogDisclosurePolicyRequirement` | Ordinal stable policy key, binding schema, canonical opaque resource JSON. No credentials, principal, saved allow or tenant-switch authority. Clone JSON/collections; canonicalize and deduplicate exact requirements. |
| `CatalogOwnerScope` | Explicit Unspecified/Global/Tenant discriminator. Global requires no tenant ID; Tenant requires its exact nonblank opaque tenant ID. These are canonical resource ownership facts, never execution-authority arguments. |
| `CatalogDisclosureResource` | Resource kind, exact ID, actual owner scope and retained requirement. The policy receives these facts alongside the opaque policy requirement so it can check binding/owner agreement. |
| `CatalogAdmissionCandidate` | Definition + version read models, actual definition/version owner scopes and inherited requirement. Construct from server clones/persisted material under the qualified scope; never from authored tenant text. |
| `CatalogAdmissionOrigin` | Registration ID + opaque current-scope producer binding. Ephemeral server-only context; never serialized in workflow, catalog or receipt content. |

The existing [version interface](../../Essentials/src/Activities/Design/Core/Contracts/IActivityDefinitionVersion.cs#L6) has no tenant property. Admission must carry canonical scope explicitly; a provider payload cannot fill that gap. The current [persistence access contract](../../Essentials/src/Workflows/Runtime/Core/Models/PersistenceAccessContext.cs#L53) selects trusted scope, while [scoped catalog reads](../../Essentials/src/Activities/Design/Persistence/EntityFrameworkCore/Stores/EfActivityDesignStores.cs#L1705) include own plus intentional global rows.

The canonical reference authorizer loads under that existing access context and ordinally validates loaded ownership as own/global before policy evaluation. It does not accept an authored tenant-to-run-as. Global CLR reconciliation uses an explicit registered Global profile. Private concrete source entities consume #3917's qualified tenant-preserving clone/publication scope; this unit adds no general entity SDK tenant model.

## Retained entities and relationships

| Owning entity / projection | New retained data and rule |
|---|---|
| `ActivityDefinitionVersion` / its Core model | Exact immutable disclosure requirement; copied through factory, clone and rebase paths. |
| `ActivityDefinitionVersionPublication` | Exact same requirement as the catalog version; equality checked before any publication write and on recovery/duplicate adoption. |
| Definition / authoring state | Immutable inherited requirement plus current effective union covering definition header and retained child versions/drafts. Effective union updates atomically with header/child mutations. |
| `ActivityDefinitionDraft` | Server-owned inherited requirement, preserved through fresh drafts, clone, provider migration, conflict copy and replacement. Not part of writable authored state. |
| Fork candidate / receipt | Reserved authoring/draft requirement and lineage included in material fingerprints; apply rechecks exact source and target material. |
| Management definition/draft/version revisions | Requirement metadata needed to filter before counts/pages; definition revision includes the conservative effective union. |
| Atomic operation / publication review material | Canonical requirement bytes or fingerprint participate in exact material comparison and review binding. Existing operation identities cannot be reused with different requirements. |

The initial canonicalization limits are 64 policies, policy key 128 characters, binding schema 64 characters, JSON depth 16, resource JSON 16 KiB UTF-8 and complete requirement 256 KiB. Refuse excess; never truncate security material.

Canonical storage uses `DisclosureSchemaVersion`, `DisclosureKind` and `DisclosurePoliciesSource` columns with a NotMapped requirement value. These are authoritative fields, not a derived public flag or arbitrary design facet. Validate shape/owner consistency on every supported write. Mark version/publication requirements and inherited lineage write-once; current definition union is recomputed transactionally, not freely authored.

Register policy JSON and changed nested documents as schema **content**, following the [owning inventory](../../Essentials/src/Activities/Design/Persistence/EntityFrameworkCore/AssemblyInfo.cs#L34). Preserve existing infrastructure-exception wrapping. Extend the [factory](../../Essentials/src/Activities/Design/Persistence/Core/Services/ActivityDefinitionVersionFactory.cs#L33), [clone](../../Essentials/src/Activities/Design/Persistence/Core/Entities/ActivityDefinitionVersion.cs#L115) and [immutability mapping](../../Essentials/src/Activities/Design/Persistence/EntityFrameworkCore/ActivitiesDesignDbContext.cs#L428) explicitly.

## Definition and version disclosure

Every projection combining a version/draft with definition header, label, category, description, head, recommendation or counts requires **both** the effective definition union and selected child requirement to allow. This includes palette, construct, picker, details, dependencies, diffs, assistant and publication/review views.

The current [palette projection](../../Essentials/src/Activities/Design/Api/Services/ActivityAuthoringCatalogReader.cs#L77) combines version data with [mutable definition fields](../../Essentials/src/Activities/Design/Persistence/Core/Entities/ActivityDefinition.cs#L26). A Public old version cannot disclose a private newer head's changed header. Read header/union coherently at one snapshot; no version-only cache may attach a newer unchecked header. Initially withhold the whole mixed-access definition rather than constructing partial headers or revealing denied child counts.

## State and equality rules

1. Trusted registered producer + canonical candidate → validated Public or forced Protected requirement, unioned with immutable lineage and exact transitive structured dependency requirements.
2. Unknown producer, missing origin, unsupported transient binding or scope mismatch → refusal before any Design/Runtime write.
3. Missing requirement in supported format 2.0 → Unspecified/unavailable until exact trusted inventory classification; no read-time inference of Public.
4. Same immutable version/operation with identical requirement → normal duplicate/recovery semantics; different requirement → conflict even when executable behavior is identical.
5. Fork/provider migration/clone/new wrapper → equal-or-stronger inherited/dependency conjunction; removing a private structured dependency cannot silently declassify already retained material; stripping lineage or declassification is unsupported.
6. Missing policy/module/schema or undecidable source authority → unavailable, never Public; definite denial wins over another unavailable requirement.

Requirement comparisons are separate from executable/template behavior hashes. Presentation refresh cannot change requirements. Publisher shortcuts, ordered commit/recovery and atomic material comparisons must check them before accepting a duplicate; [current ordered publication](../../Essentials/src/Workflows/Publishing/Persistence/EntityFrameworkCore/Services/EfActivityPublicationCommand.cs#L72) can otherwise adopt an existing Design commit.

## Scoped producer binding

The singleton startup registry stores registration metadata and factories only. `ICatalogProducerScope` resolves one producer binding per registration in the **current DI scope**, and all source/concrete/interface aliases resolve that same producer. Validation compares the origin binding by reference with the current binding, then verifies its actual producer alias. Matching only registration strings or types is insufficient.

Register executing producers scoped. Distinct registrations of one type use distinct keyed scoped aliases. Reject unsupported transient composition; do not retain a startup source instance. Origins cannot cross scopes even when a producer itself is shared. This binding memoizes object identity, not authorization results.

## Lossless historical identity and evidence

Snapshot 2 retains canonical resource kind, definition ID, version ID and actual owner scope for every reference needed to reauthorize its disclosed evidence. IDs are opaque and ordinal; preserve case and full text. Persisted IDs support up to **450 characters** ([mapping](../../Essentials/src/Activities/Design/Persistence/EntityFrameworkCore/ActivitiesDesignDbContext.cs#L338)); invalid/out-of-bound identities refuse. Never trim, normalize, prefix-match or UI-truncate machine identities. Other machine keys retain their declared bounds and exact values; only display text may be clipped.

The current [assistant projection](../../Extensions/Agent/src/Workflows/Elsa.Agent.Workflows/Services/WorkflowActivityCatalog.cs#L114) clips `activityVersionId` to 256 characters. Therefore a legacy snapshot is not certified lossless merely because its clipped prefix resolves to a Public entry. Version 1/uncertified snapshots, missing IDs, ambiguous ownership or incomplete provenance require **required** historical/session refusal and fresh-session recovery.

Snapshot/identity values belong in Activities.Design.Core because its existing catalog port and listing result expose them; Persistence.Core consumes them.

Snapshot 2 must be owner-produced and bound by the normal recorded disclosure provenance; a caller-written version flag is not certification. Preserve all exact references needed for headers, counts and derived text. The Core listing result supplies full identities for all count/truncation/omitted-size contributors, not merely rendered entries. Retain canonical snapshot JSON in existing attachment References under workflows.catalog.disclosure.v2; the engine freezes/digests/verifies it. Preserve the existing display budget, and enforce a 1 MiB UTF-8 enclosing-attachment limit including JSON-string escaping plus 10,000 complete identities. Refuse the whole contribution with no private body/count if provenance cannot fit; never shorten IDs. Recheck every contributor even when no rows rendered. Existing API projections return References, so it is retained data under authenticated session ownership/access, not confidential storage; real source retention is separately qualified. Re-admit exact versions **and current definition unions** before historical assistant/model/tool reuse. Erasure of already disclosed transcripts is not claimed.

## Bounded query selection

`CatalogDisclosureSelection` holds complete allowed protected definition/child resource keys and an opaque authority fingerprint for one read. Null/missing selection or evaluator is a composition/refusal error. Inventory all noncanonical-Public states, including missing/Unspecified/malformed requirements, so undecidable rows cannot silently disappear. Apply both definition and child selection before search, total, offset/limit, diagnostics and cursor construction.

The initial bound is **256 non-public or undecidable resources per read**; exceeding it returns unavailable, never truncated successful totals. Public resources do not consume the bound. Cursor binding includes trusted current authority, snapshot/filter and exact allowed-resource/owner-revision fingerprint; access changes invalidate the continuation. No positive authorization cache spans requests or assistant passes. A pass ends at the next write, model/provider call or tool execution, following [existing semantics](../../Extensions/Agent/src/Elsa.Agent/Services/AgentDisclosureHistory.cs#L192).

## Foreign receipt families

ActivitiesDesign format-2 atomic/fork material is distinct from publication receipts in PublishingLedger. ActivityPublicationReceipt retains its existing JSON members/stamp "1"; PublishingSnapshotReview stays flat/unchanged. Use existing opaque hash fields with an explicitly domain-separated security-bound preimage; update generation, all early replay paths, direct receipt reads and authoritative recomputation together. No foreign-family migration or old standalone ledger-refusal claim follows from the catalog stamp.

Applied receipts point to exact Outcome definition/version IDs for current canonical requirement/union authorization. Non-applied diagnostics require the original exact draft/revision/head/review-bound dependency material; missing/changed material is unavailable rather than authorized against a newer draft. Old-token/hash mismatches conflict without writes/fallback.

## Fresh-store boundary

ActivitiesDesign format **2.0.0 is mandatory**. Under accepted [ADR 0078](../../docs/adr/0078-workflow-executions-are-virtual-actors-and-cluster-membership-is-a-foundation-contract.md#L254), regenerate **only the four ActivitiesDesign Initial migrations/snapshots** using the normal module-context filter. Preserve old stores unchanged; use new owned stores for qualification.

No in-place or rolling preview-store upgrade, ActivitiesDesign-v1 receipt/import compatibility, finalization bypass or CAS override is included. Unsupported ActivitiesDesign schema/atomic-receipt formats fail closed; unchanged foreign receipt families retain their own current format and still require current projection authorization. Within a supported 2.0 store, known Public classification requires exact trusted producer/inventory evidence; unknown/custom material stays Unspecified. Existing global/code-owned compatibility is qualified by explicit inventory on fresh stores, not by trusting null tenant or source strings.

## Required model witnesses

- Default decision, missing/unknown requirement, absent owner and inconsistent scope cannot allow.
- Forced Protected admission cannot be weakened through either reconciliation route, source publication, fork or duplicate recovery; zero authoritative writes on refusal.
- Two scopes/two aliases of one producer type cannot exchange origins; unsupported transient composition refuses.
- Public version 1 plus private version 2/header remains private through all header-bearing routes; a new Public provider wrapper of a private child inherits protection without requiring a fork.
- Long protected ID sharing a 256-character prefix with a Public ID cannot alias; uncertified legacy history refuses even if its prefix resolves.
- Applied and non-applied publication receipts reauthorize exact original material; changed/deleted drafts and old review tokens cannot expose stale private diagnostics.
- Fresh SQLite reopen retains requirement/lineage; four providers use format 2.0; old stores/receipts refuse without mutation.
