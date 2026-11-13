# Target promotion and recovery contract

**Status: Proposed.** Design for [#3590](https://github.com/elsa-workflows/elsa-foundation/issues/3590), not shipped API or qualification evidence.
The owner selected coordinated per-owner switches, a visible mixed interval, per-run resource mapping choices,
forward recovery and explicit rollback. Accepted [ADR 0043](../../../docs/adr/0043-publication-slots-define-start-authority.md)
keeps Runtime slots authoritative. Its October convergence amendment and ADR 0034 remain proposed.
Names below describe required records/operations; they do not assert existing public contracts.

## Target, preview and admission

- Every request identifies the authenticated target authority/environment, tenant and trusted persistence partition; a URL,
  display label or caller-supplied partition is insufficient. This full scope participates in context digests, previews,
  attempt/idempotency identity, mapping/locator records and receipts, including ETL owner identities.
- Source credentials are never forwarded to a target. Existing target sessions and normal permission/resource checks apply.
- Preview observes exact release content, target immutable identities, owner/revision/selection, capabilities and prerequisites,
  logical-resource mappings with stable target incarnation identities, evidence status, trigger claims and intended changes.
- Its fingerprint binds those observations, release digest, mapping capture digest, action and target scope. It reserves nothing.
- Apply checks current authorization and all material observations again. Changed content, resource incarnation, owner,
  revision, selected binding capture or relevant capability/policy invalidates the preview; it never silently refreshes intent.
- Apply admission records the actor, effective scope and authorized operations. Denials produce scoped safe audit diagnostics;
  cross-tenant refusal must not disclose another tenant's records or resource existence.
- A target/tenant-scoped idempotency key binds a canonical request fingerprint. Identical retry returns/reconciles the same
  attempt; different intent returns conflict. A new key cannot bypass an overlapping unresolved attempt.

## Immutable intent and mutable observations

Before effects, persist an immutable attempt containing release/binding digests, preview fingerprint, action, actor/scope,
evidence references and an ordered, complete member plan. Each member records its owner and target identity, exact desired
content, original selection/revision, stable command IDs and predecessor content needed for recovery. Before staging,
freeze each material-stage request and its fingerprint, candidate publication/reference IDs, and a bounded deterministic
recipe for resolving target material locators from those stage commands' exact receipts. Source member identities,
source-qualified original locators, full producer pins and retained source bytes are fixed inputs to that recipe.
The target physical incarnation of missing material is an owner result, not a value invented during read-only preview.
Never replace the original expected revision with a newer observation merely to make a retry succeed.

### Resolving staged material before activation

Preview records each target material precondition as missing or present with its exact observed incarnation and immutable
content identity. It writes no material, reference, reservation or receipt. A preview with missing material reports unresolved
target locators; it cannot claim a final runtime capture/content digest containing physical incarnations that do not yet exist.
The mapping-choice digest and preview fingerprint remain available independently of those unresolved execution facts.

A material-stage command has one immutable request and fingerprint. Within the owning transaction, the owner validates its
scope, command fence and original presence/incarnation preconditions, stages exact supported material, establishes inert
durable retention and commits an immutable receipt containing the actual target identities/incarnations and material identity.
Identical command replay returns that receipt before treating its own writes as target drift. A different request cannot use
the same command ID. A competing create or a deleted/recreated observed incarnation fails the original precondition; it is
not silently adopted or substituted. An unreadable commit is reconciled by that command's owner receipt, not inferred failed.

The attempt appends exact owner receipts without changing its reviewed intent. When all required material receipts are
available, resolve the fixed locator recipe and freeze each actual activation command payload and fingerprint once, before
registering that activation command or causing a serving effect. The frozen payload names the exact receipt facts and final
runtime capture. Every retry uses those same bytes, IDs, locators and fingerprint. Missing, conflicting or replaced receipt
material is a visible conflict or unresolved outcome; recovery cannot recompute the payload with a newer incarnation.

This resolution step does not refresh the preview. Before registration and serving effects, revalidate current authorization,
capabilities, resources and owner state against the original reviewed observations and slot expectations. Staging's own
receipt-proved additions are accounted for explicitly; no newer resource observation, slot revision, owner or selected capture
replaces an original expectation. Dependency-only material remains inert throughout staging and resolution.

The attempt journal has its own optimistic revision and executor lease/fence. Member observations include dispatch intent,
owner receipt, actual revision/selection, serving checks, diagnostics and remaining own effects. Journal state follows owners;
it cannot start workflows, decide routing, transfer source ownership or substitute for an owner's receipt.

## Bindings and starts

- A binding capture is immutable non-secret data mapping logical requirements to authorized target resource identities and
  incarnations. Keep deployment/test revision observations separately: the runtime mapping-choice digest does not freeze
  current settings or secret versions. Evidence/readiness fingerprints also bind observed non-secret owner revisions and
  prerequisite state, so changed configuration cannot silently inherit earlier integration proof. Creating a new capture
  does not rewrite workflow/template content or old captures.
- Requirement identity is the structured defining-member identity plus authored origin/placement path, input/reference
  identity and resource kind. Equal technical names in different roots or reusable placements remain distinct unless
  explicitly grouped after compatibility checks. Model adapters retain their own semantic origin/scope identities.
- Activation selects a new runtime activation context that references the capture. The owner commits that selection with
  the slot transition; it cannot switch content first and attach bindings later.
- New-start acceptance captures bounded immutable runtime-owned binding material with its exact executable/dependency pins.
  The durable source reference, saved stimulus recipient, start mailbox/command, first/subsequent checkpoints, child-dispatch
  record/outbox and supported native/BPMN/ETL worker payloads preserve and validate that material and its digest.
  Resumption and exact retained-dependency starts inherit the captured requirements for that dependency. An independently
  selected live workflow uses its own selected source's mapping capture. Reusable placements preserve their scoped origin.
  A missing/mismatching required capture fails before resource use; it never falls back to current release mappings.
  Legacy starts with no deployment capture retain ordinary direct-name behavior without a latest-deployment lookup.
  New optional context fields preserve legacy serialized/fingerprint forms when absent. Keyed accepted-start replay checks
  the saved context before current eligibility, and never reselects today's context for an already accepted start.
- The mapped resource owner still resolves current authorized configuration and credentials. Normal secret rotation,
  revocation and supported connection configuration changes take effect; historical secret values/settings are not frozen.
- Rebinding to another resource or a deleted/recreated incarnation requires another preview and deployment. A revoked
  resource can make an existing run fail under normal policy; retaining its mapping does not retain permission to use it.

## Stage before selecting new starts

Validate the complete supported closure and target prerequisites before mutation. Import exact immutable material through
its owner, verifying identities and hashes; no compile, identity rewrite, dependency substitution, package installation or
schema upgrade occurs. Unsupported required kinds block promotion with the dependency path.

Stage required dependencies in deterministic dependency order through their owners. Retain source-owned/read-only status.
Staging can add exact immutable dependency/catalog material and retention roots; report these effects separately from
serving changes. A staged dependency is not evidence that its dependent workflow is active. Cycles fail closed.

Only explicitly selected activation roots contribute serving-slot changes. Dependency-only workflows remain unavailable
for independent starts and gain no routes/schedules/manual-start publication merely by import. Preserve any existing
serving slot unless its change is explicitly in the reviewed plan. Exact retained-dependency invocation still uses its
ordinary runtime authority; do not reuse the mounted reconciler's all-members activation pass as staging.

Before a workflow can switch, every required owner must confirm durable availability and the attempt must retain both its
candidate closure and promised predecessor closure. Stage/write receipts are idempotent by stable command fingerprint.
Interruption can leave retained inert material; recovery adopts exact matching material and refuses identity conflicts.

## Owner command receipts and revocation

Existing `IWorkflowActivationCoordinator`/`IWorkflowActivationSwitch` provide per-slot CAS and serving projection coordination.
They do not yet provide the complete deployment command receipt/fence below. Each supported owner adapter must implement
and qualify this additional protocol in its own transactional boundary; unsupported adapters refuse before activation.

1. Register the plan's fixed command IDs with immutable payload fingerprints, expected owner/revision and candidate identity.
   A command row has a concurrency revision, a dispatch fence and lifecycle `Ready`, `Switched`, `Settled`, `Compensated`,
   `Rejected` or `Revoked`. Transition receipts are immutable; final owner outcome is recorded separately from switch commit.
2. A command execution transaction reads that row and checks its fingerprint, fence and `Ready` state, target scope,
   applicable authorization and original owner/revision. It stages the owner's transition through the owner's own rules.
3. The same owner transaction commits the transition, binding-context selection where applicable, and immutable switch receipt
   identifying actual old/new selection and revisions. The command row's concurrency token fences a concurrent revocation.
   The command becomes `Switched` while owner callbacks/compensation remain possible. Existing coordinator observer failure
   may revert its own transition: that compensation must check this command/fence and original resulting slot revision,
   then commit the revert and a separate compensation receipt atomically. It is not external supersession or group rollback.
   Settling the owner outcome and granting permission to compensate are mutually fenced; no callback may revert a `Settled`
   operation later. A terminal applied member requires owner settlement plus confirmed serving state. A committed no-op
   receipt records the exact validated selection, binding capture and revision it observed and has no pending compensation.
4. Replaying the same command returns its existing receipt. A different payload cannot reuse its identity. Authorized
   takeover may monotonically advance a still-Ready command's fence by owner CAS without changing its immutable intent;
   an executor presenting a stale or unregistered fence cannot commit.
   Known refusal is recorded as `Rejected`; an unreadable/uncertain commit remains unresolved, never inferred rejected.
5. Revocation atomically changes an uncommitted command to `Revoked`; it loses to a committed transition and returns its
   receipt and unresolved/settled owner lifecycle. A `Switched` owner must be reconciled, including any guarded compensation,
   before the attempt can terminate. For a not-yet-registered planned ID, create a revocation tombstone so delayed registration cannot revive it.
   A delayed executor must recheck the command row within its transition transaction, not just before calling the owner.

The receipt is command evidence, not a second serving authority. No transaction is claimed across different owners or
between an owner's commit and the attempt journal. If an owner cannot commit the guard, effect and receipt together,
that composition is unsupported until an equivalent fenced protocol is proved; a release-level lease is insufficient.

## Overlap and external writers

Initially serialize release attempts per target authority/tenant. An executor lease controls duplicate workers only;
ordinary publish, Git reconciliation and other legitimate writers do not take it. Preserve owner CAS for every transition.
Lease takeover resumes the same immutable attempt and command IDs; stale journal writes fail their journal CAS.

An overlapping new attempt receives an explicit unresolved-attempt conflict until the previous dispatched commands have
receipts or durable revocations. It may then obtain a fresh preview. External owner writes are detected through revisions;
promotion neither takes ownership silently nor overwrites a newer selection by rebasing its command.
Trigger/resource conflicts require owning-domain protection, including races across distinct slots. Release serialization
alone does not protect against external publishers. Existing HTTP pre-write validation suggests a cross-slot race to
investigate; this is an unresolved qualification concern, not a reproduced defect or a proven current guarantee.

## State and recovery

| Attempt state | Meaning |
|---|---|
| Accepted / validating | Durable intent exists; admission/current target checks are in progress. |
| Staging / staged | Required material is being retained, or its durable availability is confirmed; workflows are not thereby active. |
| Applying | Deterministic per-owner switches are progressing; mixed serving versions are allowed and visible. |
| Recovering | An owner outcome is unknown or transiently incomplete; recovery has unfinished work. |
| Active | All planned effects are confirmed: serving roots select their intended content/context, catalog-only roots are available and dependencies are durably staged/retained. Per-root roles and observed revisions remain explicit. |
| No-op | All intended content, binding captures and selections already matched; no deployment selection changed. |
| Failed | Dispatched outcomes are resolved and no serving change from this attempt remains; staged effects remain reported. |
| Partial | Resolved unsuccessful attempt with own serving changes remaining and a blocker to forward completion. |
| Superseded | Another writer invalidated intent; commands are resolved/revoked and any remaining own effects are listed. |

Persist dispatch intent before issuing each command. Recover after timeout, cancellation, crash or journal-write failure by
reading the owner command receipt and actual authority; use `EnsureServingAsync`/publication `CompleteAsync` where applicable.

| Observation | Required resolution |
|---|---|
| Settled applied owner outcome; intended activation/context still serves | Record applied with actual revision; finish lagging bookkeeping. |
| Switch receipt exists; owner callback/compensation outcome unresolved | Remain recovering and resume the owning lifecycle under its guard; do not declare terminal success. |
| Owner compensation receipt reverted its own transition | Record compensated owner failure and actual selection; distinguish it from an external writer's supersession. |
| Original selection/revision; command still Ready | Recheck authority/readiness and replay the same command, or revoke it. |
| Another owner/revision/selection | Stop new dispatch; reconcile/revoke all remaining planned commands and record supersession. |
| Our command committed but its selection was subsequently replaced | Preserve historical applied receipt; record current supersession, not active success. |
| Owner/receipt/serving cannot be read consistently | Keep unknown/recovering; retain candidate and predecessor material. |

Forward recovery retries only safely replayable original intent; current permissions and target prerequisites still apply.
The first contract uses **fresh authenticated reauthorization for further transitions**, not a new durable delegation system.
Startup/background repair may reconcile already committed owner receipts into history; it cannot issue a new serving
transition on the strength of a saved actor ID. An authenticated resume/retry by the original actor or an operator/pipeline
with the same target-scoped apply permission may continue it; read permission alone cannot resume or revoke it. Record both
the original actor and each resuming/revoking actor. Enforce current tenant/resource scope and normal request authorization
before dispatch. An expired/cancelled request stops new dispatch and leaves uncertain effects Recovering; read/reconcile
under internal repair authority, then require an authorized resume or stop request. Never store bearer tokens in the journal.
An authorized takeover advances each still-ready command's fence before dispatch; delayed old requests fail the owner guard.
The current request's authorization decision applies to its dispatched command as with ordinary publication requests;
permission changes do not erase committed effects. Preview/apply/resume each reauthorize, and an expired context cannot
silently authorize a later worker. GET/status cannot switch serving state. Denied recovery remains visible with its next action.
On a permanent blocker, resolve/revoke outstanding commands before reporting Partial/Failed/Superseded. No automatic rollback.
A pending supersession reason stays Recovering until late commands are accounted for. Later drift is recorded separately
from the historical fact that an attempt completed; Active is not a promise that external writers cannot change the target.

## No-op, rollback and retention

Same artifact alone is insufficient for no-op. The owner must validate exact content, selected capture/context, scope,
owner, revision and serving state. Binding-only change creates a new activation/context and revision even for the same artifact.
Current Runtime same-artifact early return and publication candidate create-only behavior need qualified owner adapters;
blind replay of an original Candidate after its publication record changed lifecycle state is not a supported retry strategy.

Rollback is a new explicit attempt selecting retained prior content and a reviewed mapping capture after fresh validation.
It can replace an occupied slot through normal ownership/CAS rules. Existing restore-latest-into-empty-slot behavior is not
release rollback. Rollback changes future-start eligibility, not running pins, captured mappings, schemas or business effects.

Use existing fenced root-write/reference retention seams for explicit release/attempt/rollback roots, separate from the
publication references a switch retires. Protect complete closures and binding captures needed by serving contexts,
retained executions, unresolved attempts and promised rollback. Release an attempt root only after owner outcomes settle;
release rollback roots only under explicit retention policy. Execution roots remain independent. GC must fail conservatively
when owner/root evidence is incomplete. Deleting display/history metadata cannot delete serving or execution-required content.

## Engineering proofs required before production implementation is accepted

- Owner-transaction effect/receipt/revocation fencing, including delayed old workers and split-owner recovery, through real hosts.
- Atomic activation-context selection and new-start capture; binding-only switches; old-run mappings with credential rotation.
- Whole-closure retention versus concurrent GC, publication retirement, interruption and explicit rollback retention expiry.
- Existing candidate lifecycle replay, unknown commit, observer failure, cross-slot trigger races and foreign-owner refusal.
- Authorization loss, request expiry and authenticated resume/revoke, tenant/authority changes and equivalent Studio/API/packaged CLI outcomes.
