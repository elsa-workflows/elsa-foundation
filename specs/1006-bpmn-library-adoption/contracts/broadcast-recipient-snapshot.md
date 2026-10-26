# FR-015 — durable delivery-time recipient snapshot

Status: reviewed plan refinement for the existing #2735 switch. The owner selected first-delivery
matching; the reviewed specification is [FR-015](../spec.md). This document adds no implementation
receipt and does not reopen that decision. Spec review preceded this design; task refinement and
source implementation follow the independent design review. Correctness, standards and acceptance
plan reviews completed clean; this does not qualify implementation or host behavior.

## Authority and sequence

The existing exact committed outbox row is the single delivery authority. Its immutable Intent stays
unchanged. Add a versioned recipient snapshot to its delivery state; do not create a second replay
ledger or put recipients in the publisher's checkpoint. Selection happens during first delivery.

1. Obtain the exact durable source row and current delivery claim through an explicit delivery context.
   Bind its full commit-specific OutboxItemId, canonical immutable Intent and current owner/fence to the
   provider's scoped source. IntentId alone, an authored source ID or an in-memory claim is insufficient.
2. If a valid cohort is already present, use it. Otherwise resolve the complete eligible bookmark set
   before any triggered start, then resolve all eligible start bindings. No recipient dispatch occurs
   during these finite traversals. Preserve the existing scope/eligibility rules and deterministic order.
3. Commit absent → whole snapshot under source-row revision CAS and current claim authority. The
   provider returns the authoritative persisted winner; a losing proposal dispatches no local candidate.
   Lost claim, corrupt source, unavailable provider or incomplete traversal produces no recipient effect.
   A renewal racing CAS may require reread/retry; it cannot replace the cohort or erase another writer.
4. Dispatch only saved recipients through existing start/resume dispatchers. Partial failure, shedding,
   cancellation, acknowledgement loss, concurrent retry and restart reuse that cohort and existing keys.
   An expired claimant never chooses a new cohort. Existing claim renewal, completion fencing and
   continuation-before-mailbox ordering remain in force.
5. Mark the existing outbox delivery complete only after the saved cohort's responsibility is resolved.
   Retain the snapshot through retry, FailedFinal/redrive and acknowledgement loss. FailedFinal is still
   owed; it is not successful delivery or permission to release references.

The first committed selection is the linearization point for the recipient set. This does not claim a
cross-store transaction or globally instantaneous read across trigger/bookmark indexes: preserve their
current finite scoped selection semantics, then atomically choose one complete candidate set. A crash
before that commit permits selection again because no recipient effect has begun. A crash after it
must find the saved set, even if no first recipient was dispatched.

## State and identities

| State/model | Required contents and invariant |
|---|---|
| Uninitialized snapshot | Absent, distinct from a present empty set. Never interpret malformed present data as absent. |
| `StimulusDeliverySnapshot` | Explicit version, exact source/intent digest, matching timestamp and trusted routing scope, ordered start and resume lists. Two empty lists are an authoritative empty cohort. Immutable after acceptance. |
| `StimulusStartRecipient` | Complete saved binding identity and executable pin (artifact ID/version/hash), node, activation/slot, routing metadata and original order. Preserve every eligible binding entry, including several for one artifact. |
| `StimulusResumeRecipient` | Exact workflow execution and BookmarkId, with the saved eligibility/scope context needed by the existing dispatcher. No execution-only rediscovery on retry. |
| `PostCommitDeliveryContext` | Exact source row and current claim identity passed by the processor, not supplied by activity payload. The provider revalidates it; it is no independent authorization token. |

Model names are proposed, subject to the normal Core/API review. Use immutable snapshots/defensive
copies and strict version/identity/uniqueness validation. Page discovery deliberately to completion;
never truncate fan-out to satisfy a collection bound. If serialization/storage limits prevent an
atomic complete snapshot, refuse before dispatch and preserve the source rather than deliver a prefix.
Payload remains in the immutable Intent; do not duplicate it or expose provider diagnostics in status.

Preserve ALL start binding entries in the router's existing artifact/binding ordering. Keep
`KeyedWorkflowStartIdentity.For(originalKey, artifactId)`: several bindings of one artifact still
share that keyed start. A first binding can shed while a later same-artifact binding accepts because
shedding leaves the key unconsumed. Collapsing entries or introducing per-binding keys would change
ordinary behavior. Retry duplicate-start checks retain their current ordering. Resume dispatch keeps
the current parent key format and exact BookmarkId scoping, preserving existing accepted mailbox dedup.

Correlation narrows resumes, not starts. Local publication remains ResumeOnly to the publisher's trusted
execution ID. Preserve authoritative tenant/partition/test-run filtering and existing start/run scope.
Never derive authority or a target from user input. Newly started waits, rearmed listeners and newly
published bindings cannot join an already accepted cohort. A consumed/missing saved bookmark follows
the existing retired/duplicate outcome; it never substitutes another matching bookmark.

## Runtime and provider boundaries

Extend the existing dispatch/handler seam additively to carry explicit delivery context. Preserve old
CLR signatures and ordinary handlers through a compatible default path. The current keyed handler
contribution remains one implementation per exact kind; no new fan-out or BPMN-specific kernel branch.
An opted-in publisher without durable context/router/provider support refuses before effects, with
startup/pre-effect diagnostics rather than silent fallback to per-attempt matching.

Split selection from execution in the runtime stimulus router and reuse its existing start/resume
policy. Expose the durable operations through a named Runtime.Core contract; the Primitives publisher
must not reference Runtime implementation types or concrete EF stores. A proposed provider replacement
capability, `IStimulusDeliveryStore`, installs/reads the source row's cohort with the above semantics.
Register one scoped implementation per provider and detect competing registrations. Context/provider
ownership, failures and unsupported combinations belong in the relevant extension-point catalogs.

