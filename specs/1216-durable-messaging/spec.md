# Feature Specification: Durable Messaging

**Feature Branch**: `agent/codex/3583-durable-messaging`
**Created**: 2026-11-05
**Status**: In progress — owner scope/product decisions and reviewed plan authorize independent submission-rule implementation; T003 shared ingress mapping precedes dependent ABI changes. No producer or durable capability is implemented yet.
**Input**: Deliver [#3583](https://github.com/elsa-workflows/elsa-foundation/issues/3583) end to end under the [5 November owner authorization](https://github.com/elsa-workflows/elsa-foundation/issues/3583#issuecomment-6671359620).
**Program goal**: [Durable Messaging](../../docs/program-goals/durable-messaging.md). #3583 remains the canonical feature and delivery record.

## Decision status

The owner-selected requirements are carried forward without reopening them:

1. Correlate a named business message using a business key.
2. Deliver each accepted message to at most one recipient per logical workflow definition across all versions and instances. Different eligible definitions receive it independently. Events remain broadcast.
3. Buffer by default with configurable retention and sender overrides, including immediate-only delivery.
4. Include published message starts and waits in running workflows in the first release. A message cannot both start and satisfy a wait within one logical definition.
5. Allow at most one active **message-started** run per tenant, logical definition and correlation key across versions. Further messages can satisfy waits or remain buffered; an undelivered, unexpired message can start a later run after the active one ends. Other start mechanisms are not restricted by this rule.
6. Use a separately configurable producer-ID deduplication window, including after delivery or recipient completion. A duplicate preserves the original message's outstanding deliveries.
7. Support application submission, authored workflow send/receive, and operator inspection of overall and per-definition outcomes with authorized payload access.
8. On 5 November, the owner selected durable acceptance receipts with delivery status separately. Immediate-only unmatched outcomes are explicit; lost responses are uncertain and reconciled by retrying the same producer ID.
9. On 5 November, the owner excluded brand-new logical definitions from buffered history. Only definitions eligible at acceptance may receive it; their later waits and eligible replacement versions remain possible until expiry.
10. On 5 November, the owner selected ready waits before starts within each definition. Start a new run only when no matching wait is ready and message-started admission for the key is free.
11. On 6 November, the owner selected cancellation of an unaccepted workflow send when cancellation wins the durable race. Already accepted messages remain committed; cancelling the sender cannot recall them.

[Research](research.md), [draft delivery design](contracts/delivery-design.md) and [acceptance matrix](acceptance-matrix.md) distinguish observations, engineering proposals and not-run qualification. Owner product decisions do not establish implemented runtime behavior or adopt a persisted ABI.

## User Scenarios & Testing

### User Story 1 — Keep an early business message (Priority: P1)

An application sends `PaymentReceived` for `Order 123` before Order Fulfillment reaches its receive step. Elsa retains it for a matching recipient within its lifetime.

**Why this priority**: Early callbacks must not disappear because a wait is not open yet.
**Independent Test**: Submit through the real API, restart the owned host, open a matching wait and inspect its exact receipt/recipient.

**Acceptance Scenarios**:

1. **Given** an eligible published receive-only definition with no ready bookmark or message-start subscription, **when** a wait opens before message expiry, including after host restart, **then** it receives the retained payload.
2. **Given** waits for Orders 123 and 456, **when** a message for Order 123 arrives, **then** no Order 456 wait receives it.
3. **Given** default and overridden lifetimes, **when** admission and expiry occur, **then** effective deadlines and outcomes match the documented configuration and receipt.
4. **Given** immediate-only delivery without an eligible recipient, **when** durable admission-time selection finishes, **then** its receipt reports an explicit unmatched outcome and later subscriptions cannot receive it, including after queue delay or restart.
5. **Given** a delivery already selected before expiry, **when** buffering expires during recovery, **then** its committed intended delivery remains recoverable rather than permitting reselection.

### User Story 2 — Deliver once within each definition (Priority: P1)

Order Fulfillment and Accounting can each receive one payment. Competing versions or instances of Order Fulfillment must not each consume it.

**Why this priority**: Independent consumers must coexist with one-recipient consumption within a definition.
**Independent Test**: Race submission/selection across workers, versions and instances, and inspect durable per-definition outcomes.

**Acceptance Scenarios**:

1. **Given** two eligible matching definitions, **when** one message is accepted, **then** each can receive it independently.
2. **Given** matching waits and starts across versions/instances of one definition, **when** selection occurs, **then** at most one start-or-wait recipient is chosen there.
3. **Given** one definition delivered and another pending, **when** the latter becomes ready within lifetime and eligibility policy, **then** the first delivery does not erase its opportunity.
4. **Given** a selected recipient and lost acknowledgement, **when** recovery retries, **then** it completes the same intended delivery without choosing another recipient in that definition.
5. **Given** an equivalent Event with several matching listeners in one definition, **when** routed, **then** all eligible listeners retain broadcast delivery.
6. **Given** two distinct buffered messages racing for one one-shot wait, **when** one reserves it, **then** the other stays unselected/pending and can receive a later eligible wait within its lifetime. Immediate-only instead completes its bounded admission selection and reports unmatched when no eligible candidate remains.
7. **Given** a brand-new logical definition first becomes eligible after acceptance, **when** it opens a matching wait or start, **then** it cannot consume that historical message. Eligible replacement versions of an existing acceptance-time definition remain possible.

### User Story 3 — Start and continue keyed conversations (Priority: P1)

A payment can start a published workflow. Later messages can satisfy its waits or stay buffered until the active message-started run ends.

**Why this priority**: Starts and waits are selected first-release scope, including across workers and publication versions.
**Independent Test**: Race same-key starts across workers/versions, deliver a follow-up wait message, terminate the active run and observe a pending message start its successor.

**Acceptance Scenarios**:

1. **Given** no active message-started run in scope, **when** concurrent messages compete, **then** at most one message-started run becomes active for that tenant/definition/key.
2. **Given** an active message-started run, **when** another message arrives, **then** it may satisfy an eligible wait or remain pending, but cannot start a concurrent message-started run in scope.
3. **Given** different keys, tenants or definitions, **when** messages arrive, **then** admission for one does not lock the others.
4. **Given** authoritative termination and a pending eligible unexpired message, **when** reconciliation runs, **then** that message may start the next eligible published run.
5. **Given** a message already delivered for the definition, **when** its run ends or publication changes, **then** the message cannot start another run there.
6. **Given** another start mechanism, **when** it starts a run, **then** this feature imposes no general same-key uniqueness restriction on that mechanism.

### User Story 4 — Retry a submission safely (Priority: P1)

A sender that loses a response retries with the same producer ID and learns the original acceptance and outstanding outcomes.

**Why this priority**: Delivery completion must not end producer deduplication prematurely.
**Independent Test**: Lose responses, repeat IDs before/after delivery and run completion, then advance the independent clocks.

**Acceptance Scenarios**:

1. **Given** an accepted ID inside its deduplication window, **when** identical content repeats, **then** it resolves to the original acceptance and preserves pending per-definition deliveries.
2. **Given** delivery or recipient completion, **when** that ID repeats inside its window, **then** no new accepted message or run is admitted for the duplicate.
3. **Given** buffer expiry before deduplication expiry, **when** the ID repeats, **then** expiry does not turn the duplicate into a fresh message.
4. **Given** deduplication expiry before buffer expiry, **when** identity cleanup runs, **then** it preserves the original pending delivery; the documented reuse rule governs later submissions.
5. **Given** the same ID with changed content, **when** submitted, **then** the explicit conflict rule applies without mutating original content or progress.

### User Story 5 — Author and operate Messages (Priority: P1)

An author configures message sends, receives and starts. An operator finds a receipt, diagnoses pending/terminal delivery by definition and uses authorized recovery.

**Why this priority**: API, authoring and operations belong to delivery rather than follow-up products.
**Independent Test**: Use composed Studio/Workbench to author, publish, submit through API/activity, receive and inspect outcomes/recovery.

**Acceptance Scenarios**:

1. **Given** authored message activities, **when** saved/reopened and run, **then** their name, key, payload, lifetime and producer-ID behavior matches application submission.
2. **Given** native and supported genuine-model workflows, **when** they start/catch messages, **then** their owning execution, publication and terminal contracts govern delivery.
3. **Given** partial multi-definition delivery, **when** an operator inspects it, **then** overall status and each definition's pending/delivered/expired/failed outcome remain distinct.
4. **Given** a role without payload access or a foreign tenant, **when** it queries or attempts recovery, **then** no unauthorized payload, key, secret value or foreign identity is disclosed.
5. **Given** a failed selected recipient, **when** an authorized recovery action runs, **then** it preserves the selected-recipient invariant and produces an auditable result.

### Edge Cases

- Subscription opening/cancellation/replacement/expiry races; several waits, bindings or publication slots per definition.
- Version replacement before selection versus after a start is durably selected.
- Native completion/fault/cancellation versus model incident and pending terminal settlement.
- Blank/unkeyed keys, Unicode/case/size bounds, different names with the same key.
- Producer ID reuse with changed payload/name/key/lifetime or another producer; no producer ID after a lost response.
- Definitions introduced after acceptance; waits and eligible versions opening later.
- Temporary unavailability, permanent payload error and operator recovery after ambiguous completion.
- Independent buffer, dedup, receipt/payload audit and execution/material retention.
- Crash before/after acceptance, selection, execution admission and midway through multi-definition delivery.
- Disabled module, unsupported persistence, runtime-only deployment and capacity/backpressure rejection.

## Requirements

### Functional Requirements

- **FR-001**: Match message name and business key within authorized tenant/persistence scope.
- **FR-002**: Use stable owning definition namespace/ID across versions/instances. Authored native/BPMN workflows share the canonical model-neutral `WorkflowDefinitionId`; protocol/model family, artifact hash, synthetic model pin, version and subscription MUST NOT split one logical definition into extra recipients or admission keys. Standalone model definitions need a genuine owner-provided stable namespace/ID.
- **FR-003**: Derive tenant, producer and execution authority from authenticated/owning context, never trusted payload claims.
- **FR-004**: Persist at most one start-or-wait recipient per accepted message and eligible logical definition across all versions/instances.
- **FR-005**: Preserve independent delivery outcomes/opportunities for separate eligible definitions under the historical eligibility policy.
- **FR-006**: Preserve Event/signal broadcast and existing correlation semantics; MUST NOT globally narrow Event routing to implement Messages.
- **FR-007**: Buffer by default with documented configurable lifetime and sender overrides including immediate-only delivery.
- **FR-008**: Success MUST acknowledge durable acceptance with a stable receipt; report delivery separately and explicit unmatched immediate-only outcomes. Lost responses are uncertain; same-ID retry resolves the original acceptance.
- **FR-009**: Acknowledged durable acceptance and selected delivery MUST survive the restart/worker failures claimed by the configured persistence mode.
- **FR-010**: Buffer expiry MUST prevent new recipient selection, without erasing committed intended delivery or permitting reselection after uncertainty. Immediate-only MUST perform its special candidate capture/reservation at admission; zero duration MUST NOT prevent that attempt or allow later selection.
- **FR-011**: Support published starts and existing waits in the first release under existing publication/execution authorities.
- **FR-012**: A ready matching wait MUST take precedence over a published start in its logical definition. Enforce deterministic competing-wait/message ordering with a stable tie-breaker atomically across workers. Exclusive one-shot subscription reservation MUST prevent two accepted messages selecting the same generation; a losing buffered message remains unselected/pending, while immediate-only completes its bounded selection with explicit unmatched outcomes if no candidate remains.
- **FR-013**: Admit at most one active message-started run per tenant/logical definition/key across versions; leave other start mechanisms and independent scopes unaffected.
- **FR-014**: Release message-start admission only using authoritative owning-execution lifecycle evidence, never worker lease expiry, unavailable providers or lost notifications.
- **FR-015**: Reconsider eligible pending messages after release within lifetime; never reconsider a message already delivered for that definition.
- **FR-016**: Document/test blank keys, terminal faults, recoverable incidents, cancellation and model settlement before implementation.
- **FR-017**: Give optional producer IDs a documented namespace and independent configurable deduplication window continuing after delivery/completion.
- **FR-018**: Duplicate acknowledgement MUST identify original acceptance and preserve outstanding deliveries; duplicates MUST NOT implicitly extend either lifetime.
- **FR-019**: Define equality, changed-content conflicts, clock origin/bounds and ID reuse after dedup expiry explicitly.
- **FR-020**: Keep buffer/dedup cleanup independent; retain all proofs needed for selected delivery, active admission, recovery and configured audit retention.
- **FR-021**: Persist the exact recipient before execution effects; retries/crashes MUST complete it or report its authorized terminal failure without selecting a second recipient there.
- **FR-021a**: Target admission/consumption MUST validate the selected Message reservation owner/fence. Releasing a still-live subscription after failed/abandoned delivery MUST durably close its old target capability before another message can reserve it; a delayed old resume MUST fail even if the physical bookmark generation remains unchanged. Ambiguous consumption MUST reconcile before release.
- **FR-022**: Recover per-definition partial progress independently, including successful and unavailable recipients in one message.
- **FR-023**: Expose payload incompatibility/permanent failure without silently skipping to another recipient after selection.
- **FR-024**: Freeze eligible logical definitions at acceptance; exclude brand-new definitions from historical messages. Support late waits and eligible replacement versions in that cohort through Runtime-owned publication/active-material interest declarations, including receive-only definitions without an open bookmark or start. Define dynamic-name declarations, retirement and publication changes without querying Design data for execution authority.
- **FR-025**: Recover lost wakeups using existing Runtime scheduling/delivery ownership; MUST NOT create a competing broker, scheduler, execution store or message authority.
- **FR-026**: API and authored activities MUST share semantic/access contracts; authored sends MUST respect the committed-effect boundary.
- **FR-026a**: Workflow cancellation that wins before durable acceptance MUST stop the pending send and fence stale claims. Acceptance that wins first MUST preserve the message and every recipient obligation. The actual source/checkpoint transaction MUST determine and retain the result; cancellation requests or missing sender state are insufficient proof.
- **FR-027**: Expose overall/per-definition pending, selected/in-progress, delivered, expired and failed outcomes, effective lifetimes and sanitized diagnostics. Recovery actions MUST be separately authorized/audited.
- **FR-028**: Authorize payload access separately. Lists/logs/errors MUST exclude protected payloads/resolved secret values; cross-tenant probes MUST fail without disclosure.
- **FR-029**: Document defaults/bounds, capacity/backpressure, retry/failure and retention. Diagnose unsupported durable/provider compositions before advertising durable capability.
- **FR-030**: Supply runnable API/receipt/retry, authored send/receive and configuration examples with explicit guarantees/limits and no exactly-once external-effect promise.
- **FR-031**: Native/BPMN/participating genuine-model adapters MUST preserve model-owned starts, exact subscriptions, compatibility and terminal/recovery contracts. Unsupported shapes MUST be explicit rather than silently treated as Events.
- **FR-032**: Qualify every claimed provider/multi-node mode with composed adverse-path evidence; mocks/fixtures alone MUST NOT qualify production modes.
- **FR-033**: Studio journeys MUST meet [Studio UX acceptance](../../docs/contributing/studio-ux-acceptance.md), including current-commit downloadable composed evidence and independent inspection.
- **FR-034**: Producer and target capabilities MUST enforce actual source/row/wire/participant versions before effects. Protect Message source outcomes from unknown-kind claims/field loss and target conditions from old decoders; preserve legacy Event writes during dormant rollout. State supported homogeneous/quiesced versus qualified rolling-fleet modes explicitly rather than assuming metadata is a physical writer fence.

### Key Entities

Use the [Elsa glossary](../../docs/glossary/elsa.md) for existing execution/publication/bookmark/authority meanings. Proposed Message records belong to the draft design until reviewed.

- **Accepted message / receipt**: Immutable business content, authorized scope, effective lifetimes and stable acceptance/progress identity.
- **Definition delivery**: A pending opportunity or exact selected recipient and outcome for one accepted message/definition.
- **Message-start admission**: Active message-started conversation for tenant/definition/key, separate from receipt and deduplication.
- **Deduplication identity**: Bounded producer-ID recognition independent of remaining buffer or recipient lifecycle.

## Success Criteria

### Measurable Outcomes

- **SC-001**: Real early-arrival/restart journeys deliver to the later matching wait and never a different key.
- **SC-002**: Default/override/immediate-only/expiry/unmatched journeys produce documented deadlines and acknowledgements.
- **SC-003**: Worker/version/instance races yield at most one start-or-wait recipient per definition and independent outcomes across definitions.
- **SC-004**: Same-key races yield at most one active message-started run; independent scopes proceed; authoritative termination releases an eligible pending successor.
- **SC-005**: Duplicates before/after delivery/completion admit no fresh message inside the independent window; changed-content and independent-expiry cases follow contract.
- **SC-006**: Crashes/retries at acceptance/selection/admission/partial-fanout boundaries recover original recipients without second selection.
- **SC-007**: Unavailability, payload failure, permanent failure, authorized recovery and backpressure have observable outcomes without protected-data disclosure.
- **SC-008**: API/activity/native/supported genuine-model journeys share semantics; Event broadcast regressions continue passing.
- **SC-009**: Every claimed provider/multi-node mode has executed evidence, and agreed Studio states have current downloadable independently inspected evidence. Skipped/unavailable/incomplete cases do not count as passing.
- **SC-010**: Current-head review/checks and resulting-main qualification pass; canonical issue/program/spec/configuration/examples agree before closeout.

## Product Decisions Recorded 5–6 November

The owner answered each question separately in this chat. Record links on #3583 are added with the decision receipt; none reopens the earlier selections.

- **Q1 — Acceptance, accepted**: Durable receipt; separate delivery status. Immediate-only unmatched is explicit. Timeout is uncertain and the sender retries the same ID.
- **Q2 — Historical eligibility, accepted**: Existing eligible definitions only. Late waits and eligible replacement versions may receive while buffered; new logical definitions do not consume history. An open-bookmark snapshot alone is insufficient.
- **Q3 — Arbitration, accepted**: Ready wait first. Consider a published start only with no ready matching wait and free message-start admission. Deterministic competing-wait/message ordering is an engineering requirement for review.
- **Q4 — Sender cancellation, accepted 6 November**: Stop unaccepted sends when cancellation wins; keep accepted messages. The source/actor transaction determines the winner and preserves an immutable outcome. A lost response remains uncertain, and cancellation cannot recall accepted deliveries.

Remaining engineering review covers physical atomicity/capabilities, Runtime-owned interest declarations and dynamic names, producer namespace mapping, configuration defaults/bounds/quotas, precise model mappings and failure/recovery policy. These proposals must be concrete before approval; they are not additional unanswered product cards by default.

## Assumptions and Boundaries

- Owner delivery authorization and product answers do not adopt an unreviewed shared ABI.
- Native and genuine BPMN integration are in scope; complete standalone-trigger #3582 and background-job #3586 products remain separate.
- Preserve Runtime-only, Design-only and combined deployment shapes; runtime delivery cannot query drafts/source definitions for execution authority.
- The Runtime-owned standalone ingress family discussed in [#2892](https://github.com/elsa-workflows/elsa-foundation/issues/2892#issuecomment-6670940060) is proposed, not landed or adopted for producers. Coordinate before implementing that shared source boundary.
- Blank-key handling, dedup namespace/conflicts, numeric configuration bounds and recovery are explicit draft design proposals, not silently inherited Event rules.
- No exactly-once external effects, release, deployment, workflow-permission change or external service/secret provisioning is promised.

## Constitution Alignment and Review

Apply both [framework](../../.specify/memory/constitution-framework.md) and [Elsa](../../.specify/memory/constitution.md) constitutions and the [review checklist](../../docs/contributing/review-checklist.md). Check domain-owned Core contracts, optional providers, scoped services, extension points versus internals, existing test objectives, actual extension catalogs, Runtime/Design separation, artifact-only execution and provider-backed risk tests/CI tiers.

Framework §2.24 and Elsa §E2.8 versioning/Model X and §E2.9 are draft/provisional. This work does not ratify them. Changes to initiative-owned contracts require their agreement path; #3583 currently has no matching #3052 board topic or parent link.

[Planning quality review](checklists/requirements.md) is complete for the independent submission-rule unit. [Tasks](tasks.md) retain the shared-source mapping prerequisite before dependent ABI/store changes and every product/provider/UX qualification still outstanding.
