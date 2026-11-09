# Durable Rules data model

This document defines the logical records and invariants that Rules persistence must preserve. It does not prescribe EF tables or provider SQL. The provider contract is in [Persistence](contracts/persistence.md), and implementation limits are in [the plan](plan.md#finite-v1-bounds).

## Domain boundary

Rules owns immutable inference artifacts, invocation identity, semantic state, round receipts, cancellation, terminal outcomes, and owed native delivery. A Rules invocation is not a workflow execution, scheduler item, actor, or one-activity-per-rule topology. Sequence and Flowchart retain their native ordering, sibling, and join semantics; their adapter only submits and observes a Rules invocation through native publication and checkpoint/outbox/bookmark seams.

The optional domain consists of six modules: Core, Runtime, Persistence.Core, Persistence.EntityFrameworkCore, Workflows.Design, and Workflows.Runtime. The semantic implementation pin covers the three semantic packages—Rules.Core, Rules.Runtime, and Rules.Persistence.Core—plus any explicitly semantic third-party dependency. The remaining modules supply provider storage and native composition. This model introduces no universal registry, runtime package downloader, new actor membership, or per-node actor.

## Immutable artifact and language

An artifact snapshot identifies the exact canonical finite rule model used by a run:

- artifact format, language, and canonicalization versions;
- a canonical content identity over the exact artifact bytes;
- uniquely identified rules, each with positive conjunctive premises and one conclusion;
- the finite set of propositional atoms available to the invocation.

A fact is one atom. Rule variables, terms, bindings, negation, retraction, arbitrary code, and external effects are excluded. All applicable rules inspect the same frozen input facts for one round; conclusions become input only in the next round. The complete receipt records every firing, including conclusions already present, while its additions contain only previously absent atoms. Canonical identifiers and set ordering use the artifact’s versioned ordinal encoding.

The artifact explicitly declares a finite vocabulary. Every initial fact, premise and conclusion must belong to it; out-of-vocabulary input is invalid, not an implicit vocabulary extension. An empty vocabulary with no rules is valid. The finite vocabulary and initial facts determine the natural closure limit. With no explicit run limit, the facade freezes the number of absent vocabulary facts plus one, allowing a final no-addition round. A positive lower limit is also allowed; if its last committed round adds facts, the run faults as non-converged instead of claiming success. Nonpositive limits and malformed artifacts are invalid before admission.

## Scope, invocation, and implementation pin

| Record | Identity and retained values |
|---|---|
| Rule scope | Nullable tenant identifier plus persistence partition. Null is an exact tenantless scope, never a wildcard or fallback. |
| Run identity | Exact scope plus stable invocation ID. A unique scoped key arbitrates admission, cancellation, and refusal. |
| Invocation envelope | Immutable artifact snapshot, canonical initial facts, captured positive round limit, invocation/state schemas, expected semantic implementation pin, and optional frozen native result destination. |
| Invocation fingerprint | Rules-computed, versioned canonical SHA-256 over the scope/key and every immutable invocation field, including the optional destination. It is not independently caller supplied. |
| Semantic implementation pin | Pin schema and exact package IDs/versions/NuGet content hashes plus selected runtime asset relative paths, assembly full names, and loaded asset SHA-256 values for the semantic package set. |
| Deployment provenance | Raw package archive digest and runtime/framework environment are reported separately from semantic pin and model-content hash. Neither is publisher-authentication evidence. |

Scope equality is ordinal, case-sensitive and normalization-free; no trimming or case folding occurs. Partition and invocation ID are nonempty valid Unicode strings of at most 256 UTF-8 bytes. Tenant is null or a nonempty string under the same bound. Null has a distinct canonical tag. Providers index the SHA-256 of a versioned length-prefixed encoding and retain/compare the full canonical identity before any match; a hash collision fails without exposing or mutating a different run. Case variants and distinct Unicode encodings are different identities.

For a direct caller, the facade obtains the expected semantic pin from its trusted Rules deployment identity source. Native compilation persists the selected trusted pin with the compiled executable node and frozen result destination. Admission compares that explicit pin with the installed deployment receipt; it never substitutes the currently active deployment pin for an old invocation. A retried or recovered run keeps its original artifact, invocation fingerprint, pin, state schema, and routing destination.

## Run state and transition identity

A run’s authoritative state contains its immutable invocation envelope and fingerprint; lifecycle status; semantic model revision; canonical committed facts; committed-round count; the one current owed round or accepted pending receipt; and, once terminal, an immutable terminal result. The store may split this logical aggregate across rows, but each transition must preserve the same invariants atomically.

Lifecycle statuses are Running, Succeeded, Faulted, Cancelled, and Refused. Refused is a permanent pre-admission Rules settlement, not an admitted inference run or a fault produced by an admitted interpreter. Structurally invalid invocation input, including a nonpositive round limit, creates no Rules row. For a native caller, the adapter resolves its normal durable dispatch-refusal outcome only after proving the committed parent destination; if proof is not yet available, the parent keeps retryable responsibility. No child or Rules delivery row is fabricated for that invalid request. A valid envelope with deterministic permanent Rules-level invalidity established by a compatible interpreter may settle Refused under the same scoped key; that settlement carries no admitted inference state. Temporary absence or mismatch of the installed semantic pin or supported schema is Unavailable and does not create a refusal. A common invocation envelope can be checked for cancellation without interpreting an old artifact or requiring support for its semantic version.

The model revision is a compare-and-swap token for changes to semantic state. It advances on admission, accepted-receipt persistence, receipt consumption, cancellation, and refusal settlement. It is separate from worker ownership. A work or delivery lease has an owner, monotonically replaced fence, and trusted expiry; renewal changes lease expiry/provider concurrency data but not model revision. Lease expiry does not prove the previous process stopped.

## Round state and receipt

A round identity binds the scoped run and invocation fingerprint to its ordinal and input-state digest. Its frozen input includes the artifact, implementation pin, canonical fact set, round limit, and model revision. The accepted receipt contains:

- that exact logical round/input identity and implementation pin hash;
- the complete canonical set of firings and conclusions;
- the canonical set of newly added facts;
- a semantic receipt digest.

The physical evaluation attempt—attempt ID, worker ID, and lease fence—is stored separately from the semantic receipt. The receipt ID is derived from logical round identity. Its semantic digest excludes both its own digest field and attempt metadata. Equivalent semantic results for one logical round replay even when the retry ran under another attempt; persistence retains the first accepted physical attribution. A different semantic digest for the same round conflicts.

Acceptance stores exactly one immutable receipt and advances model revision. It does not change authoritative facts, increment committed rounds, or settle success. Consumption names the stored receipt identity rather than replacement facts. In one transaction it verifies the receipt, appends its separate consumed-at-revision marker, applies its additions once, increments the round count, then creates exactly one next owed round or one terminal result. It cannot both create a next round and a terminal result. The accepted receipt and revision-stamped consumption marker preserve the historical progress view.

Cancellation before admission wins the unique invocation key with a terminal tombstone and an empty result fact set. Matching admission replays it; a different fingerprint conflicts. Cancellation after admission preserves initial and committed facts, including initial facts before any round, but never applies an accepted unconsumed receipt. If consumption wins first, its facts and count remain committed; later cancellation may terminate only a still-running run. No terminal state can be rewritten by a late worker.

## Terminal result and native delivery

A terminal result records stable terminal identity, outcome, committed round count, result schema, the complete bounded canonical committed fact set and its digest, plus an optional safe diagnostic code. Success occurs only when a committed round adds no facts, including when that is the last permitted round. If the last permitted round adds facts, the run terminates Faulted with the diagnostic code rules.round-budget-exhausted and preserves those facts as non-converged progress. Budget exhaustion is an admitted model fault; validation refusal and deployment unavailability are separate outcomes.

Permanent pre-admission refusal and cancellation produce no admitted inference state; their result facts are empty. Once admission succeeds, cancellation returns initial plus committed facts. Pending, unconsumed additions are excluded.

When a native destination was frozen at admission, terminal settlement and its Rules-owned delivery obligation commit atomically. Direct runs have no native delivery row. Delivery has its own identity, immutable payload hash, and independent lease/fence. The adapter may acknowledge only after validating durable proof of equivalent native consumption, prior consumption, or a terminal parent against the frozen executable, bookmark, route, and payload identity. Enqueue success, timeout, missing parent evidence, or a cancellation request alone is not proof. A terminal parent may retire delivery without rewriting the already terminal child result.

Terminal results and receipts contain collections. Deserialized records must be checked by canonical content digest and full bounded validation, never by generated record equality over collection references.

## Bounded reads and retained evidence

Run views expose exact-scope identity, pins, status, model revision, committed-round count, pending receipt identity, terminal identity/outcome, and bounded counts. Progress is read in stable, exclusive-cursor pages bound to one captured model revision; later facts are outside that page sequence and cannot appear before its cursor. Full artifacts, accepted receipts, and terminal results are read only through the specific bounded query that needs them. Reads do not acquire or renew leases.

The plan’s v1 bounds cover rule/vocabulary counts, identifiers, artifact bytes, invocation/receipt/result bytes, one current persisted-state projection, page entries, and the greatest accepted round limit. The state-projection cap is not a cumulative history cap: accepted and consumed receipts remain separate immutable history rows, read through bounded pages. Before admission, canonical input and conservative maximum reachable receipt/result representations must fit their envelopes, including encoding and frozen routing overhead. The admitted model may not later fail solely because a valid reachable result exceeds a storage or delivery cap. Concrete values and exact/one-over acceptance tests are owned by [the plan](plan.md#finite-v1-bounds).
