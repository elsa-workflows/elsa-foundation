# Feature Specification: Tolerate Concurrent Post-Commit Outbox Delivery Contention

**Feature Branch**: `1305-outbox-delivery-contention`

**Created**: 2026-09-17

**Status**: Draft

**Input**: GitHub issue [#1798](https://github.com/elsa-workflows/elsa-foundation/issues/1798), plus a correction posted by the reporter (see *Reported Behaviour*), plus six design decisions settled with the architect before drafting (see *Settled Decisions*).

---

## Problem Statement

Starting a workflow intermittently answers HTTP 500. No instance is created, so the caller's follow-up read finds nothing.

The runtime delivers post-commit outbox items by two paths that are both live at the same time:

- A **live drain** path, which skips the durable claim round-trip for the execution it is draining. It reads the deliverable items and records each one as delivered *without* presenting an owner or fencing token.
- A **durable claim** path, used by the background resumption sweep, which claims each item under an owner and a fencing token before delivering it.

When the sweep claims an item that a live drain is already delivering, the live drain's claim-less recording is rejected: the item now carries an owner and a fence, and the recording contract has no way to present either. The rejection is raised as an error, escapes the drain orchestrator, and becomes the 500 the caller sees.

The same defect previously existed in the Groundwork store and produced the same intermittent start-path 500. Removing Groundwork ([PR #1764](https://github.com/elsa-workflows/elsa-foundation/pull/1764)) was expected to remove it and did not, because the defect is in the **contract**, not in any one store: the delivery-record contract cannot express "another deliverer owns this item", so every store that implements it reproduces the same rejection.

### Why the obvious fix is not the fix

The most-cited candidate — *make the deliverable candidate selection skip items that are already being delivered* — is **already implemented**. The candidate query treats an in-delivery item as having no deliverable time and therefore already excludes it.

It fails anyway, because the sweep's claim lands **between** the live drain's read and its record. Narrowing the read narrows the window; it cannot close it. Any fix built only on the read will appear to work and will regress.

This must be stated in the spec so the same candidate is not "fixed" a third time.

### A second, independent trigger with no race in it

The recording contract rejects an item when it is in delivery **or** when it carries a non-zero fencing token. The fencing token is never reset: each claim increments it, and an expired or failed claim returns the item to a deliverable state with the incremented token still on it.

So any item that was **ever** claimed becomes deliverable again while permanently carrying a fence — and the next claim-less delivery of it is rejected **deterministically, on the first attempt, with no concurrency involved**. This is a distinct defect from the race above and is fixed in the same unit of work.

---

## Reported Behaviour

Measured on image `sha-71852a1` (`71852a10e25062299264a73ea5cb571c50a404ce`), runtime persistence on PostgreSQL 17, Groundwork not present.

The issue as originally filed stated that ten concurrent starts were required and that single starts stayed clean. **The reporter has since corrected the second half**, in this session and as a comment on the issue:

| Observed suite            | Starts | Symptom |
|---------------------------|--------|---------|
| `10 ten instances at once` | 10     | The 8th start answers 500; 8 instances created; the loop stops there, so the poll for ten times out at its 90-second budget |
| `01 sequence order`        | 1      | The single start answers 500; no instance created; the settled read matches nothing |

The shared condition is **concurrency of outbox delivery, not concurrency of starts**. Ten starts provoke it reliably because they generate a large volume of outbox traffic, but ordinary background drain pressure is sufficient: in the single-start failure the engine had already served seven earlier suites, including a recurring timer firing every five seconds.

Frequency on `sha-71852a1`: **3 of 4 full or partial catalog runs**, landing in a **different suite each time**. It lands wherever a start meets a drain, which is why no single suite owns it, and why a few clean runs were misread as a fix.

This correction is load-bearing for the acceptance gate: a test that starts N workflows concurrently and asserts N instances covers the ten-start case and **would miss the single-start case entirely**.

---

## Settled Decisions

These were settled with the architect before drafting. They are inputs to this spec, not open questions. Planning must not re-open them.

| # | Decision | Rationale |
|---|----------|-----------|
| **D1** | The claim-less delivery path **tolerates losing** the race. Contention is legitimate. | The live drain's ownership is ambient in-process state; the sweep may run in a different process entirely. Tolerance is the only resolution that holds under multi-instance hosting. |
| **D2** | Tolerance is expressed as a **distinct, non-throwing outcome** returned to the caller — neither a silent no-op nor a caught exception. | A silent no-op corrupts the drain orchestrator's loop-continuation signal, which keys off the delivered count. A caught exception leaves the *contract* unable to express ownership — the exact failure mode that caused this defect to be reimplemented. |
| **D3** | Accept a **breaking change** to the delivery-record contract so it can return that outcome. Do **not** add another optional capability interface. | This contract family already accreted four additive interfaces and still arrived at a store that cannot say "not mine". An optional interface can be omitted, and an omitting store reproduces the defect. A required signature change makes every implementer confront the question at compile time. |
| **D4** | Also fix the deterministic fence trigger, by excluding items carrying a fencing token from the deliverable candidate selection. Do **not** implement owner filtering — **remove the dead owner filter** from the query instead. | No caller sets the owner filter, and every surviving store rejects it with an identical refusal message. The dead parameter *is* the vector: it looks like the ownership hook, so each new store dutifully reimplements the refusal rather than solving the problem. Audited: **three** test sites reference it, not one. |
| **D5** | The acceptance gate is a **contended single delivery**, not N concurrent starts. Every new test must be proven to go red when the production change is reverted. | Per the reporter's correction, the single-start case is the one that reproduces most often, and an N-start test is blind to it. The revert-to-red step is mandatory: four consecutive fixes on the reporter's side had first-draft tests that passed without the production change. |
| **D6** | The duplicate-key errors logged in the same run are **out of scope**, recorded as a documented non-defect. | The scheduler work-queue store already catches that collision, identifies it, re-reads the winner and returns it. The database driver logs the raw error before that handling runs, which is exactly the observed "logged, but no 500" signature. |

### Rejected alternative (recorded, not revisited)

**Make the background sweep exclude executions that are being live-drained.** Rejected under D1: drain liveness is in-process ambient state and the sweep may be a different process, so this would require a durable liveness lease that does not exist. Adding one is a substantially larger change that would still leave the contract unable to express ownership.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A start succeeds while background delivery is in flight (Priority: P1)

An operator starts a workflow through the runtime API while the engine is busy: a recurring timer is firing, other executions are draining, and the background resumption sweep is running on its timer. The start succeeds and an instance is created, regardless of whether the sweep happens to take over delivery of any item belonging to this start.

**Why this priority**: This is the reported defect and the only story that removes the 500. Everything else in this spec is either a second trigger for the same symptom or the evidence that the fix holds.

**Independent Test**: Deliver one outbox item through the claim-less path while another deliverer holds a claim on that same item, and assert the delivery completes without raising. Fully testable at the delivery-processor level with an injected steal; no host, no database, no timing budget.

**Acceptance Scenarios**:

1. **Given** an outbox item that a live drain has read as deliverable, **When** another deliverer claims that item before the live drain records its result, **Then** the live drain's recording completes without raising, and the caller's start answers success rather than 500.
2. **Given** that same contended delivery, **When** the live drain records its result, **Then** the outcome reported to the caller distinguishes "superseded by another owner" from "delivered", so the drain's continuation signal is not inflated by an item this deliverer did not actually deliver.
3. **Given** an item superseded in this way, **When** the owning deliverer completes it, **Then** the item reaches its terminal state exactly once and the work it represents is performed exactly once.

---

### User Story 2 - A previously-claimed item can be delivered again (Priority: P1)

An outbox item was claimed by a deliverer at some earlier point and returned to a deliverable state — because the claim expired, or the delivery attempt failed retryably. A later claim-less delivery of that item succeeds.

**Why this priority**: Also P1 — this is a **deterministic** failure, not a race. It needs no concurrency at all, and it will fail on the first attempt every time the situation arises. It is a separate defect reaching the same 500.

**Independent Test**: Claim an item, return it to a deliverable state, then deliver it via the claim-less path and assert it succeeds. Entirely sequential and deterministic.

**Acceptance Scenarios**:

1. **Given** an item that was claimed and then returned to a pending or retryable state, **When** the deliverable candidate selection runs, **Then** that item is not offered to the claim-less path while it still carries a fencing token.
2. **Given** an item that carries a fencing token from an earlier claim, **When** a claim-less delivery records a result for it, **Then** the result is reported as superseded rather than raising.

---

### User Story 3 - A store cannot silently omit ownership reporting (Priority: P2)

A team implementing a new persistence provider for the outbox cannot produce a store that compiles and passes while being unable to report that an item belongs to another deliverer.

**Why this priority**: P2 — it does not fix the running system, but it is the reason this defect survived a store migration. Without it, the next provider reintroduces the same 500 and the same investigation is repeated a third time.

**Independent Test**: Confirm the contract's shape forces the question — a store that does not account for the superseded outcome fails to build, rather than failing at runtime under load.

**Acceptance Scenarios**:

1. **Given** the revised delivery-record contract, **When** a provider implements it without handling the superseded case, **Then** the omission surfaces at build time rather than as an intermittent production 500.
2. **Given** the revised query contract, **When** a provider implements it, **Then** there is no dead ownership-filter parameter present to be refused, so no provider is invited to reimplement the refusal.

---

### Edge Cases

- **The superseded item is never completed by its owner** — the owning deliverer crashes mid-delivery. The durable item must remain a crash backstop: the claim expires, the item becomes claimable again, and the resumption sweep redelivers it. Tolerating supersession must not weaken this.
- **Both deliverers succeed in dispatching** — the work is enqueued twice. The enqueue is idempotent on a deterministic identity, so the second enqueue resolves to the existing work item and the work is performed once. This is the mechanism behind the out-of-scope duplicate-key log noise in D6.
- **Every item in a drain cycle is superseded** — the drain's delivered count is zero, so the drain treats the cycle as quiesced and stops. This is correct: the items are being delivered by their owner, not lost. The drain must not treat this as a delivery failure.
- **An item is superseded on a failure recording** — the live drain's dispatch failed *and* the item has been taken over. The failure must not be recorded over the new owner's state; the outcome is reported as superseded and the owner's completion governs.
- **A retryable failure exhausts its retry budget while superseded** — retry accounting belongs to whichever deliverer actually completes the item; a superseded recording must not consume an attempt.

---

## Requirements *(mandatory)*

### Functional Requirements

**Tolerating contention (D1, D2)**

- **FR-001**: The delivery-record operation MUST report a distinct, non-error outcome when the item it is recording is owned by another deliverer, instead of raising.
- **FR-002**: That outcome MUST be distinguishable by the caller from a successful delivery and from a delivery failure.
- **FR-003**: A superseded recording MUST NOT modify the item's delivery state, its owner, its fencing token, or its attempt count.
- **FR-004**: A superseded recording MUST NOT count toward the delivered count that the drain orchestrator uses as its loop-continuation signal.
- **FR-005**: A superseded recording MUST NOT count as a delivery failure, and MUST NOT cause the drain to report a delivery-failed stop reason.
- **FR-006**: A superseded item MUST remain a crash backstop: it stays recoverable by claim expiry and the resumption sweep exactly as it is today.

**Contract shape (D3)**

- **FR-007**: The delivery-record contract MUST return its outcome to the caller. This is a breaking change to the contract and is accepted.
- **FR-008**: The capability MUST NOT be introduced as an optional interface that a store can decline to implement.
- **FR-009**: All in-repository implementations of the contract MUST be updated to report the outcome, including the in-memory implementation.

**The deterministic fence trigger (D4)**

- **FR-010**: The deliverable candidate selection MUST exclude items that carry a fencing token from an earlier claim, in addition to the in-delivery items it already excludes.
- **FR-011**: The dead ownership-filter parameter MUST be removed from the deliverable query contract, together with every refusal raised in response to it.
- **FR-012**: All three sites that reference the removed ownership filter MUST be handled, not left asserting behaviour that no longer exists: one whole test method (a genuine test removal requiring architect approval), one assertion pair inside a surviving test, and one validation-guard assertion line.
- **FR-013**: Ownership filtering MUST NOT be implemented on the deliverable query. No caller requires it, and adding it would create an untested surface.

**Documentation (D1)**

- **FR-014**: The documented single-writer ownership invariant on the live-drain scope MUST be corrected. It currently states that no other deliverer competes for the same execution's intents, which the background sweep contradicts.
- **FR-015**: The corrected documentation MUST name the sweep as a legitimate competing deliverer, so the next reader does not rebuild on the false invariant.

**Evidence (D5)**

- **FR-016**: A delivery-processor-level test MUST exercise a single delivery contended by an injected steal, and MUST assert the superseded outcome rather than an exception.
- **FR-017**: A store-level test MUST exercise the deterministic fence case: an item claimed, returned to a deliverable state, then delivered claim-lessly.
- **FR-018**: A store-level concurrency test MUST run against a real PostgreSQL instance, reusing the existing container fixture and skipping gracefully where no container runtime is available.
- **FR-019**: **For every test added under FR-016 through FR-018, the production change MUST be reverted and the test MUST be observed to fail.** A test not proven to go red is not accepted as evidence. The observation MUST be recorded in the pull request.
- **FR-020**: Tests MUST use the repository's pinned test framework and assertion idiom. Fluent assertion libraries are constitutionally excluded.

**Scope boundary (D6)**

- **FR-022**: The claim-completion concurrency guard (D7) MUST NOT be implemented in this unit. It is filed as a separate follow-up.
- **FR-021**: The specification MUST record the duplicate-key log noise as an observed non-defect, note that this fix is expected to reduce its frequency as a side effect, and state the correlation check that would confirm the connection — **without** asserting that diagnosis.

### Key Entities

- **Outbox item**: a unit of post-commit work awaiting delivery. Carries a delivery state, an optional owning deliverer, a fencing token that only ever increases, an attempt count and a retry policy.
- **Claim**: a time-boxed assertion of ownership over one outbox item by one deliverer, identified by an owner and a fencing token. Expires, at which point the item becomes claimable again.
- **Live drain**: the in-process delivery loop that owns delivery for the single execution it is draining, and skips the durable claim round-trip to avoid the round-trip cost.
- **Resumption sweep**: the background, timer-driven delivery loop that claims items across *all* executions with no execution or intent filter. This is the competing deliverer.
- **Delivery outcome**: what a delivery recording reports back — delivered, failed, or (new) superseded by another owner.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Starting a workflow while background delivery is in flight succeeds. The reporter's catalog, which hit this in 3 of 4 runs on `sha-71852a1`, completes with **zero** starts answering 500 across a comparable number of runs.
- **SC-002**: Every start produces an instance: the count of instances created equals the count of starts issued, for both the single-start and the ten-start suites.
- **SC-003**: No outbox item is delivered twice in effect. Work represented by a contended item is performed exactly once, and the item reaches a terminal state exactly once.
- **SC-004**: An item that was previously claimed and returned to a deliverable state can be delivered without error on the first attempt — the deterministic case fails 100% of the time today and 0% after.
- **SC-005**: Every new test is demonstrated to fail when the production change is reverted, with that demonstration recorded in the pull request.
- **SC-006**: Crash recovery is unchanged: an item whose owning deliverer dies mid-delivery is still redelivered by claim expiry and the resumption sweep.
- **SC-007**: A provider that cannot report the superseded outcome does not build, rather than failing intermittently under load.

---

## Out of Scope

- **Duplicate-key errors on the scheduler work-item table (D6).** Six were logged in the reported run and none surfaced as a 500. The work-queue store already catches that collision, identifies it by its error code, re-reads the winner and returns it; the database driver's own logger emits the raw error at error level *before* that handling runs, which is precisely the observed "logged, no 500" signature. The item is therefore log noise from a race the code is designed to lose safely.

  This fix is **expected to reduce its frequency** as a side effect, because a superseded delivery stops issuing the second, redundant enqueue. That expectation is **not** a diagnosis. Two other routes produce a byte-identical log line and cannot be distinguished from source: double *execution* of a single work item (successor identities are derived deterministically, so two runs derive the same one), and ordinary multi-pump concurrency at any of roughly nine enqueue sites.

  **The check that would confirm it**: correlate the timestamp of a duplicate-key log line against the claim state of the corresponding outbox item. If this race is the cause, that item shows both a sweep claim and a live-drain terminal transition inside the same window. If instead the source work item shows two distinct claim owners, it is the double-execution route and a separate investigation.

- **Suppressing the duplicate-key log line.** That is a logging-configuration decision about an error the runtime handles correctly, and it should not be bundled with a correctness fix — particularly not on a race we have not proven is the one being logged.

- **An end-to-end concurrent-start test through the HTTP layer.** De-prioritised on the reporter's correction: it would miss the single-start failure that now reproduces most often. It is additionally blocked on infrastructure that does not exist — the only host that maps the execute route stubs the start service outright, the runtime host application is never booted in a test, there is no web-application test factory anywhere in the repository, and the workflow execution harness hardcodes a single execution identity. Building that harness is a larger piece of work with its own value, and belongs in its own unit.

- **Implementing ownership filtering on the deliverable query.** Excluded by D4. The parameter is removed rather than implemented.

---

## Follow-Up — filed separately (D7)

**Concurrency behaviour on the claim-completion path — verify whether it is a defect at all.**

*Corrected 2026-09-17 after reading the code.* This item was originally recorded as "`CompleteClaimAsync` has no concurrency guard". **That is wrong.** It does have one: it catches `DbUpdateConcurrencyException`, rolls back, re-reads, re-runs the transition to surface a stale claim, and then throws `InvalidOperationException("…changed concurrently; retry completion.")`.

So the open question is narrower than first stated: `ClaimAsync` *swallows* the conflict and skips the item, while `CompleteClaimAsync` *throws*. That asymmetry is plausibly correct by design — a failed claim is trivially skippable because another cycle re-claims it, whereas a failed completion means the delivery already happened and was not recorded, which cannot be skipped silently.

What remains worth checking is (a) whether that throw can reach an HTTP caller, and (b) that the path is untested at every layer. On the start path it is not reached: `DrainImmediateAsync` requests only `EnqueueSchedulerWork` and the live-drain accessor is registered by default, so the claim-less branch is taken. A coalescing burst does take the claim path, so a non-session-owned item's completion could reach it there — unproven either way.

**This is not the defect the reporter observed.** Their logs carry only the claim-less message, "is claimed; its owner and fencing token are required". The claim-completion path produces a different message that never appeared.

**Decision (D7)**: handled **as a separate unit of work, after this one.** It is a different code path with a different fix, and bundling it would blur the per-change revert-to-red evidence that D5 requires.

**This carries a scheduling constraint**: downstream consumers are currently blocked by the 500 this spec fixes, so unblocking them takes priority. Nothing in this unit may sit on the critical path of that fix unless it is required for correctness. Concretely — the P1 stories and their gating tests ship first; documentation corrections and the container-backed provider test are valuable but must not delay the unblocking change.

---

## Assumptions

- **The reporter's single-start observation is accurate.** It is runtime evidence from their environment and could not be verified from source in this repository. It is consistent with, and independently arrived at the same mechanism as, the source analysis. If it turns out to be a different failure, the D5 gate shape should be revisited — but the two defects fixed here are established from source and stand regardless.
- **The superseded outcome is reachable by any store**, because every store can compare a presented recording against the item's current owner and fence. No new persistence capability is required.
- **Retry and attempt accounting belong to the completing deliverer.** A superseded recording consumes nothing, on the basis that the owner's completion is authoritative.
- **No external implementations of the outbox contracts are in the field** that would make the D3 break unacceptable. The break was accepted with this understood; if an external implementer surfaces, the decision is the architect's to revisit.
- **The existing container-based PostgreSQL fixture is reusable** for the FR-018 test, and its graceful-skip behaviour on machines without a container runtime is acceptable for this gate.
- **Fixing delivery contention does not change delivery ordering.** Items delivered by the sweep rather than the live drain are delivered slightly later, which the system already tolerates, since the sweep is an existing delivery path today.
