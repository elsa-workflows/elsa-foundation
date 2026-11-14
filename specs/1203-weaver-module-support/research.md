# Research: generic effects and owner prerequisites

**Date**: 2026-11-04
**Source inspected**: `9daaa3206cdd98dfb5978468ea354d711415352d` (`origin/main`)
**Status**: Plan research; decisions below are proposed Preview contracts, not runtime evidence.

The approved [specification](spec.md) supplies scope and owner decisions. This research resolves PD1–PD3 for leaf 0 and the first three module leaves. Later leaves still require FR025's scoped specifications. Historical evidence in specs 1186/1189/1193 remains historical; it does not qualify these additions.

## R1 — Current principal without changing every tool signature

**Decision**: Add the scoped `IWeaverPrincipalSource` replacement in Weaver Core; Weaver API supplies the genuine current HTTP principal. Base Weaver registers none. API `TryAddScoped` preserves an explicit host integration; a later replacement uses `Replace`. Diagnose duplicates/wrong lifetime, and require exactly one at every new tool/context call boundary. Missing integration refuses owner-unavailable.

**Rationale**: [WeaverEndpointActor](../../Extensions/Weaver/src/Api/Endpoints/WeaverEndpointActor.cs) resolves actor and tenant but does not itself check authentication. [HttpWeaverToolAuthorizer](../../Extensions/Weaver/src/Api/Authorization/HttpWeaverToolAuthorizer.cs) sees the current request, while [IWeaverTool](../../Extensions/Weaver/src/Core/Contracts/WeaverToolContracts.cs) receives invocation data only. A source bridges that gap without persisting a principal or making approval call a module. Owners independently authorize the supplied principal.

**Alternatives**: Reject reconstructed claims, principal fields on frozen models, and a singleton ambient-principal capture. A nonfunctional default plus special replacement logic is unnecessary; absence is an explicitly unsupported optional host integration. The normal API feature still declares its actual prerequisites (framework §2.11).

## R2 — Feature inheritance and selected-store admission

**Decision**: Distinct `WeaverEffects` and `WeaverEfEffects` identities; the EF feature inherits common behavior and directly declares Weaver 1.1.0. Extend the existing store-selection metadata with its required effects feature Type. Check the completed collection and actual selected type. Explicit null/old factory selection means undeclared support and refuses composition with effects; only the known memory marker declares common support. Custom stores opt in through the new overload.

**Rationale**: CShells 0.0.31 feature discovery rejects different types with one name; inheritance does not substitute a dependency identity. [RequiresSchemaVersionAttribute](../../Essentials/src/Cluster/Core/Models/RequiresSchemaVersionAttribute.cs) is not inherited. [SchemaVersionRequirement](../../Essentials/src/Cluster/Core/Models/SchemaVersionRequirement.cs) extracts direct declarations. [WeaverPersistenceSelection](../../Extensions/Weaver/src/Elsa.Weaver/Extensions/WeaverPersistenceSelection.cs) already owns store selection. Common Weaver must not reference EF implementation or guess store type.

**Alternatives**: Reject treating an arbitrary legacy factory as known memory, an overlong new EF feature type name (Elsa §E6 R1), a common-to-EF dependency, enabling both siblings, descriptor schema requirements, a second registry or version engine, and treating missing family observation as available. Validate direct minimum declarations for custom specializations; use the shared dormancy checker.

## R3 — Live policy and legacy digest

**Decision**: Monitored host options, all three privilege classes false by default; Workbench base enables all three. Canonical nonempty class sets enter the prepared digest. Logical empty uses the exact old hash shape. A legacy Admin record gets a current administrative veto and display requirement without rewriting its frozen payload/digest.

**Rationale**: [WeaverInvocationIntegrity](../../Extensions/Weaver/src/Elsa.Weaver/Services/WeaverInvocationIntegrity.cs) hashes an explicit anonymous shape. [WeaverProposalServices](../../Extensions/Weaver/src/Elsa.Weaver/Services/WeaverProposalServices.cs) approves without module dispatch and independently retains executor outcomes. Current accepted terminal receipts must precede a new policy refusal; no replay or false zero-effect result after dispatch.

**Alternatives**: Reject startup/session policy as authority, adding an empty property to the legacy hash, and generic inference of irrecoverability. Module declarations own consequences; the explicit FR020 third-party follow-up remains required.

## R4 — Performed and closed codes

**Decision**: Append status 12 and scope 2, use existing `Executed=true` plus `Status=performed`, and require the actual descriptor's declaration and genuine unchanged-revision owner evidence at both integrity boundaries. Normalize declared module codes at their trusted installed source, preserving owner-unavailable provenance before replacement.

