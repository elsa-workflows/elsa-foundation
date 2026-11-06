# Implementation Plan: Durable Runtime Preparation Retention

**Branch**: agent/codex/2892-preparation-retention | **Date**: 2026-11-06 | **Spec**: [spec.md](spec.md)

**Status**: Plan R3 focused correction, awaiting review. R2 acceptance/standards and the held API shapes were reported clean; this packet corrects the remaining mutable-template-revision finding without API changes. No approved ABI, implemented provider capability or T066 acceptance is claimed. Tasks remain deferred.

**Frozen requirements**: spec SHA256 f04e6f8a8fd9b38c2ff8a035d7c21b0793a1e9083a31b114797aa822150a24ae; checklist SHA256 31c36886c1ffe629a2dbf6bcdbb909a5b1fb5efc42efb823b09168594fccc4dd. Requirements reviews were reported clean on these bytes. Held implementation HEAD remains fd0fdb21eacabe690183ff761432746e9c7e680c.

## Summary

Add a typed preparation-root document to the existing canonical source-reference backend, using one new metadata table in RuntimeArtifact. The document contains exact typed roots/closure, physical incarnations, canonical operation binding and permanent terminal replay facts. It has NO executable/definition anchor. Existing native/model/template stores and ordinary source-reference models remain unchanged.

The actual consuming owner is T066's shared SourcePublicationCoordinator/ISourcePublicationStore with WorkflowSourcePublicationAdapter and AgentSourcePublicationAdapter. Its current EF provider has revision/state CAS but lacks true operation insertion incarnation and through-commit fencing. Implement those narrow provider/issuing changes there, using canonical operation revision/state, not a new worker claim or takeover engine. Keep winning prepared-envelope fingerprint distinct from Runtime's physical selection fingerprint; accepted/terminal verification stamps are historical across legal revision advancement.

Stable selection/decision identity excludes mutable template CAS revision observations. TemplatePin.Revision is a historical observation, not immutable material identity: each attempt reads/checks fresh native revisions under the fence without replacing accepted pins, fingerprint or token. Real Agent publication legitimately advances template revision while preserving content/incarnations; retention replay and transfer must still succeed.

Same physical owner/Runtime stores use the existing EfSharedTransaction with fresh enlisted contexts and one Runtime-participant-owned commit. Independent proven stores hold the real owner guard through Runtime commit without claiming distributed atomicity. Both modes have positive production-class SQLite proof gates; neither a universal Unsupported implementation nor fake owner fixtures completes this leaf.

