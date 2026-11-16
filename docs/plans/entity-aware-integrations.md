# Entity-aware integrations and optional process records

**State:** proposed delivery plan; external integrations are the production scope. Service connections
come first; the optional record backend is an explicit follow-on. Architecture decisions remain open as marked below.
**Owner authorization:** [6 November 2026](https://github.com/elsa-workflows/elsa-foundation/issues/3608#issuecomment-6671902429).
**Canonical request:** [#3608](https://github.com/elsa-workflows/elsa-foundation/issues/3608).
**Assessment baseline:** main `e0ba87ecfc3b1e774c509ed274b19cd3f34a1124`.

## Product outcome

An author connects an existing business application, discovers its supported record types and values,
selects a stable record reference, and automates source-owned business operations and events. The source
keeps its records, permissions and domain rules. Elsa does not need a mirror store.

The first journey is **Expense Claim approval**. A separate source owns employee, amount, currency,
status and attachment references. `ClaimSubmitted` starts an Elsa workflow with the claim reference and
an explicitly selected event-time projection. A normal User Task collects approval. A source-owned
`ApproveClaim` or `RejectClaim` operation validates the decision and expected record version. Another
workflow can process the same claim; completing or cancelling a workflow does not delete it.

A decision can initially be an ordinary workflow branch. The optional Decisions module can replace that
step through its own agreed contract; its delivery is not a prerequisite for proving entity integration.
A CRM Customer/Opportunity follows the same pattern with the existing CRM retaining authority.

## Refreshed gap assessment

The paths below describe actual inspected source, rather than acceptance evidence.

| Existing seam | What can be reused | Remaining gap |
|---|---|---|
| [Integrations SDK](../../Extensions/Integrations/README.md), [connector base](../../Extensions/Integrations/src/Sdk/Elsa.Integrations.Sdk/ConnectorActivity.cs) | Ordinary typed activities, HTTP transport, Connections/Secrets and faults | The SDK explicitly remains activities-only; no entity discovery, reference or external-event host |
| [Activity reconciliation](../../Essentials/src/Activities/Design/Reconciliation/Clr/ClrActivityReconciliationFeature.cs) | Existing persisted activity catalog | Entity descriptors must not become a second activity catalog |
| [Input options](../../Essentials/src/Workflows/Design/Core/Contracts/IActivityInputOptionsProvider.cs) | Dependent source/type/record/operation selection | Source authorization and schema-shaped arguments need entity-owned contributions |
| [Connection resolution](../../Essentials/src/Connections/Elsa.Connections/Services/DefaultConnectionResolver.cs), [connection identity](../../Essentials/src/Connections/Core/Models/ConnectionModels.cs) | Tenant lookup, credentials, source-host rules and incarnation identity | Discovery cannot fabricate an activity context; source record/field authorization and source retargeting need explicit handling |
| [Trigger provider](../../Essentials/src/Workflows/Runtime/Core/Contracts/IActivityTriggerStimulusProvider.cs) | Normal publication and start/wait routing | Authenticated, durable external acceptance is not supplied by the existing direct dispatch API |
| [Internal delivery](../../Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Triggers/StimulusDeliveryServices.cs) | Runtime-owned recipient retention and exact resume semantics | The current publisher/checkpoint boundary is workflow-owned; an external event cannot invent a publisher workflow |
| [User Task forms](../../Essentials/src/Workflows/Runtime/Core/UserTasks/UserTaskContracts.cs) | Pinned form versions, validation and explicit prefill | Reference/attachment editors and authorized live dimensions are not built-in field types |

The [owner selected](https://github.com/elsa-workflows/elsa-foundation/issues/3608#issuecomment-6672133904)
**tenant/service connections first**, with current Elsa permissions checked as well. Current Connections do not establish delegated human
source identity. Discovering a type never grants access to all its records or fields. Authorization is
rechecked at discovery, selection, read, invocation, event admission and projection boundaries.

### Primary-source comparison

[Dataverse metadata](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/query-schema-definitions)
exposes types, attributes and relationships; its [operations](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/webapi/perform-operations-web-api)
distinguish reads/functions from actions. This supports a small provider contract around source-owned
operations, rather than a universal writable-record API. Its opaque [ETags](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/webapi/perform-conditional-operations-using-web-api)
also show why identity, schema revision and write concurrency must remain separate.

[Power Automate change triggers](https://learn.microsoft.com/en-us/power-automate/dataverse/create-update-delete-trigger)
may run for updates containing unchanged values; selected columns mean request membership, not a proven
value transition. Relationship/virtual-table support is limited. [Dataverse webhooks](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/use-webhooks)
have bounded retries and optional images; a synchronous notification can escape a transaction that later
rolls back. Elsa must describe each provider's actual event semantics.

Orchard Core separates [framework and CMS](https://docs.orchardcore.net/en/main/), exposes
[content types](https://docs.orchardcore.net/en/latest/reference/modules/ContentTypes/) and distinguishes
[item and item-version identity](https://docs.orchardcore.net/en/latest/reference/modules/Contents/).
Its [workflow integration](https://docs.orchardcore.net/en/latest/reference/modules/Workflows/) correlates
content events by item identity and can reload an item on resume. Its
[content event handler](https://github.com/OrchardCMS/OrchardCore/blob/main/src/OrchardCore.Modules/OrchardCore.Contents/Workflows/Handlers/ContentsHandler.cs)
does not establish a durable external outbox or a completed database commit.

**Recommended real-source witness:** a separate owned Orchard Core host and database, using its real
types, records, identities and permissions, plus a clearly identified custom integration module for
selected metadata/business operations and event handoff. This is an external-provider integration,
not a claim of a built-in Orchard connector or Dataverse support. Verify compatible package/host versions
and the actual commit/event boundary before selecting the production adapter. A Dataverse witness is
additional scope only if isolated development-tenant access is available; no account is presumed.

## Proposed contract boundaries

These are design proposals for specification, not approved public API names or implemented guarantees.

| Surface | Minimum useful meaning |
|---|---|
| Source and record reference | Provider/source identity, trusted tenant/partition, connection incarnation, type identity and opaque record identity. Credential rotation preserves identity; recreation/retargeting must not silently redirect a retained reference. Schema revision and concurrency token are separate facts. |
| Type and fields | Stable identifiers, labels, portable value types, nullability, bounded validation and limited single-record relationships. Readable/queryable/writable, sensitive, unavailable and unsupported are explicit. No generalized object graph or ORM contract. |
| Selection/read | Bounded authorized selection with declared operators/limits; explicit current or supported historical read. Absent, null, denied, deleted, incompatible schema and source unavailable remain distinguishable to authorized callers without leaking foreign records. |
| Business operation | Source-owned named operation, typed parameters/result, source validation/access, expected version where supported, idempotency/retry declaration and an ambiguity result for an uncertain write. No blind retry of a potentially committed operation. |
| Event | Source/event identity, record reference, lifecycle kind or business name, occurrence/receipt time, schema revision, optional before/after, optional source sequence/version, correlation, causation and origin where supplied. Missing images differ from explicit null values. |
| Consumer projection | Explicit authorized fields/dimensions for existing form prefill and decision inputs, and a bounded dashboard contribution through the dashboard owner's agreed seam. No raw SQL, arbitrary database access or implicit retention of sensitive values. |

Metadata caching must include tenant, source incarnation, schema revision and access context, with bounded
staleness and fail-closed access handling. A saved workflow keeps the reference/field contract it was
authored against. Removing/changing a field must produce a useful incompatibility result or an explicit
provider migration, never silently bind a different field. A live read deliberately obtains current
source values; event-time snapshots remain labeled and governed by their own retention/access policy.

Use ordinary catalog activities and module-contributed editors for read/operation/event authoring.
Reuse existing portable value types. Keep provider metadata, workflow adapters, transport and Studio
composition separate. The SDK's activities-only package does not acquire hosted endpoints or persistence.
Evaluate whether these contracts fit an Integrations subdomain or need a new domain; a new root domain
requires the applicable ADR and narrow §E2.1 admission before production, not an incidental addition to
Primitives. No constitution amendment or unratified execution proposal is assumed by this plan.

**Private source catalog admission remains unresolved.** A disposable public-extension experiment preserved
separately typed operation inputs, exact decimals and immutable schemas through normal catalog/publication
and runtime paths. That does not establish safe source-specific metadata exposure. The private-tenant
case failed: [source publication](../../Essentials/src/Workflows/Publishing/Api/Services/SourceOwnedActivityVersionPublisher.cs)
copied explicit tenant-owned entities through factories that dropped tenant ownership, producing a global
definition visible to another tenant. Narrow prerequisite [#3917](https://github.com/elsa-workflows/elsa-foundation/issues/3917)
owns that existing ownership defect. Availability filtering is also addability, not a source-disclosure
policy; construct/detail/assistant paths must be covered as well as the picker. Before private operation
versions are adopted, M2 must agree tenant-preserving provisioning and a common source-aware disclosure
decision through the existing catalog. No second catalog, nested binding language or entity-specific core
switch is justified. Experimental evidence stays on #3907; source privacy remains failed/incomplete.

### Event and recovery obligations

The provider declares event coverage, filters, commit boundary, retry window, ordering and optional
images. Source acceptance, Elsa durable acceptance and workflow effect are separate states. Record ID
is not an event ID, and correlation does not reserve one workflow per record. Define duplicate identity
and retention per source/subscription; independent workflows may legitimately consume the same event.

Runtime owns retained recipients and workflow start/resume. Coordinate the shared external-ingress
contract with #3582/#3583 before production event implementation. No second inbox, fake workflow
publisher or distributed transaction across the source and Elsa stores is introduced. If that shared
contract is unavailable, metadata/read/operation work continues while reliable-event acceptance remains
explicitly incomplete; direct dispatch is not restamped as a durable receipt.

Propagate operation causation/origin when supported and filter the resulting event deliberately.
Suppressing all events for a record would hide legitimate edits. Where the source cannot echo causation,
document the supported alternative and remaining limitation. Exercise duplicates, partial fan-out,
source/receiver restart, denied/revoked access, outages, stale concurrency and deliberate feedback.
Diagnostics expose safe source/event/reference identities, acceptance/delivery state and recovery steps,
without secrets or sensitive record snapshots.

## Optional record backend: assessed follow-on

**[Owner decision, 6 November 2026](https://github.com/elsa-workflows/elsa-foundation/issues/3608#issuecomment-6672584508):**
after the bounded assessment, the **record backend is an explicit follow-on**, tracked in
[#3916](https://github.com/elsa-workflows/elsa-foundation/issues/3916).
This delivery completes the external entity-integration production scope and records the optional-backend
assessment/outcome. It does not implement that backend. External-only deployment remains independent of
any business-record store. The follow-on creates no active implementation lane or mandatory CRM.

An Elsa-managed business record would be maintained by an optional record module, with its own identity,
validation, permissions, history and persistence independent of workflow lifetime. This describes the
system responsible for maintaining the record; the existing external application retains that role in
the initial integration delivery.

The proposed audience is a small process application without an existing business backend. The initial
profile is Expense Claim: text, decimal amount plus currency, boolean, date, bounded choice, one-record
employee reference and attachment references to a separately authorized owner. No arbitrary nested
objects, many-to-many modeling, calculated scripting, generic application builder or binary store is
assumed. A source type/action designer is separate from a workflow designer.

Before activating the follow-on, settle type/field validation, create/read/bounded query/update APIs and
Studio forms/list/detail states; current tenant/record/field permissions; conditional writes; history;
deletion/retention and dangling references; schema revisioning and existing-record migration; active
workflow compatibility; and committed-change events backed by the actual persistence boundary. Named
Submit/Approve/Reject operations preserve record rules. Record lifetime is independent of workflow runs.

The assessment found reusable field rendering, identity, composition and persistence primitives, while
independent record storage/UI, history, migrations and record-commit event responsibility remain new work.
User Task storage is execution-owned and cannot provide independent business-record lifetime. The real
source experiment demonstrated source validation/reopen and a callback-before-rollback case; its custom
post-commit handoff still has a crash-loss window. Those observations support completing integrations
before the new data domain. [The decision brief](https://github.com/elsa-workflows/elsa-foundation/issues/3608#issuecomment-6672266759)
and #3916 preserve the bounded obligations. No tables/documents/persistence provider is selected, and no
production record store is claimed.

## Staged critical path

| Stage | Reviewable result and exit condition |
|---|---|
| M0 — establish and assess ([#3906](https://github.com/elsa-workflows/elsa-foundation/issues/3906)) | Owner receipt, scoped D0/registry/goal, this refreshed assessment, concrete journey and bounded-record decision register. Existing #3608 remains canonical. This stage does not approve production contracts. |
| M1 — prove source boundary ([#3907](https://github.com/elsa-workflows/elsa-foundation/issues/3907)) | Reproducible owned real-source discovery/reference/read/operation and event-to-Elsa feasibility; investigate source commit/access semantics and a separately authored/compiled provider using only public SDK/contracts. Controlled adapters are labeled separately. |
| M2 — agree production design ([#3908](https://github.com/elsa-workflows/elsa-foundation/issues/3908)) | Reviewed spec → plan → tasks resolving contract placement, service-connection access, compatibility, event acceptance and authoring/UX states. Preserve any initiative-owned architecture agreement and the explicit record-backend follow-on. |
| M3 — deliver provider/read/operations | Small optional contracts, authorized metadata/cache/reference services, normal catalog activities and source/type/record/operation authoring. Actual composed-host and independent SDK evidence. |
| M4 — deliver events and projections | Shared Runtime ingress, declared event/filter/feedback behavior, recovery/diagnostics and explicit consumer projections. Coordinate adjacent owners; no blanket dependency on their complete programs. |
| M5 — qualify and close | Real provider journey and fault/security/schema/restart matrix, independently inspected Studio evidence, practical docs/examples, reviewed merges and resulting-main qualification. Preserve the record-backend assessment/outcome and actionable #3916 follow-on without claiming a delivered store. |

Maintain one active implementation leaf and one integration lane; split concrete delivery leaves only
when their contract and ownership are clear. New issues enter needs-triage. Track transitions and evidence
on #3608 and its owning leaves/PRs. Keep consolidation priority one and unrelated owner pauses intact.
Production/shared-system deployment, production credentials, package release and publisher activation
are outside this delivery. A setup task, metadata schema, mock demo, prototype or unmerged PR is not done.

## Coordination and acceptance

- **User Tasks/forms #3577:** delivered through #3691/#3706; reuse its lifecycle and validation. Prefill
  copies values, so choose safe projections and document retention after source access changes.
- **Decisions #3602 / #3689:** optional separate domain in design; expose bounded inputs through its
  agreed contract. Do not absorb evaluation or DMN implementation.
- **Dashboards #3604:** active discovery; no agreed saved-query/dataset contract was evidenced. Agree a
  module-owned business-dimension seam; do not invent a parallel query platform here.
- **Triggers #3582 / spec 1217 and messaging #3583 / #3904:** coordinate their proposed shared external
  ingress with the Runtime owner; its authority remains Runtime-owned, and neither whole product is a
  prerequisite. Entity business events retain their declared provider/broadcast semantics rather than
  silently becoming consuming messages.
- **Releases #3590:** owns release mapping and packaging; entity references remain source identities.
- **Activity Data Selection #3882 / #3094:** owns basic nested-value selection. Contribute declared
  entity result metadata and use its agreed bindings; no competing picker/reference language.

Before Studio implementation, define user goal, reference and observable states under
[Studio UX acceptance](../contributing/studio-ux-acceptance.md): empty/populated/loading, invalid and
unsupported capabilities, denied/revoked access, source failure/retry, schema/concurrency conflicts,
keyboard/focus, themes and narrow layouts. Verify actual composed author/save/reopen/publish/run paths;
retain downloadable current-commit screenshots/traces and independent UX inspection.

The qualification matrix must cover a real external source and a separately authored/compiled entity
provider using only supported public SDK/contracts, registered without core changes and proving metadata,
read/operation and event extensibility. A consumer that only calls a first-party provider is insufficient.
Also cover stable references through source/receiver reopen; multiple workflows per record; invalid/denied/foreign
tenant reads and operations; field restrictions; incompatible schema/connection recreation; duplicates,
outages and feedback. Follow-on #3916 separately requires independent record lifetime, authorized management,
concurrent writes/deletion/retention, schema evolution and committed-change events across restart before
that backend may be called delivered. Risk-based targeted tests and required architecture/maps/CI gates remain mandatory.
Record passed, failed, skipped, cancelled, unavailable and incomplete separately. Preliminary source/runtime
experiments are recorded on #3907; no production provider/SDK/UX qualification is claimed by this plan.