**Rationale**: [WeaverProposalExecutor](../../Extensions/Weaver/src/Elsa.Weaver/Services/WeaverProposalExecutor.cs) and `StoreOutcome` in proposal services are separate success boundaries. [WeaverToolAccess](../../Extensions/Weaver/src/Elsa.Weaver/Services/WeaverToolAccess.cs) and [WeaverContextOwnerMarkers](../../Extensions/Weaver/src/Elsa.Weaver/Services/WeaverContextOwnerMarkers.cs) already distinguish installed owner statements from forged host flags. A public/deserialized boolean alone is not provenance.

**Alternatives**: Reject accepting any receipt scope, a second outcome enum, converting all `Executed=true` to persisted, interpreting opaque module JSON, or changing a genuine receipt to uncertain merely because a typed code was unknown. Module-owned safe projections remain mandatory. The existing incorrect client numeric fallback is coordinated with #3761 and tested for every server ordinal.

## R5 — Protected Weaver 1.1 and all row kinds

**Decision**: Identity content upcast in the existing Weaver chain; deserialize optional defaults only after authenticated unprotect. Read every stamped row kind and owned summary/count projection through shared readability. Preserve old canonical bytes, purposes, enum ordinals, digests and permanent 1.0 counter readability.

**Rationale**: [WeaverEfModule](../../Extensions/Weaver/src/Persistence/EntityFrameworkCore/WeaverEfModule.cs), [content declarations](../../Extensions/Weaver/src/Persistence/EntityFrameworkCore/AssemblyInfo.cs), [codec](../../Extensions/Weaver/src/Persistence/EntityFrameworkCore/Services/WeaverRecordCodec.cs), [session decoder](../../Extensions/Weaver/src/Persistence/EntityFrameworkCore/Stores/EfWeaverSessionRecordStore.Codec.cs), and [audit store](../../Extensions/Weaver/src/Persistence/EntityFrameworkCore/Stores/EfWeaverAuditStore.cs) show encrypted payloads, integrity-only snapshot Canonical, hard-equality 1.0 checks, and counter updates that never restamp. Audit-owner and summary projections need explicit coverage too.

**Alternatives**: Reject decrypt/re-encrypt in an upcaster, latest-version equality, counter restamping, and a relational migration for JSON/enum additions. Keep minimal plain owner lookup before authorization, then validate only exactly owned data; foreign unreadable rows must not disclose schema state or trigger settlement.

## R6 — Per-attempt admitted writes

**Decision**: Select WriteVersion from the existing context's module gate inside each metadata attempt. Preserve the initial raw owner/fence lock/version increment; load the tracked session after it and save content, selected stamp and children in the same transaction through the shared interceptor. Rebuild encoding/entities on a narrowly verified forward-adoption retry, never rerun an effect.

**Rationale**: [EfWeaverSessionRecordStore](../../Extensions/Weaver/src/Persistence/EntityFrameworkCore/Stores/EfWeaverSessionRecordStore.cs) currently encodes before retries, stamps the latest constant and uses raw `ExecuteUpdate` for session content. [EfSchemaWriteGateInterceptor](../../Essentials/src/Persistence/EntityFramework/SchemaFinalization/EfSchemaWriteGateInterceptor.cs) validates rather than restamps content. [EfWriteRetry](../../Essentials/src/Persistence/EntityFramework/EfWriteRetry.cs) supplies the bounded metadata retry seam. A held 1.0 fleet must keep representable old writes working while refusing new behavior before effects.

**Alternatives**: Reject latest-assembly stamping, a new observer/fleet, retrying arbitrary schema/corruption failures, and a new raw-write API when tracked SaveChanges preserves the existing fence transaction. Unsupported/unobserved EF cannot authorize 1.1; an unadmitted custom context supports representable legacy content only.

## R7 — Truthful support discovery

**Decision**: Join a bounded safe Studio registry projection with current loader state and actual contribution origins; project backend support from existing registered tools/context sources. Keep host, side, generation, observation and authority separate. Collect fresh per prompt; client facts are untrusted information, never execution authority.