See [research](research.md), [data model](data-model.md), [contract semantics](contracts/preparation-retention.md), [C# contract proposal](contracts/runtime-preparation-api.cs), [EF participation proposal](contracts/ef-participation-api.cs), [issuer integration](contracts/issuer-integration.md), [validation guide](quickstart.md) and [review record](plan-review.md).

## Technical Context

**Language/Version**: C# / .NET 10; repository SDK baseline 10.0.300 with existing roll-forward policy.

**Primary Dependencies**: Existing Runtime.Core, Workflows.Runtime, Runtime EF and Elsa.Persistence.EntityFramework. Publishing EF already bridges Runtime EF; its owner adapter stays there. Runtime gains no Publishing/Design dependency.

**Storage**: One preparation metadata table in the existing canonical Runtime source-reference provider/RuntimeArtifact family. Canonical source-publication ledger gains owner-side incarnation/coordination fields under its own family. No material mirror, new execution store, generic delivery-source SPI or owner-policy registry.

**Testing**: Existing Runtime, Runtime EF/ProviderTests, Publishing EF, actual Workflow/Agent publication and Architecture projects. No tests/builds/hosts have run for this plan.

**Target Platform**: Existing server/library deployment shapes. Production issuer is EF; memory Runtime tests cannot claim durable issuing authority.

**Project Type**: Additive library/provider behavior; no UI or HTTP endpoint.

**Performance Goals**: Finite selected-closure operations and bounded keyset recovery/GC pages. Permanent terminal metadata is retained deliberately; no throughput promise or silent expiry.

**Constraints**: Exact closure/incarnation and actual canonical revision/state fencing; zero preparation-derived start/export/interest effects; actual Agent Design publication evidence; unknowns retain; no implicit ambient/caller transaction adoption.

**Scale/Scope**: One preparation document per canonical publication operation. This supplies the Runtime prerequisite plus the real consuming owner adapter, not all T066/publication/Studio acceptance.

## Constitution Check

Pre-research and post-design review against both constitutions and [review checklist](../../docs/contributing/review-checklist.md):

| Gate | Design assessment | Implementation evidence |
| --- | --- | --- |
| Framework §2.1/§2.6/§2.20 | Core contracts are EF-free; provider hook stays EF-specific. Existing canonical owner retains policy. | Dependency/composition and real issuer binding tests. |
| §2.9/§2.10 | Existing canonical providers; typed metadata contains no copied material. Queries and confirmation-returning commands are separate. Consumer-owned contexts supported. | Scope/CAS/projection/provider tests. |
| §2.22–§2.23 | Update existing catalog/PublicAPI; scoped coordinator, internal implementations. Same-domain Core friend only for opaque session construction; third-party providers receive sessions. | API compatibility, registration, architecture and whole affected projects. |
| Elsa §E2.2.3/§E2.6 | No Runtime→Design dependency; ordinary published material still runs independently. | Runtime-only/shared-material controls. |
| Schema/versioning | Revised R1 no-DDL decision BEFORE implementation: one Runtime metadata table; separate owner-field migration. Allocate actual chain steps with each owner; no historical rewrite. | All four provider migration/designer/snapshot/readiness/upcaster paths and retained-row preservation. |

No exception is requested. Framework §2.24, Elsa §E2.8 activity versioning/Model X and §E2.9 remain draft/provisional. These are design assessments, not executed gate passes.

## Project Structure

### Documentation (this feature)

Reviewed spec/checklist remain unchanged. Planning files are plan.md, research.md, data-model.md, quickstart.md, contracts/*, planning-record.md and plan-review.md. No tasks.md is generated until clean plan review.

### Source Code (repository root)

| Owner / existing home | Concrete planned change |
| --- | --- |
| Runtime/Core Models and Contracts | Typed unanchored preparation selection/snapshot/token, new reader/staged writer members on the existing source-reference store, scoped retention facade/factory and operation-scoped issuer/session contracts. C# signatures are linked above. No new ordinary source-reference scope/constructor requirement. |
| Runtime implementation, source-reference provider | Retention facade orchestrates live root-lease callback and configured issuer callback. Provider implements dedicated metadata stage/commit participant; existing public SaveWithRootWriteLeasesAsync transaction guard stays unchanged. |
| Runtime EF | New preparation metadata entity/codec/mapping in RuntimeArtifact; exact actual issuer/context/session/frame checks; native material/template/metadata fences and one owned commit. |
| Existing material graph/root-write manager | Full native/model closure and physical-incarnation checks. Never retain a grant vector beyond ExecuteFencedAsync callback lifetime. Coordinate newer MessageInterest accessor normally. |
| Existing template providers/maintenance | Exact ID/hash/row/hash-claim observation; template-only native fence; complete nested closure in final deletion checks. No fake workflow lease. |
| Existing source-reference queries and GC | Explicit typed preparation reads; initial/final roots union; terminal metadata never pruned. Conservative failure; bounded pages including terminal rows. |
| Dispatcher/resolver and indirect/interest readers | Published/TestRun requiredScope allowlist BEFORE replay/material/effects. Known preparation history blocks reference-less legacy admission. Published/TestRun/retained execution authority remains valid. |
| Export factory/importer, inspection/options/catalog | Ordinary Published-only authority remains; preparation IDs/history never create runnable export, activation or Message interest. |
| T066 owner branch, Publishing Core/EF | Existing ISourcePublicationStore exposes configured issuer binding; EfSourcePublicationPreparationIssuer stages the real operation guard. Add physical IncarnationId/FenceRevision; all competing transitions honor them. No logical Revision-only increment or new claim policy. |
| Actual T066 coordinator/adapters | Prepare retains Runtime closure before returning publishable review; Finish/Reconcile/Reject use actual canonical terminal evidence before completion. Agent requires Design publication plus exact Runtime reference/template. |
| Existing tests | All three journeys on real same-file AND independent-file SQLite owner/Runtime compositions; actual Workflow and Agent consumers; server provider/schema coverage. |

Runtime paths are under Essentials/src/Workflows/Runtime/. Actual T066 paths/head and provider responsibilities are in issuer-integration.md. Root delegates Publishing edits to its owner; do not edit that branch here.

Concrete existing Runtime seams to change/verify are Core/Contracts/IWorkflowExecutableSourceReferenceStore.cs and ReusableActivityRuntimeStores.cs; Services/Executables/WorkflowExecutableDependencyGraph.cs, WorkflowExecutableRootWriteLeaseManager.cs, WorkflowExecutableReferenceGarbageCollector.cs and InMemoryWorkflowExecutableSourceReferenceStore*.cs; Services/Executions/WorkflowExecutableStartSourceResolver.cs and WorkflowStartDispatcher.cs; and EF Stores/EfWorkflowExecutableSourceReferenceStore*.cs plus EfExecutableActivityTemplateStore.ReferenceFences.cs/.Maintenance.cs. Service paths here are relative to Elsa.Workflows.Runtime/. Export authority stays in Publishing's Services/WorkflowArtifactClosureFactory.cs; runnable import validation stays in Runtime Reconciliation's WorkflowArtifactClosurePlanner/WorkflowArtifactReconciler/JsonWorkflowArtifactClosureReader. Add typed preparation rejection where needed; never serialize it as Published material.

## Mandatory transaction nesting

For any native/model roots, keep the whole operation inside ExecuteFencedAsync(rootSet, unique invocation id, callback). Inside that callback: configured owner issuer callback → participant opens Runtime-owned/shared transaction → issuer Join stages/verifies canonical guard → ordered material/template/source/metadata fences → metadata save/commit → dispose all Runtime/shared contexts/transactions → issuer releases its guard → callback returns → root manager renew/release cleanup. No grant can be saved for use after the manager returns.

For template-only selection skip the empty executable-root overload (it rejects empty sets); the same issuer/participant lifecycle uses genuine template/hash-claim fences. Confirmed terminal replay may read its immutable metadata without material grants or material existence.

SAME physical: select mode BEFORE opening an owner write transaction. Participant uses EfSharedTransaction.BeginAsync with configured contexts, including owner mapping, and fresh owned handles. Join stages owner CAS in that shared transaction; it does not call the owner's existing Write or the old public Runtime root writer. INDEPENDENT: only after genuine physical independence is proved, the issuer holds its own ledger guard while the participant commits/disposes a separate Runtime transaction. No two-file atomic commit is asserted.

## Admission, schema and overlap

Actual configured issuer, selected material/source/template tuple, context origins, physical scopes, schema readiness, callback lifetime and private native frame must be checked; capability booleans/interface properties/identity strings are not authority. Same SQLite strings alone are not enough for arbitrary handles; different strings are not proof of independence. Alias or unknown topology refuses.

The real minimum target is the actual T066 EF ledger plus actual Runtime EF stores on durable SQLite, in both topologies, with all three journeys. Four-provider metadata/owner migrations, codecs/readiness and native conformance are required before advertising those compositions; skipped/not-run results are not support evidence.

Allocate new RuntimeArtifact metadata and owner PublishingLedger migration steps from their actual chains, with all four provider migration/designer/snapshot/upcaster/readiness updates. Keep logical publication Revision/state separate from physical FenceRevision/incarnation. No fixed schema number is reserved in this plan.

Held HEAD remains fd0. Reconcile verified newer #3912 PublicationScope/model/comparer/protected writes before implementation, never blind-replace them. #3904's source-reference interest fence uses a dedicated partial; its root-write manager accessor also overlaps. Preserve native scope/transaction/schema/incarnation/revision and Published-only interest semantics. Local proposals are not published-head proof; see planning-record.md.

## Complexity Tracking

R2 revises the R1 header/member-in-ordinary-reference design because template-only retention cannot honestly supply required native definition/provenance fields. One typed metadata table avoids fake fields, member-source aliases and terminal anchor dependencies. Closure paging is intentionally simple; an indexed membership optimization requires separate evidence/review. No owner policy or new material store is introduced.


## Owner-approved S1 source increment (6 November)

The [enrollment settlement contract](contracts/enrollment-settlement-s1.md) amends T005/T015/T016 with the approved permanent acceptance/abort protocol. Implement the EF-specific native seam, internal Runtime owner-session invocation and checked barrier mechanics first, then integrate the actual supported producer/writers/schema and Publishing cooperation. Keep Core Complete/token/session unchanged. This source increment leaves all five native/integration gates explicit and does not complete those tasks or add a dependency to #3804. No adapter-registration or SQLite lifecycle choice is implied.