In-memory and EF providers use their existing checkpoint/outbox authority. EF stores the additive
snapshot in the row's existing ContentJson and uses its revision concurrency token with claim fencing.
Use declared outbox family2.0.0 with an explicit1.0.0 upcast preserving absence; ordinary predecessor
writes omit all new-only fields while the existing gate still observes1.0.0. Publisher protocol
acceptance checks the shared schema dormancy contract before effects. The supported fleet/hold/
finalization protocol advances the same host to2.0.0; storage never force-finalizes or restamps new
protocol data as1.0.0. Backfill uses the family's declared checked revision-CAS row rewriter. Old readers
must refuse the new stamp, rather than discard an unknown JSON member and rewrite it. Unknown cohort
versions, corrupt present cohorts and mismatched source/intent/scope refuse before effects. Preserve the
cohort through every constructor/copy, claim/renew/retry/completion, atomic checkpoint participant,
redrive, pending conversion, equivalence/replay check and coalescing path. Completion must merge current
persisted content; the pre-initialization claim.Item cannot overwrite a newly installed cohort.

Coalescing needs an explicit durable-source boundary before this handler runs. Its overlay can reissue
intents under a flush commit; only the resulting canonical durable OutboxItemId may own a snapshot.
Never snapshot/dispatch an overlay source, mint a replacement source during retry, or let overlay
reconciliation overwrite durable cohort state. Add generic required-source registration metadata or
an equivalent named contract, keeping ordinary handlers/coalescing defaults and constructor behavior.
Prove the real processor/flush path and mailbox ordering, not only a direct store invocation.

## Saved-start authority and retention

The ordinary start resolver requires a live publication. After republish, an already keyed execution
can deduplicate, but an unstarted selected old publication cannot currently start. Add a narrow retained
start authority for an accepted cohort entry. It validates the exact persisted source, immutable intent,
entry, full executable pin and scope through existing start admission. It must not reuse unrelated
RetainedDependency authority, rediscover a current publication or permit an arbitrary artifact ID.
Current domain/security admission remains applicable; the cohort grants only its saved publication
selection. Preserve normal shedding/duplicate behavior and safe diagnostics.

Add owed cohort start pins and their dependency closure to existing executable-GC roots and existing
root-write/deletion lease protocol. Cohort acceptance holds/renews the relevant existing root-write leases through durable installation,
and reference deletion performs its guarded final root recheck. They must coordinate: either
acceptance wins and its material is retained, or deletion wins and acceptance refuses before dispatch.
Retain source/cohort and accepted-delivery dedup evidence while responsibility is owed, including
terminal publisher runs and FailedFinal. Extend relevant execution/dedup deletion guards; the existing
called-workflow retention receipt does not establish broadcast retention. Use existing lifecycle/GC
mechanisms, not a second cleanup ledger. Conservative finite scans may be used with an explicit cost
statement; no new throughput claim is made.

## Upgrade and legacy ambiguity

New publisher writes declare the durable-cohort protocol at commit without selecting recipients there.
The additive source row retains the protocol version and the exact authoritative persistence scope at
that commit, including when an upgraded legacy publisher has no TenantId. Neither a caller's current
scope nor the eventual candidate snapshot can supply missing source ownership. All row rewrites retain
this committed witness. An expired claimant cannot install/select a cohort; each provider checks its
trusted clock against the current persisted visibility deadline, including renewed claims.
New protocol rows must be protected from old writers before first dispatch, not only after installing
the cohort. Verify explicit family-version/read compatibility and every relevant provider transition.

A legacy owed publish without a snapshot does not prove it has never dispatched: even attempt count
zero can remain after dispatch/acknowledgement loss. Do not silently resnapshot it or infer its original
cohort from current listeners. Add a finite actual-store preflight for legacy unresolved publishes and
mixed-writer/quiescence checks. Preserve such responsibility with an explicit recovery-required status.
If actual legacy owed rows exist and their original cohort cannot be proven, rollout remains blocked
for those rows and the concrete owner decision is presented individually with evidence. No speculative
owner question is required now. Delivered legacy sends retain their prior result; accepted workflow
private-state v1 and the frozen authoring contract are unchanged. Running binary rollback remains
unsupported where a new outbox family has been written.

## Required qualification before deletion/merge

- Competing distinct first cohorts, including empty versus nonempty: one persisted winner, no loser
  effects. Lost/renewed claims and stale completion cannot overwrite it.
- Delayed first delivery includes earlier-created waits. Pre-start wait snapshot excludes waits created
  by starts. Local/correlation/test-scope behavior and multi-binding shedding/dedup remain unchanged.
- Actual Foundation host plus fresh EF: crash after cohort commit/before dispatch; partial starts and
  resumes; acknowledgement loss after recipient acceptance; restart; rearmed same-name listener and
  newly published trigger excluded. Exact selected recipients execute at most one accepted effect set.
- Saved old-publication start after republish, including not-yet-started selected artifacts; no arbitrary
  retained-start authority. GC/deletion races retain required material and owed dedup/source state.
- Renew/retry/complete/redrive/replay/coalescing preserve cohort, including authoritative empty; malformed
  version/source and old-writer refusal controls. Real source-context and flush identities are verified.
- Actual legacy preflight evidence; meaningful mutations for cohort reuse, recipient identity, CAS,
  retained authority and retention; affected whole projects and byte-identical authoring fingerprint.

Library tests, synthetic cohort JSON and direct-provider CAS controls support these gates but do not
replace actual upgraded-host recovery or final Studio compatibility evidence. T045, T024, T029,
production #2898/#2907 and ADR0063 acceptance keep their separate existing gates.