**Rationale**: [ActiveShellStudioApiEndpoint](../../Studio/src/essentials/Elsa.Studio.Hosting/ActiveShellStudioApiEndpoint.cs) already exposes `/_elsa/studio/module-registry`. [StudioModuleManifestProvider](../../Studio/src/essentials/Elsa.Studio.Api/Services/StudioModuleManifestProvider.cs) currently loses some failed discoveries before manifest collection. [loader.ts](../../Studio/src/essentials/Elsa.Studio.Hosting/Client/src/app/loader.ts) can leave partial registrations after a module throws. Manifest capability hints are not actual Weaver registration; satellites may own registration for another resource module. The context collector also needs the disabled/failed-origin filtering already used by other contribution collectors.

**Alternatives**: Reject the credential-bearing Nuplane management registry, raw loader exception text/URLs, route-to-module maps, treating unknown as not installed, and a second domain registry. Use the smallest generation/origin mechanism that makes removal, partial failure and nested contributions observable.

## R8 — Owner revisions and residual intervals

**Decision**: [Owner operations](contracts/owner-operations.md) carry genuine principal and exact expected identity/revision into the same owner load and conditional command. Choose the specification's explicit residual-window option for consumer observation versus Secret deletion; warn in review and attribute receipts to observations. No cross-owner transaction coordinator is added.

**Rationale**: [Secrets revision repository](../../Essentials/src/Secrets/Core/Contracts/ISecretRepository.cs) supplies metadata CAS in EF; in-memory does not. [ConnectionDefinition](../../Essentials/src/Connections/Core/Models/ConnectionModels.cs) has IncarnationId but existing repository CAS uses integer Version. Name-only managers reload targets; comparing first then calling them is unsafe. Secret value deletion precedes metadata CAS and connection cleanup/external tests span independent effects. The PD3 table in the plan assigns barriers and truthful partial/uncertain outcomes to each interval.

**Alternatives**: Reject unconditional fallback, integer version as creation identity, a local semaphore presented as a cluster fence, global consumer hash/refusal of all truncated advisory observations, or a final CAS described as undoing an earlier external effect. Native #3779 remains exactly scoped best-effort cleanup. Freeze a Connection delete cleanup observation and compare it before execution: OAuth can create PKCE/grant secrets without changing Connection.Version ([OAuth2TokenService](../../Extensions/Integrations/src/Connectors/OAuth2/OAuth2TokenService.cs), BeginAuthorizationCodeAsync and WriteGrantAsync). A fresh complete scan cannot silently broaden the reviewed operation.

## R9 — Major semantics within the existing preview line

**Decision**: Retain approved Major semantic classification for performed and default-off Admin veto, Minor for the listed additive surfaces/Weaver 1.1, and Patch for exact native #3779 cleanup. Keep computed 4.0 preview versions; no version-line/publisher/release change or general magnitude engine.

**Rationale**: Framework §4.2, Elsa §E5 and accepted [ADR0080 D4](../../docs/adr/0080-elsa-4-simplification-decisions.md#d4--pre-ga-cleanup) apply the pre-GA policy. `VersionLines.props`, `VersionComputation` and `PackageVersioning.props` supply computed package versions; the constitution explicitly says magnitude mechanical enforcement is not built. Contributor checkpoints reference immutable image pairs and publish metadata only; they are not GA baselines.

**Alternatives**: Reject downgrading semantic breaks to Minor because CLR additions are optional, inventing a severity-marker API, hand-written project versions, or expanding this leaf into a release-cut decision. Record manual API/binary/behavior assessment and exact package/native proof separately; an absent mechanical gate is not a pass.

## Post-design gate result

No unresolved owner decision or constitutional exception is identified. The plan keeps Core/implementation boundaries, scoped authority, replacement diagnostics, shared discovery/persistence mechanisms and risk-based proof. Implementation and contract freeze are still gated by independent review and demonstrated journeys, not this research record.


## 12 November source inventory reconciliation

At current main `dff7c490d0e5501b22d4ba89beb389c956d4f423`, the normal Studio shell selects
`DecisionsStudio` (`Studio/src/apps/Elsa.Studio.Web/shells.json`), and Workbench selects the
Decisions design/runtime/API/EF features (`Essentials/apps/Elsa.Workbench/shells.json`).
`Extensions/Decisions/studio/Elsa.Studio.Decisions/Client/src/module.tsx` registers `/decisions`
with exact decision/version workspace identity, a decision-version property editor and a decision-input
expression editor; its source has no Weaver contribution registration. The owning page also composes
table/model editing, cases, evaluation and rule explanation. FR-001 assigns this new module to the
mandatory later leaf22 under FR-025. That leaf gets its own spec/issue when it starts; this source
inventory does not start another implementation lane or imply support. The real normal-host inventory
and feature/removal observations remain T045 gates, and dated M0 evidence remains unchanged.
