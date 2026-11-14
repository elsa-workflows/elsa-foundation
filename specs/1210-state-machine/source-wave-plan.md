# Shared publication waves and native readiness

**Status:** proposed M0 architecture work unit, 13 November 2026. No implementation or product agreement.
Route: [StateMachine](../../docs/program-goals/state-machine.md), #3815/#3820, coordinated with shared-kernel
#4229. This advances Q2's common-publication fence rather than further local identity polish. The
[agreed input principles](m0-input-profile-proposal.md) and [selection proposal](contracts/selection.md) retain
their authority; the historical [fence proposal](m0-shared-fence-control-contract.md) is not an installed dependency.

Source basis: main `a756b579158dae8c534192b5dfa4bacd16baeb41`. Exact creation addressing remains a **draft
prerequisite**, [#4451](https://github.com/elsa-workflows/elsa-foundation/pull/4451), whose review-limit exception
is pending. This plan neither clears that gate nor adopts its deletion/naming/provider policy. Implementation
follows this plan's own review and applicable shared-owner agreement; only the plan is being prepared now.

## Question and observed gap

Can independently authored ordinary triggers collect ready completions from one actual publication, survive
delayed delivery and runnable continuations, and select against one configuration only after the wave settles?
A composite durably waiting for a later input must not prevent other currently ready reactions.

| Current prototype source | Observation, not qualification |
|---|---|
| `src/Kernel.Durable/Store.cs`, `Events.PublishAsync` | Writes an event, reads current subscriptions, then adds deliveries separately. A retry can observe changed membership. There is no immutable installation manifest or transitive closure fact. |
| `Store.cs`, `CatchUpAsync`; `Worker.cs`, `MeetAsync` | Kept-event catch-up can discover an event after registration. It needs a defined readiness wave rather than retroactively extending an earlier cohort. |
| `models/Native/NativeModel.cs`, `Resume`/`DrainAsync` | Agenda items survive Durable/budget yields, but carry no shared source responsibility. A committed yield is not a genuine external wait. |
| `NativeModel.cs`, `ExecuteAsync`/`ChildTrigger` | Local composite work, jobs and dispatched workflows cross different boundaries. Treating every scheduled boundary as settled would close a wave too early. |
| `models/Native/CompositeSignal.cs` | Deferral gates callbacks; it does not establish who else received the source or which continuation is still owed. |

The Statecharts spike and invocation-admission probe do not supply this boundary. Equal names/payloads,
timestamps, an empty mailbox and retained provenance cannot substitute for it. This unit adds no second chart
engine, actor per state, secondary semantic queue or independent workflow-row writer.

## Proposed ownership and protocol

Names below are logical records/operations, not CLR APIs or a product schema. Existing durable actor fences
and model checkpoints remain the workflow writers; installation belongs to the existing event storage family.

### Fixed source cohort

For feature-participating subscriptions, propose one source-store transaction that records an immutable
installation and enqueues its complete initial delivery set. Each member identifies the actual subscription
occurrence, exact actor creation and trusted consumer route. Capture an empty set explicitly. A duplicate
installation returns its original set; it never rescans to add a newer wait or replacement visit. Changed
contents for the same retained publication attempt conflict before any delivery is added.

Installation identity comes from the provider's non-reusing allocation domain, distinct from the caller's
event ID and semantic evaluation order. The in-memory experiment can use the store-owned monotonic sequence
domain already used for message rows. Durable allocation, backup/restore and namespace retirement remain
provider gates. Reusing an expired caller ID creates a new installation and cannot replace a manifest still
referenced by earlier work; any version/index stays transactionally within the event family.

Before the first install call, the publisher must durably record a stable publication-attempt key in its
existing source intent. A qualified native publication ticket's exact message ID and sender creation can
provide that identity; the current caller-controlled `EventRow.Id` alone cannot. An external ingress needs
an equivalent accepted durable intent before participating; that ingress remains unavailable until qualified.
The source transaction installs/compares `(attempt key, semantic envelope digest) -> installation` atomically.
Lost acknowledgement reuses that pre-existing key rather than requesting another allocated identity.
Keep the mapping while its manifest or promised receipt remains needed, even after caller-ID/payload expiry.
Explicit reuse of a caller ID has a different attempt key. An expired/unknown retry reports that condition;
it never resolves to the newest publication with that caller ID. Native lease/intent and ingress authorization
must be checked by the source owner; a sender payload cannot assert ownership of an earlier attempt.

The first cut installs all initial messages with the manifest, adding no new dispatch pump. Oversized cohorts
are refused atomically under an explicit experimental bound. Large fan-out/provider costs require shared-owner
design before adoption; this sets no product limit. Native publication tickets retire only after successful
installation. An uncertain commit retries/reads that same installation, not a freshly observed subscriber set.

A business payload cannot assert a manifest or consumer route. Runtime-owned registration/transport supplies
them and receivers verify the installed contents. Keep legacy publication behavior separate from opt-in grouping.
Proposed catch-up rule: later registration has a new readiness wave with the historical event as provenance;
an installed physical delivery retains its original assignment. Never reopen/extend a closed wave. Event
starts, registration races and waits installed across Durable yields need explicit compatibility qualification.

### Runnable responsibility

A manifest supplies initial obligations, not completion evidence. The receiver's normal model state records
the wave, ready occurrences and outstanding responsibility. Before arbitration it resolves the authoritative
manifest, including undelivered members. Unavailability remains unknown/owed, not an empty cohort permitting
parent fallback. Consumer routes follow actual trigger ownership and lexical frames, not sender payloads.
Dispatched workflows need checked propagation/return, not lookup of a parent's newest run.

| Boundary | Proposed treatment |
|---|---|
| Delivery accepted; native agenda work scheduled | Transfer responsibility to scheduled work in the same checkpoint. |
| Local child, composite callback, immediate continuation | Transfer responsibility; a new work ID/frame does not settle it. |
| Durable/budget yield or pre-effect attempt commit | Keep responsibility open across the persisted continuation. |
| Genuine Event/timer/external wait | Park that branch without a ready occurrence; later input supplies actual readiness. |
| Ready trigger completion | Record the exact occurrence and settle its runnable branch atomically. |
| Fault, poison or unknown delivery outcome | Retain a visible pending/recovery obligation until normal recovery, absorption, cancellation or terminal disposition settles it. |
| Stale visit/ended consumer | Record stale/ended disposition; never retarget a replacement. |

Replace consumed responsibility with children, ready outcome or declared suspension/terminal disposition in
one ordinary commit. Retries are idempotent; uncertain commits cannot lose responsibility. No independent
acknowledgement store or cross-execution row update participates. Returned settlement uses normal durable
messages whose consumption and local accounting share the receiving checkpoint.

If A advances a composite which waits for B, later completion belongs to B's readiness wave while output may
still contain A. This neither revives evaluated-unhandled A nor merges both inputs. Per-work cause metadata
is required when one burst processes different messages; a single ambient "last event" cannot establish it.

### Closure before selection

Close only after sealed membership is known and every relevant runnable obligation settles or genuinely
parks. Commit closure and one owed evaluation wake together. A crash before wake delivery cannot lose
arbitration; repeated closure/settlement cannot schedule it twice. The selector retains child precedence,
compatible regional selection and authored order.

A ready parent cannot outrun an undelivered child of the same wave. If that child instead reaches a real wait
for B, it is not ready for A. If the visit survives and B later completes it with output A, that is new readiness.
Automatic/completion priority, business FIFO, selected-phase barriers and ordinary recovery remain as agreed.
Installation sequence adds no priority. Record engine ordering between distinct eligible waves and business
inputs; introduce neither wall-clock ordering nor an implicit wait-for-all-composites barrier.

## Decisions required before executable adoption

The shared execution-model owner reviews source installation/identity/retention and transitive propagation
before code. Native placement and product agreement remain separate. Required boundaries include:

- Genuine suspension for background jobs and workflow/model calls. Queued child starts/return notifications
  cannot disappear from accounting. Explicit background placement may be a suspension boundary; ordinary
  scheduling yields and synchronous completions cannot silently change that classification.
- Cross-actor consumer routing, registration and settlement before arbitration. A fixed list alone does not
  prove that every ready notification reached the chart.
- Kept-event catch-up, event/HTTP starts, mixed participants and publication-ID reuse. Unsupported participants
  and providers refuse participation before accepting the guarantee.
- Capacity, retention roots and cleanup. Pending/recovery responsibility has no invented timeout; event payload
  expiry cannot delete a still-needed manifest. Value/secret retention stays with its owners. No unbounded
  product retention policy/default is selected.
- Guard-fault recovery and normal absorption without unknown work becoming false eligibility. Continued
  bounded business admission cannot execute a guard or unblock authored phase work.

These are scope gates. A local-only witness cannot close arbitrary composite, child/model-call or provider proof.

## Bounded implementation and evidence order

**A — source installation.** After plan/shared-owner review, use actual in-memory source/store operations in
a non-packable runner. Prove fixed membership, duplicate/conflict, empty cohorts, later registration/replacement
visits, caught-up registration, before-apply/lost-ack installation and atomic bound refusal. Enable no chart
selection here. Other providers remain unavailable rather than dropping fields.
Use normally committed publisher intents/subscriptions, not seeded successful manifests. Also lose an install
acknowledgement, expire/reuse the caller ID with a new attempt, then replay the original ticket: it must recover
the original installation/cohort. Forged, unknown and expired attempt references cannot adopt the new one.

**B — native readiness.** A generic composite fixture composes ordinary Event, Sequence, Parallel, waits,
yields, faults and callbacks across Balanced/Durable and root/nested use. Opposite callback orders produce
the same ready set from one closed wave. Delayed recipients, pending agenda work, undelivered child/return
messages and recovery blockage prevent early closure. Qualify A-then-B with retained A data, normal retries,
cancellation and stale-visit rejection. Do not add a second StateMachine interpreter.
Two normal-native topologies are mandatory: independently authored triggers in separate parallel regions,
and independent child-owned/parent-owned triggers. Each receives one actual publication under one installed
cohort. Reverse and delay the relevant deliveries and continuations; require the same exact closed-wave
occurrence accounting, with no ancestor fallback while the child's runnable responsibility remains owed.

**C — transitive/chart integration.** Extend through normal workflow/model calls, background placement and
catch-up/start ingress. Only after those boundaries are proved connect the reviewed selector, normal guard
recovery and phase work. Reuse admission/terminal/identity probes without counting their inventories as this
proof. Full product/Studio/native-compensation qualification remains the delivery goal.
Selector integration must execute both topologies: compatible regional reactions are selected together and
an eligible child suppresses its parent. These are actual integration paths, not only pure-selector tests.

Each stage gets a discovered inventory and isolated named controls: rescan on retry, omit a cohort member,
close on mailbox emptiness/yield, lose a transfer, classify retained A as current readiness, accept a forged
route, publish closure/wake before commit, and ignore stale creation/visit identity. A control must fail its
named assertion, not merely throw or time out. Keep all failure/unrun history and source/assembly hashes.

Bound focused builds to 600 seconds, deterministic runners to 60 seconds and affected sweeps to 180 seconds, through
the build-slot wrapper with one processor. No service/cloud/old Azure grant, whole-solution build or additional
infrastructure is implicit. Independent source and raw-evidence acceptance precede dependent integration.

## Deliverables and completion boundary

This is architecture development in the existing Active StateMachine bucket. Current changes are spec/work-order
links only: check links/scope/whitespace, claim no executable result. Ratified runtime/design separation and
ADR 0080 remain gates; provisional Model X/§E2.9 are not approval. This installs no glossary meaning, public API,
extension point or solution filter. Actual code later carries the corresponding normal catalogs/maps/gates.

<proposed_plan>
Create specs/1210-state-machine/source-wave-plan.md and link delivery.md, README.md and the existing StateMachine program goal.
After separate plan/shared-owner gates, refine source-install/native-readiness prototype tasks against current main; reserve no product API/schema here.
Code follow-through covers generic source/store/native seams, positive/negative runners and affected admission/terminal/identity/Statechart/lease/compatibility regressions.
Refresh the actual M0 plan/contracts/evidence from observed results and obtain applicable agreement before production StateMachine/Studio. Keep #4451's review gate intact.
</proposed_plan>
