# Feature Specification: Standalone triggers and workflow subscriptions

**Feature Branch**: `agent/codex/3582-standalone-triggers`

**Created**: 2026-11-05

**Status**: In progress — M0 reviewed at f064e47b7/PR #3912 on 6 November 2026; resumed 13 November 2026 as bounded stacked slices; product qualification outstanding

**Input**: [Canonical feature #3582](https://github.com/elsa-workflows/elsa-foundation/issues/3582), with [owner delivery authorization and milestone record](https://github.com/elsa-workflows/elsa-foundation/issues/3582#issuecomment-6671646417), 5 November 2026.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Manage shared entry points (Priority: P1)

An operator creates an End of business day schedule and associates Reconcile Accounts, Generate Reports and Clean Up Imports. Each starts independently. The operator changes the schedule or pauses it without editing the business workflows. A workflow author can inspect and edit the same associations from the workflow editor.

**Why this priority**: Independent operations and discovery from both directions justify a resource beyond a small workflow that dispatches other workflows.

**Independent Test**: Create a trigger and associations, reload both surfaces, change and remove an association from each, and inspect the same persisted result. Publish unchanged business graphs and prove one source starts several workflows and one workflow starts from several sources.

**Acceptance Scenarios**:

1. **Given** no triggers, **when** an operator creates a schedule, **then** it is saved paused and can be reopened with its configuration intact.
2. **Given** several published workflows with compatible declared inputs, **when** the operator associates and enables them, **then** a due occurrence starts each independently using ordinary workflow entry.
3. **Given** an existing association, **when** either management surface edits it, **then** both surfaces show the same saved revision after reload without changing the workflow graph or draft.
4. **Given** a shared active trigger, **when** its configuration or associations change, **then** the operator sees affected workflows and a concrete impact review before applying the change.
5. **Given** executions already running, **when** the trigger is paused or edited, **then** those executions keep their original inputs and versions.
6. **Given** a live resource, **when** an update is refused because validation or revision checks fail, **then** its prior effective configuration, endpoint reservation, associations and schedule remain intact.
7. **Given** accepted pending work, **when** the trigger/association is deleted or its source is removed, **then** fresh admissions stop and accepted work/history remain retained; deletion does not implicitly cancel it.

### User Story 2 — Invoke a business workflow over HTTP (Priority: P1)

An integration author creates an HTTP trigger and chooses its response mode explicitly. A workflow-owned response endpoint has one target that produces the response. An asynchronous endpoint accepts work for its subscribers and exposes durable delivery outcomes.

**Why this priority**: An endpoint must have understandable response ownership and must not acknowledge work it failed to accept.

**Independent Test**: Use real requests against the composed host in both modes; inspect response ownership, accepted work, invalid input, unavailable targets, conflicts, disconnection, timeout and recovery.

**Acceptance Scenarios**:

1. **Given** workflow-owned response mode and one eligible target, **when** a valid request arrives, **then** only that target owns the response, and the transport reports actual response delivery separately from workflow execution status.
2. **Given** workflow-owned response mode, **when** a second response owner is proposed, **then** activation is refused with a useful diagnostic.
3. **Given** asynchronous fan-out and compatible subscribers, **when** the request is durably accepted, **then** the response identifies the accepted delivery, does not claim business completion and no subscriber can write to the original response.
4. **Given** invalid source input, an unavailable subscriber or a persistence failure, **when** intake cannot accept the promised work, **then** the response exposes that failure and never presents a successful acceptance for nonexistent work.
5. **Given** a conflicting embedded or standalone HTTP route, **when** either owner attempts activation, **then** at most one normalized endpoint owner becomes authoritative.
6. **Given** Require all subscribers, **when** any enabled subscriber cannot be admitted, **then** none is accepted and the response identifies the admission failure.
7. **Given** Accept available subscribers, **when** some enabled subscribers can be admitted and others cannot, **then** the response explicitly identifies partial acceptance and each accepted/refused outcome; no accepted subscribers means a failed request.

### User Story 3 — Follow publication safely and map declared inputs (Priority: P1)

A workflow author declares business inputs such as AccountId and BusinessDate. Each subscription maps its source data to those inputs. New deliveries follow the selected publication slot's current serving version. Replacing that version rechecks compatibility; incompatible subscriptions become visibly blocked.

**Why this priority**: The graph stays independent of its invocation transport while publication remains the authority for fresh starts.

**Independent Test**: Map distinct HTTP, Event and schedule payloads to one unchanged workflow; publish compatible and incompatible replacements, unpublish, and inspect new versus previously accepted work.

**Acceptance Scenarios**:

1. **Given** a workflow without an embedded trigger activity, **when** a valid standalone delivery is admitted, **then** it enters the ordinary native or supported BPMN execution semantics with validated declared inputs.
2. **Given** a serving-slot replacement, **when** a new occurrence arrives, **then** it targets the new serving version only after mapping validation; it never silently falls back to an older version.
3. **Given** required, optional, defaulted and typed inputs, **when** a mapping is saved or realized, **then** existing input rules apply, field findings identify errors and a preview is distinguished from actual admission.
4. **Given** already accepted work, **when** its target is replaced or unpublished, **then** delivery retains the exact accepted version and inputs while fresh admission observes the current publication state.
5. **Given** a model or response mode unsupported by an installed adapter, **when** a target is selected, **then** the limitation is visible and activation is refused. Native BPMN composition is not reported as proof of direct BPMN model support.

### User Story 4 — Recover independent subscriber deliveries (Priority: P1)

An operator sees that one subscriber started successfully while another is waiting for capacity. Recovery retries only the pending recipient. Pause stops new admissions; cancellation of accepted pending work is an explicit operation.

**Why this priority**: Fan-out must survive failure without repeating work that already started successfully.

**Independent Test**: Force partial dispatch, retry and duplicate requests, crash/restart around acceptance and dispatch, compete workers, and inspect recipient/execution identities and retained evidence.

**Acceptance Scenarios**:

1. **Given** one accepted occurrence, **when** dispatch or recovery is repeated, **then** each subscription converges on its own intended execution and successful recipients are not started again.
2. **Given** accepted work followed by trigger pause or target unpublication, **when** recovery runs, **then** that work remains deliverable against its captured version, while new admissions stop.
3. **Given** pending recipients and separate cancellation permission, **when** the operator explicitly cancels them, **then** cancellation is fenced against dispatch, every outcome is visible and already running workflows are not implicitly cancelled.
4. **Given** configuration, association and publication changes during intake, **when** admission commits, **then** it captures one valid set of resource/subscription/target revisions or refuses the stale attempt.
5. **Given** a delivery, **when** the operator inspects it, **then** acceptance, recipient dispatch, execution completion, response delivery and failures have distinct evidence.

### User Story 5 — Operate Event and schedule sources (Priority: P1)

An operator shares one Event or Timer/Cron trigger across workflows, previews its next firing, and understands what happens during downtime or a clock/timezone change. Existing embedded triggers and mid-flow waits continue to work.

**Why this priority**: These sources provide the initial shared-entry use case and must retain their own broadcast and recurrence semantics.

**Independent Test**: Deliver real Events and due occurrences through the composed host, change schedules while work is due, restart workers and compare standalone starts with embedded starts and bookmark resumption.

**Acceptance Scenarios**:

1. **Given** Event subscribers and existing embedded Event starts/waits, **when** a matching broadcast arrives, **then** independently eligible recipients receive their intended delivery without becoming competing durable-message consumers.
2. **Given** a due schedule shared by several workflows, **when** workers compete or restart, **then** one occurrence owns a frozen recipient set and repeated processing converges per subscription.
3. **Given** elapsed schedule occurrences while the host is unavailable, **when** it recovers, **then** the documented missed-occurrence policy applies and no accidental backlog burst occurs.
4. **Given** an intentional pause, **when** the schedule resumes, **then** the preview and subsequent firing match the documented resume policy.
5. **Given** a Cron timezone or clock change, **when** the operator reviews it, **then** the affected future firing is explicit, accepted work remains unchanged and ambiguous/nonexistent local times follow the documented rule.

### User Story 6 — Install and authorize the feature (Priority: P2)

A module author contributes another trigger type or execution model through supported contracts. An administrator grants resource configuration and operations separately. Users see only accessible resources, targets, diagnostics and available capabilities.

**Why this priority**: Triggers are live resources in a modular, tenant-aware product; the initial families must not become hard-coded host knowledge.

**Independent Test**: Install an independent module consumer, exercise missing/incompatible modules and Design-only/Runtime-only/combined deployments, then repeat direct API and Studio attempts under denied and cross-tenant identities.

**Acceptance Scenarios**:

1. **Given** a third-party trigger type, **when** its module is installed, **then** its descriptor, configuration validation, runtime intake and supported Studio editor participate through the normal extension contracts.
2. **Given** a user without mutation or operation rights, **when** they open a direct URL or call the API, **then** policy is enforced consistently and protected payloads are not disclosed.
3. **Given** a source-provisioned read-only trigger, **when** a user inspects it, **then** ownership and allowed operations are clear; ordinary edits cannot overwrite source-owned configuration.
4. **Given** a resource in another tenant/access scope, **when** a user or worker supplies its identifier, **then** discovery, admission, delivery, retries and diagnostics cannot cross that scope.

### Edge Cases

- No subscribers; last-subscriber removal; subscribers individually paused, unpublished, unauthorized or incompatible.
- Duplicate/retried intake with the same idempotency key and with changed payload, configuration or caller scope.
- A crash before admission, after admission, after enqueue and before recipient acknowledgement; expired or stolen worker leases.
- Concurrent route activation across embedded and standalone owners; route changes while requests are arriving.
- Concurrent config, association, activation, pause, cancellation, unpublication and publication replacement.
- Invalid payloads, expressions, required/default/unknown inputs, numeric precision, unsupported model entry and response capabilities.
- HTTP body limits, malformed requests, unavailable storage, request timeout, no response instruction and client disconnection.
- Schedule downtime, intentional pause, timezone/DST changes, backward/forward clocks, updates with an in-flight occurrence and multi-node recovery.
- Missing provider/module/editor, source-owned settings, stale revision conflict, forbidden direct navigation, keyboard/focus recovery and narrow/theme layouts.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Provide independently persisted live trigger resources with list, create, inspect, edit, enable/pause and delete operations. Initial families are HTTP Endpoint, Event, interval Timer and Cron. Creation starts paused.
- **FR-002**: Preserve activity-based embedded starts and mid-flow waits/bookmark resumption as separate existing mechanisms. Standalone starts MUST NOT require a dummy activity or a hidden parent workflow.
- **FR-003**: Provide one canonical subscription association editable from trigger management and the workflow editor. One trigger can have several independent subscribers and one workflow can have several triggers; duplicate associations to the same trigger/target slot are refused.
- **FR-004**: Keep live resource/subscription configuration outside the workflow business graph and draft. Historical/draft workflow views MUST identify that the displayed associations operate on the current logical workflow and serving slot.
- **FR-005**: Require impact review before applying live configuration/association changes. Show affected workflows, routing/next-firing changes and newly invalid associations. Apply using observed revisions; stale concurrent changes require reload/review. A refused change preserves the prior effective configuration, endpoint reservation, associations and schedule.
- **FR-006**: A trigger with no eligible subscribers MUST NOT report successful acceptance. Saving a paused empty resource is allowed; activation requires at least one eligible enabled subscription. Removing the last subscription requires visible impact confirmation and pauses the trigger atomically. Later loss of all eligibility leaves the resource enabled-but-unavailable, with total/eligible counts and reasons; compatible eligibility restoration permits fresh intake automatically unless explicitly paused.
- **FR-007**: Pausing a trigger/subscription stops new admissions only. Existing executions continue, and already durably accepted work retains its accepted authority. Trigger/subscription deletion and source removal also stop fresh admissions without erasing accepted responsibility or its retained history. Cancellation of pending deliveries is a separate authorized, explicit, revisioned operation with race-safe outcomes.
- **FR-008**: Resolve new subscriber targets through the logical workflow's explicit publication slot, visibly defaulting to `default`. Follow its serving version automatically. An enabled trigger is never independent authority for fresh starts of an unpublished target. A never-published/unavailable slot may be saved as a disabled association with validation pending and a publish/select-slot recovery action; both surfaces prevent enabling it until its actual serving contract validates.
- **FR-009**: At admission capture exact target publication/version, slot revision, executable/model identity, resource/subscription revisions, validated mapped inputs and recipient identity. Accepted work retains that material and authority until its retained delivery lifecycle permits collection.
- **FR-010**: Revalidate subscriptions on activation, editing and serving-contract change, and validate actual realized inputs at each admission. Incompatibility blocks that subscription visibly; it does not rewrite the target or silently select an older version.
- **FR-011**: Map source envelope/payload and explicit literals to declared workflow inputs per subscription. Use existing input types, defaults and validation. Support the installed expression capabilities through a bounded pre-execution context; do not fabricate workflow variables, activity outputs or a workflow execution for mapping.
- **FR-012**: Expose safe mapping preview with source/target contract and field findings. Preview is advisory; it cannot bypass actual admission validation, authorization or publication checks. Persist realized values so retry never reevaluates mapping against changed time/configuration.
- **FR-013**: Enter ordinary native and supported genuine BPMN model root semantics using the appropriate model adapter. Model adapters declare publication, declared-input and response capabilities; unsupported combinations fail before activation. Qualification MUST distinguish direct models from native wrappers.
- **FR-014**: HTTP mode is an explicit, visible required choice: workflow-owned response or asynchronous fan-out. Workflow-owned mode permits exactly one eligible response owner. Async subscribers cannot write to the originating response.
- **FR-015**: Async HTTP success is returned only after durable responsibility for the promised recipient work is committed. Authors choose one admission policy per trigger: **Require all subscribers** (default) refuses the whole request before any recipient acceptance if an enabled subscriber cannot be admitted; **Accept available subscribers** durably accepts the healthy subset and explicitly reports partial acceptance plus accepted/refused outcomes. Zero accepted recipients is a failed request under either policy. The policy is visible beside the HTTP mode and from either association surface; changes require impact review. This governs admission only: after acceptance, dispatch/retry/recovery remain independent under both policies.
- **FR-016**: Validate HTTP shape, size, source authentication and inputs before accepting the affected work. Expose refusal/temporary unavailability and durable acceptance distinctly; include a delivery identifier and authorized status location for accepted work. Acknowledgement never means business completion.
- **FR-017**: Workflow-owned HTTP responses have bounded request lifetime and explicit missing-response/timeout/disconnection outcomes. Preserve execution versus transport outcome distinction. Accepted workflow execution is not automatically cancelled when its requester disconnects.
- **FR-018**: HTTP route ownership uses the same normalized exclusive namespace and atomic conflict boundary for embedded and standalone owners. Async fan-out has one ingress owner, regardless of subscriber count. A newly created paused resource has a proposed, visibly unreserved route; its first successful activation acquires the reservation. Already activated paused resources retain their route reservation and return visible unavailability; deletion or an atomic reviewed route change releases the old reservation. Two unactivated drafts may propose the same route, but at most one can reserve it.
- **FR-019**: Event remains broadcast. Its independent standalone subscriptions supplement eligible embedded starts and waits without turning Event into durable-message consumption or ordered orchestration. Failed/incompatible subscribers do not rerun successful ones or withhold unrelated eligible Event/schedule starts.
- **FR-020**: Resource-owned schedules have stable identities independent of subscriber publications. Each due occurrence captures one frozen recipient set. Fenced ownership and stable per-subscription starts prevent duplicate executions under retry, restart, updates and worker competition.
- **FR-021**: Reuse a bounded missed-occurrence policy: after unplanned downtime, deliver the persisted due occurrence once and advance to the first future occurrence, without replaying the elapsed backlog. If all subscribers are unavailable and none can be accepted, retain that one overdue occurrence with bounded backoff until eligibility returns; do not advance or accumulate every elapsed interval. Distinguish known zero eligibility from uncertain/storage failure, which also retains the cursor without inventing acceptance. After any recipients are accepted, freeze the evaluated set and never add a restored subscriber to that old firing. Intentional pause/resume schedules from the next future occurrence; accepted occurrences remain deliverable.
- **FR-022**: Cron exposes an explicit timezone, default UTC, and next-firing preview including timezone and offset. Persist absolute occurrence instants; define and qualify DST and clock-change behavior. Configuration/timezone changes affect future admissions only, fence stale claims and do not rewrite accepted occurrences.
- **FR-023**: Each admitted delivery has a stable tenant-scoped source identity and one recipient identity per subscription. Repeating an idempotency key with incompatible content is a conflict. Existing embedded keyed-start formats remain unchanged.
- **FR-024**: Dispatch/recovery is independently tracked per recipient, with bounded retry/backoff and terminal/refused/cancelled outcomes. Recover only unfinished recipients and converge on a stable execution identity for already enqueued/started work.
- **FR-025**: Durable admission, recipient capture/retention and source settlement MUST survive the supported store/process topology. Report and refuse unsupported persistence/transaction combinations rather than downgrade acknowledged durability silently.
- **FR-026**: Use the normal runtime persistence and delivery composition for standalone ingress and retained starts, with explicit provenance. Do not create fake publisher workflows, caller-fabricated saved-delivery context or a private API queue that bypasses runtime authority/recovery.
- **FR-027**: Show resource operational state separately from configuration ownership and compatibility findings. Show source acceptance, recipient dispatch, linked execution state, response delivery and failures separately, with useful safe diagnostics, timestamps and explicit retry/cancel effects.
- **FR-028**: Enforce tenant/access scope and normal permissions server-side on discovery, configuration, target selection, admission, operations and diagnostics. Invocation authority belongs to the shared resource and its scoped target grant, administered by authorized managers. Creator identity is audit attribution; creator departure or permission loss does not revoke the resource's authority. Fresh admissions check the current resource grant and ordinary target policy; explicit grant revocation stops future admissions without implicitly cancelling accepted work. Capture authorized server-owned execution identity/provenance without storing a user's bearer token for later background work.
- **FR-029**: Source-provided resources identify their source and read-only configuration. Their runtime pause/operate permissions are explicit; removing a source/module cannot leave a falsely active or editable resource.
- **FR-030**: Expose versioned backend capabilities and relations, normal Studio navigation/routes/components and graceful unavailable/denied/read-only states. Both association surfaces use the same persisted API and shared mapping editor.
- **FR-031**: Allow module-provided trigger descriptors, authoring validation and runtime source adapters with separate design/runtime concerns. Add public Studio customization only where generic descriptors cannot serve a qualified consumer; qualify public contracts with an independently built module.
- **FR-032**: Preserve Design-only, Runtime-only and combined deployment shapes. Runtime dispatch/recovery needs captured runtime material, never the workflow Design store or an open editor draft.
- **FR-033**: Provide practical operator/module documentation and examples for both HTTP modes, Event and schedule mapping, authority, missed firings and recovery limits. Coordinate shared contracts through #3583/#3590/#2892 without depending on completion of those whole products.
- **FR-034**: Complete real composed-host and Studio qualification, independent UX inspection, relevant behavioral/provider/module coverage, current-head review/checks/merge and resulting-main verification before reporting #3582 delivered. Keep failed, skipped, cancelled, unavailable and incomplete evidence distinct.

### Key Entities

- **Standalone trigger**: A live resource with stable identity, family/configuration, revision, ownership, tenant scope and independent operational lifecycle.
- **Workflow subscription**: The shared association from a trigger to a logical workflow's named serving slot, with mapping, revision, operational eligibility and input findings.
- **Trigger delivery**: An observed occurrence and its durable acceptance outcome, source/configuration identity, frozen recipient cohort and safe operator evidence.
- **Subscription delivery**: One captured target, validated input bag, retained authority, stable execution identity and independent dispatch/recovery/cancellation outcome.
- **Schedule occurrence**: One absolute due instant under a resource-owned schedule generation, with fenced ownership and documented advance policy.
- **Endpoint claim**: One normalized HTTP ingress owner, independent of the number of downstream workflow starts.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Create/reload and add/change/remove associations from both surfaces all preserve the same live state; the business graphs remain byte-for-byte unchanged by these operations.
- **SC-002**: One Event and one schedule each start at least two independently subscribed workflows, and one unchanged workflow starts from at least two standalone triggers with the intended declared inputs.
- **SC-003**: Real HTTP requests pass both explicit response modes, both configurable async admission policies and every specified invalid-input, unavailable-target, conflicting-route and failed-admission case without false acceptance or competing response writers.
- **SC-004**: Compatible publication replacement changes the next admitted target; incompatible replacement/unpublication blocks fresh starts visibly; accepted work continues on its captured material unless explicitly cancelled.
- **SC-005**: Duplicate delivery, partial dispatch, process restart and multi-worker tests produce exactly one intended execution per accepted subscription while recovering unfinished recipients independently. This is start convergence, not an exactly-once guarantee for arbitrary external business effects.
- **SC-006**: Pause/update/cancel races, due/missed schedule behavior, tenant/access checks and embedded start/wait regressions match the reviewed contract in the supported composed persistence topology.
- **SC-007**: Module consumer and native/direct-BPMN cases prove the capabilities claimed. Unsupported cases are refused visibly rather than silently adapted or counted as passed.
- **SC-008**: Current-commit downloadable Studio evidence and independent inspection cover the actual authoring/operations journeys, relevant empty/validation/recovery/permission states, keyboard/focus, themes and narrow layouts. Review and resulting-main qualification are complete.

## Assumptions

- This is one feature tracked by #3582 with program-goal state `none/free-flow`; it does not change the scope, sequencing or pauses of existing programs.
- [ADR 0043](../../docs/adr/0043-publication-slots-define-start-authority.md)'s accepted slot/start and Runtime-ledger ownership decisions remain authoritative. Its proposed October convergence amendment is not made accepted by this feature. A separately reviewed extension must state standalone ingress ownership and captured accepted-delivery authority explicitly.
- All four initial families are included. Initial source types consume Elsa's existing local HTTP/Event/scheduler facilities; a general broker, ordered fan-out orchestration, background jobs #3586 and durable messaging #3583 are outside scope.
- Routine lifecycle defaults proposed here are paused creation, impact-reviewed live revision changes, no automatic backlog after intentional pause, one catch-up after unplanned downtime and explicit HTTP route reservation during pause. They remain reviewable implementation contracts, not claims of already shipped behavior.
- Owner decision, 6 November 2026: asynchronous HTTP admission policy is chosen once per trigger, with Require all subscribers by default and Accept available subscribers returning explicit partial outcomes. This supersedes the earlier open reject-versus-partial question.
- Owner decision, 6 November 2026: invocation authority is owned by the shared resource and continues until paused or its resource grant is revoked. The creator remains in the audit history; continued creator membership/permission is not an admission condition.
- Owner decision, 6 November 2026: if no schedule subscriber can be accepted, keep one overdue occurrence until it can be admitted, without accumulating the missed interval backlog. A partially accepted firing keeps its original result/cohort.
- Ordinary permissions and source-owned settings patterns apply. No production deployment, package/software release publication, external service purchase or credential provisioning is authorized by this specification.
- [Research](research.md), [authority/input design](architecture.md) and [UX acceptance](ux-acceptance.md) distinguish checked source, confirmed owner decisions, proposed contracts and unverified behavior.
