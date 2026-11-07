# Feature Specification: Entity-aware integrations

**Feature Branch**: `agent/codex/3908-entity-integration-design`
**Created**: 2026-11-06
**Status**: Draft — full-feature agreement and delivery remain open; the catalog security boundary has owner agreement
**Input**: Deliver [#3608](https://github.com/elsa-workflows/elsa-foundation/issues/3608) end to end, beginning with external entity-aware integrations. [M2 #3908](https://github.com/elsa-workflows/elsa-foundation/issues/3908) owns this specification. The owner selected [service connections first](https://github.com/elsa-workflows/elsa-foundation/issues/3608#issuecomment-6672133904) and the optional business-record backend as an explicit [follow-on #3916](https://github.com/elsa-workflows/elsa-foundation/issues/3608#issuecomment-6672584508).

This specifies user and integrator outcomes for external authoritative applications. Elsa learns what a source permits, retains stable references and orchestrates its supported operations/events. It does not become the source's record authority or require a mirror store. Opinionated Expense Claim, CRM, sales or HR applications remain separate compositions.

**Agreement boundary**: the owner [approved the catalog security boundary](https://github.com/elsa-workflows/elsa-foundation/pull/3923#issuecomment-6675758688) at `1bf3d5955a42222a5b62e4b0d463e17c43eedd16`: retained public/protected requirements, protection through forks/missing providers, explicit legacy classification, one catalog and the existing Integrations family. The scoped [catalog plan](plan.md) develops that unit; it does not plan or approve the complete entity feature. [M1 #3907](https://github.com/elsa-workflows/elsa-foundation/issues/3907), private-source tenant preservation [#3917](https://github.com/elsa-workflows/elsa-foundation/issues/3917), production disclosure qualification, shared external-event ingress and workload authority remain incomplete. No full-feature task list or production acceptance is claimed. The [delivery plan](../../docs/plans/entity-aware-integrations.md) carries the staged critical path.

## User Scenarios & Testing

### User Story 1 — Discover only the business data I may use (Priority: P1)

A workflow author selects an allowed business connection and sees understandable source types, fields, single-record relationships, named operations and event coverage. The author selects an Expense Claim reference without copying the source record into a new Elsa database.

**Why this priority**: Authors need a trustworthy description of source data before they can automate it safely.
**Independent Test**: With a real source containing allowed and restricted claim/employee data, select the source/type/reference, inspect supported values and restrictions, save and reopen the authoring document, then compare the retained reference with the source identity.

**Acceptance Scenarios**:

1. Given an allowed connection, when the author chooses Expense Claim, then available scalar fields, employee and attachment references, supported reads/queries/actions/events and applicable limits are understandable; unsupported features have an explanation.
2. Given two tenants and two source connections with the same local record identifier, when each selects a claim, then each reference retains its own source and trusted tenant context and cannot resolve to the other's record.
3. Given access to a type but no access to a record or sensitive field, when the author searches or opens it, then protected records, values and private metadata are not disclosed, including through counts, options or assistance.
4. Given a source outage or no matching records, when selection is attempted, then unavailable and empty are different states and a recoverable failure does not erase the saved selection.

### User Story 2 — Read values and invoke a meaningful source operation (Priority: P1)

An author chooses a claim, reads supported values and configures Approve Claim or another source-declared operation through separately typed arguments. The source validates the operation and enforces its own permissions and business rules.

**Why this priority**: Entity awareness must remove repeated raw-payload construction while preserving the source's authority.
**Independent Test**: Author, save, reopen, publish and run a read plus a named operation against a real source; inspect the actual source change and refusal cases rather than relying on workflow completion alone.

**Acceptance Scenarios**:

1. Given a source operation with amount and decision-note arguments, when the author binds them separately, then values and distinct source field names survive authoring and execution without constructing a raw request payload for that operation.
2. Given a decimal amount requiring exact representation, when it passes from an authored value or earlier workflow data into an operation, then the source receives the intended value without silent rounding or conversion through an imprecise number.
3. Given an invalid state transition, lost permission or stale expected record version, when the operation runs, then the source refuses it and Elsa shows the relevant validation, denial or conflict without reporting success.
4. Given a transport failure after the source might have committed a write, when recovery is considered, then the outcome remains explicitly uncertain until reconciled; the operation is not blindly repeated.

### User Story 3 — Approve a real claim while its record stays external (Priority: P1)

Submitting a claim in the source starts an Elsa approval workflow. A User Task collects a decision, an ordinary workflow branch selects a route, and an authorized source operation applies it. The claim remains independently available after that workflow ends.

**Why this priority**: This is the smallest complete business journey that demonstrates the value of the feature.
**Independent Test**: Complete the source-submit → workflow → User Task → source-decision journey, reopen both systems and inspect the source record, workflow decision and resulting source operation.

**Acceptance Scenarios**:

1. Given a valid Claim Submitted event, when it is admitted under the supported delivery profile, then the workflow receives a stable claim reference and an explicitly selected event-time projection.
2. Given a waiting approval task, when an eligible user decides, then the resulting source operation uses the current permitted service connection and source validation; a task response cannot grant source access.
3. Given one claim participating in two workflows, when one completes or is cancelled, then the record and the other workflow remain independent; correlation does not reserve one workflow per claim.
4. Given a claim deleted or changed while approval waits, when processing resumes, then the workflow receives the declared missing/conflict/schema result and a useful recovery path rather than acting on a different record or silently using stale values.

### User Story 4 — Keep source access consistent everywhere (Priority: P1)

A source administrator controls who may use a service connection and what that source account may see or do. Authors, operators and assistants receive the same current restrictions, including when opening an old saved operation or inspecting its metadata.

**Why this priority**: A service account can have wider source rights than an individual Elsa user; its existence must not grant those rights to everyone.
**Independent Test**: Exercise the same private source/type/operation through selection, direct detail, construction, assistance, publication and execution as allowed, denied and foreign-tenant actors, then revoke access and repeat.

**Acceptance Scenarios**:

1. Given a connection that an Elsa user may not use, when the user requests interactive authoring or source access, then that request refuses without exposing private operation schemas or record values. Background execution instead requires explicitly authorized workflow/workload resource authority; an author or task participant's permission is not implicitly that authority, and a participant response or event payload grants neither kind of access.
2. Given a private tenant-owned operation version, when it is provisioned and read through the normal catalog, then its ownership remains private and a foreign tenant cannot retrieve or construct it.
3. Given revoked interactive access to a saved workflow or cached metadata, when the user reopens, publishes, starts an interactive run or inspects protected material, then current caller access is rechecked. Previously accepted background work follows the agreed current/retained Runtime responsibility contract; authoring revocation does not silently define workload-resource revocation or erase accepted work.
4. Given a denied metadata or value read, when diagnostics are shown, then they identify a safe corrective action without revealing credentials, protected payloads or another tenant's resource existence.

### User Story 5 — Understand event delivery and recover safely (Priority: P1)

An author uses source events both to start automation and to continue an existing workflow that is waiting for a matching business occurrence. An operator sees the guarantees the source actually supplies, what Elsa has accepted and which workflow deliveries are still owed. Duplicate events, outages and workflow-originated updates have deliberate behavior.

**Why this priority**: Events that silently disappear, repeat writes or create feedback loops undermine reliable automation.
**Independent Test**: Exercise an actual source event that starts a workflow and a named Claim Approved event that resumes existing workflows after receiver restart. Include duplicate/reordered deliveries, source and receiver interruption, partial workflow delivery and an operation that produces another entity event under the declared profile.

**Acceptance Scenarios**:

1. Given a source without deleted events, before-images, replay or ordering, when an author configures an event, then unsupported capabilities remain explicit and cannot be selected as if supported.
2. Given repeated delivery of the same declared source event to two independent workflow consumers, when processing resumes after interruption, then each retains its own documented delivery responsibility and duplicate handling; suppressing one consumer does not suppress the other.
3. Given a source notification before a transaction that later rolls back, when it reaches the integration, then it is not presented as proof of a committed source change. Only the profile's declared commit evidence supports that claim.
4. Given a lost acknowledgement or source outage, when an operator reviews the event, then source observation, durable Elsa acceptance, workflow delivery and business effect are distinct; retries/reconciliation follow the stated boundaries.
5. Given a workflow update that emits another entity event, when the provider supports causation/origin, then the configured feedback rule is applied. If it does not, a documented supported alternative and its limitation are visible; all events for the record are not indiscriminately suppressed.
6. Given an authored Claim Approved wait correlated to a stable claim reference, when the workflow is saved/reopened/published, reaches that wait and the receiver restarts, then the next matching declared source event resumes the same existing execution through the agreed Runtime contract. Its observed continuation and retained recovery responsibility are required; starting another workflow alone does not pass.
7. Given two independent running workflows waiting for Claim Approved on one record and selected as recipients under the agreed contract, when that event is accepted and delivery is interrupted, then each wait retains its own delivery/resumption responsibility. A successful delivery to one does not consume the other's obligation, and an event with the same local record ID from a different source or tenant resumes neither wait.
8. Given an accepted event whose recipient selection is fixed under the agreed contract, when the event is redelivered before or after receiver restart, then an already-consumed wait is not resumed again within the declared duplicate-retention horizon. Within that horizon, a later wait created after that recipient selection is fixed cannot acquire the original event through its duplicate delivery.

### User Story 6 — Use approved business dimensions across process tools (Priority: P2)

A module exposes a selected set of authorized business values for task/form prefill, decision inputs and dashboard queries. Users consume meaningful claim dimensions without gaining arbitrary access to the source database.

**Why this priority**: Business data is useful across a process, while the same access and retention rules must remain understandable.
**Independent Test**: Use explicitly exposed claim dimensions in a form, a decision input and a dashboard contribution through each owning module's agreed surface; repeat with denied fields and after revocation.

**Acceptance Scenarios**:

1. Given a declared projection, when a permitted consumer requests it, then only its approved fields, filters and limits are available; joining arbitrary source tables is not offered.
2. Given event-time/form-prefill copies and a current live read, when the user inspects them, then their observation time and retention/access behavior are distinguishable rather than all appearing current.
3. Given an absent optional consumer module, when integrations are composed, then supported metadata/read/operation/event use remains available; the missing consumer contribution is explained without creating a second decision, form or query platform.

### User Story 7 — Add a provider through the supported extension path (Priority: P2)

A third-party integrator implements a source provider and composes its workflow and authoring support using the documented public SDK. An operator installs only the integration features required by the deployment.

**Why this priority**: Third-party sources must be first-class without changes to core workflow execution or a mandatory business-record backend.
**Independent Test**: An independent implementer builds and registers a provider using only supported public packages, then demonstrates metadata, references, reads, operations and events in an ordinary composed host.

**Acceptance Scenarios**:

1. Given independently built provider packages, when composed with the supported host, then they contribute their declared types/operations/events without private APIs, friend access or core source edits.
2. Given a runtime deployment of already published workflows, when design tooling is absent, then the supported integration remains executable and source permissions still apply; authoring, runtime and combined deployment limits are documented and checked.
3. Given an external-only deployment, when integrations are enabled, then no Elsa-owned business-record store or CRM module is required.
4. Given an unsupported provider/schema/runtime combination, when configuration or publication is checked, then a located compatibility refusal precedes an apparently runnable unsupported artifact.

### Edge Cases

- A type or field disappears, changes its identifier/type/nullability, becomes sensitive or loses permission during a long-running workflow.
- A connection's credentials rotate; the connection is deleted/recreated; or its target changes while retained references exist.
- A record identifier is reused by the source after deletion; historic and live-reference behavior must follow a declared source identity/incarnation policy.
- A relationship target or attachment is inaccessible, deleted or external to the permitted tenant; no binary copy or employee-management authority follows from a reference.
- A provider returns a partial record, an unavailable field, an absent field and an explicit null; these are not interchangeable.
- A metadata/selection request finishes after the author selected a different source, or access is revoked while a cached result is visible.
- Two operators submit conflicting changes; a source accepts an operation but the response is lost; an operation's idempotency window expires.
- An event has no stable source identifier, no sequence, no images, only requested-field information, or an oversized/partial projection.
- Events arrive twice, out of order, after a long outage or after a subscription/permission/schema change; receipts and retained delivery work must follow the agreed profile.
- A flow's source update emits another matching event; a legitimate user edit to the same record must remain observable.
- One workflow delivery fails while another succeeds; a source or consumer is disabled or removed with accepted work outstanding.
- Two existing event waits correlate to one record; a same-local-ID event belongs to another source/tenant, or a duplicate original event arrives after consumption and after a new wait is created.
- UI states include empty/populated/loading, validation, unsupported, denied/revoked, unavailable, conflict, stale schema, uncertain write and recovery, including keyboard/focus, themes and narrow layouts.

## Requirements

### Functional Requirements

#### Discoverability, identity and source authority

- **FR-001**: Users MUST discover provider-declared entity types, stable field identifiers, understandable labels, supported portable value types and limited single-record relationships through an allowed source connection. The initial supported value profile covers text, decimal, boolean, date/time, bounded choice and typed record/attachment references; other shapes MUST be explicitly unsupported.
- **FR-002**: Each type/field/operation/event MUST declare applicable read, bounded query, write/operation, sensitivity and availability capabilities. Unsupported, denied, unavailable and absent information MUST NOT be presented as a supported empty value or successful result.
- **FR-003**: Selection and query MUST remain bounded by declared filters, ordering, page/result limits and current record/field access. Counts and continuation information MUST NOT disclose restricted records.
- **FR-004**: Stable references MUST identify the source, type and opaque record identity in trusted tenant/partition context, with sufficient source/connection incarnation information to prevent silent retargeting. Schema revision, record revision and write-concurrency token MUST remain distinct.
- **FR-005**: Credential rotation MUST preserve otherwise valid references. Connection recreation, source retargeting, record-identifier reuse or incompatible identity changes MUST either preserve meaning through an explicit reviewed mapping or refuse resolution with a useful recovery result.
- **FR-006**: External systems MUST retain record validation, domain rules, permissions and lifecycle authority. Reading, linking, cancelling or completing a workflow MUST NOT implicitly copy, delete or take ownership of the source record. Multiple workflows MUST be able to use the same record independently.

#### Authoring and operations

- **FR-007**: Authors MUST select source, type, reference and supported operation/event through normal Studio authoring, save/reopen the selection, and review located validation before publication. Supported named operations MUST expose separate typed arguments/results without requiring raw request payloads for every call.
- **FR-008**: Distinct stable argument keys and arbitrary source names MUST remain distinct through authoring, publication and execution. Exact declared decimals, dates, choices, nullability and reference values MUST preserve their intended meaning; unsupported conversions MUST refuse rather than silently round or reinterpret values.
- **FR-009**: Reads MUST state whether they obtain current values or a supported historical revision. Event-time snapshots and saved prefills MUST retain their own observation/schema context rather than being silently treated as current live records.
- **FR-010**: Source-owned operations MUST validate source access and business rules, apply expected-version checks where supported, and return recognizable success, validation, conflict, denial, missing, incompatible and unavailable outcomes. An unsupported concurrency guarantee MUST be visible before use.
- **FR-011**: Each operation MUST declare retry/idempotency support and its scope/window. When commitment is uncertain, Elsa MUST retain that uncertainty and provide source-supported reconciliation or an explicit operator decision; it MUST NOT blindly repeat a potentially committed write.
- **FR-012**: Saved/published workflows MUST retain the selected source/type/operation schema contract. Later metadata changes MUST NOT silently rebind them to a different field, operation or incompatible version. A new compatible version may be selected explicitly without rewriting historical versions.

#### Current authority and private metadata

- **FR-013**: The initial execution profile MUST use configured tenant/service connections. Elsa MUST authorize who may use the connection and the requested integration function; the source MUST enforce the service account's actual record, field and operation rights. Delegated individual-user source identity is outside this initial profile.
- **FR-014**: Trusted host identity MUST establish tenant/partition and the applicable interactive caller or explicitly authorized workflow/workload resource authority for source access and event processing. Untrusted authored identifiers, event payloads and permission snapshots MUST NOT grant authority or switch tenants. This does not prohibit a legitimate Runtime-owned retained acceptance grant validated under the agreed delivery-responsibility contract; a task response alone grants neither interactive nor workload authority.
- **FR-015**: Metadata discovery, record selection and business-dimension exposure MUST validate the applicable access authority; every fresh source read or operation MUST reauthorize current source access for its interactive caller or authorized workload resource. Event admission/delivery MUST validate authority against the agreed current/retained Runtime responsibility contract, without preselecting an unresolved live-versus-retained grant policy or silently losing accepted work. Metadata caches MUST be scoped to source/incarnation, tenant, schema and access context, with declared bounded staleness; unavailable authority MUST NOT become an access grant.
- **FR-016**: Private source/operation metadata MUST remain private through normal catalog provisioning and every discovery, direct-detail, construction, assistance, publication and inspection path. Visibility/addability MUST NOT be the only disclosure control. Retaining metadata or an artifact alone MUST NOT grant source access.
- **FR-017**: Source- and field-sensitive values MUST follow explicit access, protection and retention rules across snapshots, form prefills, decisions, dashboards, events and diagnostics. Copying an allowed value into retained workflow/task data MUST be disclosed; later source revocation MUST NOT be represented as erasing copies that still exist.
- **FR-018**: Refusals and operational diagnostics MUST expose safe connection/source/event/reference context, the state of the request and a useful recovery action, without secrets, protected source payloads or foreign-resource existence.

#### Events, delivery and feedback

- **FR-019**: Providers MUST declare supported created/updated/deleted and named business events independently, including filters, source commit boundary, before/after or partial-image coverage, replay/retry limits and ordering. Missing images, requested fields, proven changed fields and explicit nulls MUST retain their distinct meanings.
- **FR-020**: An event MUST retain its declared source/event identity, record reference, occurrence/receipt time and schema context, plus source revision/sequence, correlation, causation and origin when supplied. A record ID alone MUST NOT act as an event ID; correlation MUST NOT imply a single workflow per record.
- **FR-021**: If a source lacks a stable event identity, committed-change evidence, replay or ordering, the provider MUST declare the limitation and refuse unsupported stronger guarantees. Receipt order MUST NOT be substituted for a source ordering guarantee.
- **FR-022**: Users/operators MUST distinguish source observation, durable Elsa acceptance, pending/accepted workflow delivery and business effect. A durable receipt claim MUST be backed by retained recovery responsibility under the agreed shared Runtime ingress; direct routing alone MUST NOT be labeled durable acceptance.
- **FR-023**: The supported profile MUST define duplicate identity and its retention horizon per source/subscription/consumer, the timing and meaning of consumer selection, and the effects of later configuration, access and schema changes. Independent consumers MUST retain independent delivery progress; suppressing one MUST NOT erase another's obligation.
- **FR-024**: Accepted delivery responsibility MUST survive supported receiver interruption and partial consumer failure. Retries, expiry, terminal failure, manual recovery and source-outage behavior MUST follow the declared profile with observable outcomes; no transaction across unrelated source/Elsa stores or exactly-once external side-effect promise is implied.
- **FR-025**: Authoring MUST support declared source-side or admitted event filters, with their evaluation location/time explained. Filters MUST NOT claim a before/after transition when the source only supplies update-request membership or current state.
- **FR-026**: Providers and authors MUST have a deliberate feedback policy for workflow-originated changes. Supported causation/origin MUST be propagated; otherwise a documented source-supported alternative and remaining limitation MUST be visible. A record-wide suppression shortcut MUST NOT hide legitimate independent changes.

#### Module contributions and delivery experience

- **FR-027**: Participating modules MUST expose selected authorized business dimensions to forms/prefill, decision inputs and dashboards through the respective owning module's agreed contribution surface. Allowed fields, query/filter limits, sensitivity, observation time and retention MUST remain explicit; arbitrary external database access is outside scope.
- **FR-028**: Third-party integrators MUST be able to implement/register entity metadata, references, reads, operations and events through supported public SDK/contracts without core workflow/runtime edits, private APIs or friend access. Calling only a first-party provider does not prove this extensibility.
- **FR-029**: Entity operations MUST participate in the existing activity catalog and normal authoring/publication/runtime lifecycle. The feature MUST NOT introduce a second activity catalog, a competing nested-binding language or an assumption that typed arguments can safely be assembled through imprecise scripting.
- **FR-030**: Optional integration, workflow-authoring and consumer contributions MUST preserve supported authoring-only, runtime-only and combined host shapes. External-only deployments MUST require neither an Elsa-owned business-record store nor a mandatory domain application.
- **FR-031**: The UI MUST preserve saved values while showing empty, loading, populated, validation, unsupported, denied, source-failure, schema/conflict and uncertain-result/recovery states. Stale asynchronous results MUST NOT replace a newer selection. Supported journeys MUST be usable by keyboard with coherent focus, light/dark themes and narrow layouts.
- **FR-032**: Delivery MUST include reproducible real-source and independent-provider examples, user/integrator documentation, provider capability/guarantee tables, troubleshooting and explicit qualification outcomes. Mocked/control adapters, experimental bridges, unavailable evidence and production acceptance MUST remain distinguishable.
- **FR-033**: Authors MUST be able to configure a declared entity-event wait and stable record correlation through normal authoring, save/reopen/publish it and resume that same running workflow through the agreed Runtime delivery/resumption contract. Correlation MUST preserve source/type/record identity and trusted tenant/incarnation context; a same-local-ID event from another source or tenant MUST NOT match. Separate matching waits for one record MUST retain independent recipient/delivery responsibility under the agreed selection contract. Within the declared duplicate-retention horizon, redelivery of an accepted event MUST NOT resume an already-consumed wait again or attach that original event to a wait created after its recipient selection was fixed. Pending accepted wait/resume work MUST survive supported receiver restart. Start-only routing, a fake publisher workflow or direct routing labeled as durable MUST NOT satisfy this requirement; no new ingress or resumption wire interface is selected here.

### Key Entities

- **Source**: an authoritative application identified independently of one credential set, with a declared integration profile.
- **Service connection**: a configured source execution identity that an Elsa user/workload must be permitted to use; it does not transfer all service-account rights to every user.
- **Entity type/schema revision**: the source-owned type/field/reference/operation/event description against which authoring is validated.
- **Record reference**: source/type/opaque record identity and trusted tenant/incarnation context; it is not a mirrored record or a workflow identity.
- **Operation version**: a source-owned business action with typed argument/result meanings, compatible schema identity, access, concurrency and retry declarations.
- **Entity event**: a declared source occurrence with identity, reference, time/context and only the data/guarantees actually supplied.
- **Event receipt/delivery responsibility**: evidence of what Elsa accepted and what remains owed under the agreed shared delivery contract; it is separate from source commit and business effect.
- **Entity-event wait/correlation**: a retained wait in an existing workflow for a declared source event and stable record context; it neither locks the record to one workflow nor starts a replacement execution.
- **Business projection**: a module-owned, bounded and authorized set of values/dimensions for a specific consumer, with observation and retention policy.

## Success Criteria

### Measurable Outcomes

- **SC-001**: In the complete real-source Expense Claim journey, an author discovers the type, selects a stable reference, configures a read and named operation, saves/reopens/publishes the workflow, and completes a source-submitted approval that visibly updates the same authoritative record. Read/operation success is evidenced independently of workflow completion.
- **SC-002**: The same source record participates in at least two independent workflows and remains available after one completes/cancels and after source/receiver reopen; stable identity, selected schema and current record revision remain distinguishable.
- **SC-003**: Two distinct operation/argument sets preserve their names, selected immutable schema and exact values through save/reopen/publication/execution; changing a schema at an existing immutable identity is refused, and explicitly selecting a new compatible version does not rewrite old workflows.
- **SC-004**: All qualified denial cases across two tenants, two source connections, restricted records/fields and revoked access disclose zero protected values/private schemas and perform zero unauthorized source writes, including direct detail/construction/assistance paths.
- **SC-005**: Every declared event-capability case has an observed outcome: supported events both start automation and resume existing workflows as declared; unsupported events/images/replay/order cannot be configured as supported. A real named Claim Approved event resumes two independently waiting existing executions for one record after receiver restart, with zero matches for same-local-ID events from another source/tenant. Within the declared duplicate-retention horizon and agreed recipient-selection contract, duplicate delivery resumes neither an already-consumed wait nor a later wait created after the original event's recipient selection was fixed. Partial-failure, outage, expiry, reorder and feedback cases leave no accepted responsibility silently unaccounted for.
- **SC-006**: A separately authored/compiled provider contributes metadata, references, reads, operations and events using only supported public distributable contracts in an ordinary host; evidence names the package versions, consumer implementation and capabilities actually exercised.
- **SC-007**: Approved business dimensions are usable through each agreed form/decision/dashboard contribution, with denied fields withheld and retained-vs-live values distinguishable; absence of one optional consumer does not disable the other supported integration functions.
- **SC-008**: All specified Studio states and author/save/reopen/publish/run/recovery journeys have current-commit downloadable and independently inspected experience evidence, including keyboard/focus, both themes and narrow layout; failures/not-run cases remain visible until resolved.
- **SC-009**: External-only supported host compositions need no business-record store or mandatory CRM, and every documented supported/unsupported profile has a reproducible example and recovery explanation.
- **SC-010**: Every applicable finite acceptance row below is passed on the reviewed merged implementation and resulting main before #3608 is reported delivered. The optional-record outcome is its assessment, owner decision and actionable #3916 follow-on, never a delivered record backend.

### Finite acceptance evidence

These are required evidence categories, not passed results. The plan following specification review must bind each row to concrete provider/profile, current commit and reproducible witness; an unavailable required category remains incomplete.

| ID | Required witness | Requirements/outcomes |
|---|---|---|
| A01 | Real external metadata, allowed selection, stable reference, supported read/domain operation and full source-submit → task-decision → source-update journey; real employee/attachment reference behavior or explicit provider limitations are exercised | US1–3; FR-001–012; SC-001 |
| A02 | Two workflows for one record; completion/cancellation independence; actual source/receiver reopen and historical/current revision checks | US3; FR-004–006,009,012; SC-002 |
| A03 | Two operation schemas, arbitrary/distinct names, exact decimal/date/choice/reference values and ordinary value bindings; immutable-version conflict, new-version selection and old-version execution | US2; FR-007–012,029; SC-003 |
| A04 | Two tenants/two connections; denied record/field/connection; cache and saved-reference revocation; private catalog/list/detail/construct/assistant/publication/inspection access | US1,4; FR-003–005,013–018; SC-004 |
| A05 | Source schema/field/identity change, deleted/reused record ID, connection rotation/recreation/retargeting and inaccessible relationship/attachment | US1–4; FR-004–005,009–012,015–018; SC-002–004 |
| A06 | Stale and conflicting writes, denied/invalid source operations, response lost before/after possible commit and source-supported idempotent/reconciled recovery | US2; FR-010–011; SC-001,004 |
| A07 | Actual named business event plus each supported lifecycle kind; unsupported kinds/images/filters; pre-commit rollback notification; partial/oversized data and no-sequence/no-identity profile limits | US5; FR-019–022,025; SC-005 |
| A08 | Author/save/reopen/publish a Claim Approved wait; retain two independent existing waits for one record, restart the receiver and resume both original executions through the agreed Runtime contract. Exercise same-local-ID events from other sources/tenants, partial fan-out failure and duplicates before/after consumption and restart; within the declared duplicate-retention horizon, the original accepted event cannot satisfy a later wait created after its recipient selection was fixed. Cover source/receiver outage, retention boundary/expiry and safe replay/recovery; start-only consumers cannot pass | US3,5; FR-020–024,033; SC-005 |
| A09 | Workflow-originated update event and legitimate independent update to the same record, with and without supported causation/origin | US5; FR-025–026; SC-005 |
| A10 | Authorized/restricted form prefill, decision input and dashboard dimensions through agreed owner contributions; retained/live policy and absent optional module | US6; FR-017,027,030; SC-007 |
| A11 | Independent implementation using distributable public packages, real contribution/registration of metadata/read/operation/event functions and supported host compositions | US7; FR-028–030,032; SC-006,009 |
| A12 | Composed Studio positive/failure/recovery states, including event-wait authoring/correlation, exact-current evidence, independent UX inspection, user/integrator examples and safe diagnostics | US1–7; FR-018,031–033; SC-008–009 |
| A13 | Current-head review/checks, merge and resulting-main evidence linked to the original feature criteria; follow-on outcome preserved | SC-010 |

## Assumptions

### Confirmed scope and exclusions

- External applications remain authoritative. The owner-selected initial identity profile is service connections; delegated end-user source identity is a later design/product choice.
- The optional Elsa-managed record backend is the explicit bounded [#3916 follow-on](https://github.com/elsa-workflows/elsa-foundation/issues/3916). Its storage representation, module, management UI, schema migrations and record-commit event implementation are not delivered here.
- Expense Claim is a representative process composition. This feature does not absorb the User Tasks, Decisions, dashboards, standalone trigger/message or release products, require a new CRM/HR application, mirror whole external stores, allow arbitrary SQL/database access or build an unbounded application platform.
- Initial relationship coverage is single-record references and separately authorized attachment references. Arbitrary nested object graphs, many-to-many modeling, employee management and binary storage are excluded.
- Source credentials and test resources for real-provider qualification must be owned isolated non-production resources. Production/shared deployment, production credentials, package release and publisher activation remain outside this authorization.

### Evidence available before this provisional specification

| Observation | Actual result | Limit |
|---|---|---|
| Separate Orchard Core 3.0.1 source with real content/identity/persistence | Metadata/read/authorized source actions, decimal validation, sequential stale-version refusal, rollback observation and source reopen passed | Disposable source glue/admin cookie baseline; employee is text, attachments/field/cross-tenant/concurrent race/profile qualification are incomplete |
| Actual source event → Elsa workflow using normal connection-backed reads | Metadata and record responses explicitly returned success; persisted execution/connection and same record survived reopen; two consumers and repeated keyed starts were observed | Experimental post-commit file handoff can lose events before append; direct routing is not durable external admission; workflow-to-source mutation/service credentials are incomplete |
| Separately compiled public typed-input extension | Literal and ordinary request-value bindings preserved exact decimal, distinct argument names and selected synthetic schema through the normal compiler/runtime | Source project outputs reused; no released-package SDK, real source operation or retained-result-persistence claim |
| Synthetic normal catalog/source publication/workflow publication/runtime/version/reopen | Immutable schema rejection, explicit new operation version, two operations and old-artifact reopen/execution passed | Global synthetic operations; not real source privacy or independent packaged-SDK acceptance |
| Actual Orchard service-account profile and stock Elsa HTTP operations | Official client credentials/role permissions passed 28 source cases and source restart. Normal Elsa workflows submitted/approved the real claim; exact HTTP statuses, source state/revision and secret-backed connection/execution reopen were observed | Custom disposable source glue, pre-acquired bearer token, global source permissions/one tenant; no automatic Elsa OAuth, typed entity SDK, User Task, private catalog, live-token revocation or durable event qualification |
| Private-tenant source publication | **FAILED baseline**: explicit tenant ownership was dropped and another tenant could read the resulting global definition | #3917 is a prerequisite; the broader catalog boundary now has owner agreement, while implementation and qualification remain incomplete |

Canonical experiment records and failed/incomplete paths stay on [#3907](https://github.com/elsa-workflows/elsa-foundation/issues/3907) and [#3917](https://github.com/elsa-workflows/elsa-foundation/issues/3917). These observations justify further specification; none substitutes for A01–A13 production acceptance.

### Scoped agreement and remaining delivery gates

The [catalog disclosure and package boundary](design/catalog-disclosure.md) has [owner agreement](https://github.com/elsa-workflows/elsa-foundation/pull/3923#issuecomment-6675758688) for its stated unit. The [catalog plan](plan.md) and subsequent technical contracts must stay inside that boundary and receive normal review. The [shared Runtime ingress assessment](design/runtime-ingress.md) remains separate; its proposed ABI is not adopted by the catalog decision.

1. **Catalog unit and provider contracts**: implement the agreed catalog boundary through reviewed concrete contracts, forced admission, migrations/legacy classification and complete disclosure enforcement. The proposed ordinary versioned private catalog path still depends on #3917's qualified tenant preservation. The minimum entity-provider SPI and actual private-source policy/credential profile require their own technical qualification; no production profile is enabled merely by adding the catalog marker. The existing Integrations family is agreed; a different new root domain would need the applicable architectural decision and constitution process. This spec changes neither constitutions nor ADR 0045. [Catalog/domain gates](../../.specify/memory/constitution.md#e21-the-elsa-domain-tree) remain applicable. Activity versioning/reconciliation and authored-state sections marked draft/provisional are not treated as ratified approval.
2. **Shared external-event responsibility**: [standalone trigger PR #3912](https://github.com/elsa-workflows/elsa-foundation/pull/3912) and [messaging PR #3904](https://github.com/elsa-workflows/elsa-foundation/pull/3904) contain unagreed shared-ingress proposals with different product delivery semantics. The Runtime owner and consumers must agree admission, recipient/retention, duplicate, access/revocation and recovery responsibility before an integration freezes or implements that contract. This includes existing workflow waits/resumption, when an event's recipient selection becomes fixed and protection against applying that original accepted event to later waits through duplicate delivery. Entity events retain declared provider/broadcast behavior; consuming-message semantics are not silently adopted. No new inbox, fake publisher workflow, transaction promise or ingress/resumption ABI is selected here.
3. **Representative provider and consumer contributions**: M1 must qualify the selected real source/service-credential profile and supported capability matrix. Agree the source's reference/schema/record/field/operation guarantees and its event commit/retry/feedback limits. Coordinate projection contributions with [forms #3577](https://github.com/elsa-workflows/elsa-foundation/issues/3577), [Decisions #3602](https://github.com/elsa-workflows/elsa-foundation/issues/3602), [dashboards #3604](https://github.com/elsa-workflows/elsa-foundation/issues/3604) and [data selection #3882](https://github.com/elsa-workflows/elsa-foundation/issues/3882); their current complete products are not blanket prerequisites. No Dataverse tenant or production connector is presumed.

The approved catalog unit may proceed through its scoped plan, reviewed tasks and implementation independently. Other units retain their stated agreement gates, including explicit workload source-use authority and shared external-event responsibility. Applicable initiative-owned decisions retain their separate agreement process; drafting or merging prose alone does not establish that agreement or complete #3908/#3608.
