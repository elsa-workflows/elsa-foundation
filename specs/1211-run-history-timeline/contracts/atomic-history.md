# Contract: atomic Runtime history

Applies to FR-013–035, FR-040–044 and FR-047. Existing Runtime continuation state remains execution authority; history is read-only diagnostic information.

## Contribution and writer support

- A deterministic Runtime-owned checkpoint enricher contributes an optional versioned typed history batch before persistence policy/coalescing.
- History-enabled capture requires an explicit capable writer/read-store pairing. Existing third-party writers default to unsupported; decorators forward capability only when their underlying writer preserves the participant.
- Every checkpoint With* clone and fold preserves the batch. Fold concatenates original logical candidates without latest-state collapse. Synthetic flush envelopes do not recapture their final projections.
- A history-free commit retains its historical serialized fingerprint shape. Nonempty history participates through a versioned logical wrapper; assigned sequence/physical commit timestamps never enter that logical fingerprint.
- Capture profile is pinned to run/admission and original queued work. Enabling the feature MUST NOT enrich an old no-history replay with a different fingerprint. Legacy runs stay uncaptured unless a new explicit policy boundary enables future capture; profile changes cannot rewrite an earlier logical commit.
- Validate deterministic identities, exact run/subject agreement, allowed metadata/value references, finite bounded fields and conflicting repeated IDs before effects can commit.

## Commit and replay

1. Determine the exact replay fingerprint and consult the existing create-only marker.
2. A matching committed marker wins before allocation or effects; mismatch fails closed.
3. For a new commit, validate the same execution fence/admission authority as other runtime participants.
4. Within the existing atomic boundary, validate/load the scoped history head, allocate one contiguous sequence range, stage immutable events and affected bookmark projections, then the existing marker-last writes.
5. Commit all runtime state/history/head/projections/marker together. Required history failure rolls back every participant.
6. Reconcile an ambiguous acknowledged/landed write with the existing marker; replay yields original event IDs/order without allocation.

Head-only contention can be retried narrowly within existing limits or propagated as retryable unavailability, with no partial effect. It must not admit a stale execution fence or swallow corruption. In-memory implements the same semantic contract using the existing shared write gate/state, and makes no disk/restart claim.

## Persistence modes

Capture does not itself create a mandatory boundary, delayed outbox intent or other pending work. History-only candidates remain discardable for a never-persisted Ephemeral completion with recording off. Real suspend/bookmark/fault boundaries persist their candidates. A Balanced segment retains supported observed transitions while sharing one actual durable boundary; uncommitted work is excluded.

The head retains pinned Required capture policy: a new checkpoint missing its required batch refuses, including after module removal. Host toggles affect new admissions only. Supported active-run disable/late-enable requires an explicit quiescent policy boundary; otherwise it remains unsupported. Coverage changes/gaps are durable at actual boundaries; uncaptured history is not reconstructed later. An explicit purge does not reset semantic identity or sequence allocation.

## Semantic catalog

Common versioned events include workflow/activity lifecycle, bookmark creation/selected consumption/sibling retirement/stale cleanup/removal, incidents and resolution, dispatch/child and timer relationships, actual delivery/admission/disposition, retry/recovery/control/alteration, compensation and metadata-only value changes supported by their owners.

An event is derived from actual typed mutations/semantic source data. The catalog declares coverage; an unavailable owner/source is not replaced by guessed generic events. Use known actor/source provenance only. Value evidence is referenced under existing policy, not copied wholesale.

InitialTriggerClaim named ActivityAttemptClaimed is selected consumption plus sibling retirement. RedeliveryClaim is an attempt without another consumption. BookmarkConsumed for terminal cleanup does not imply a new continuation. Passive expiry emits no event. Tests must exercise all four distinctions.

## Ingress observations

Receipt/admission/disposition can have an owned durable observation boundary independent from target workflow execution. Retain that provenance and stable observation identity. Where a target is known and authorized, record actual rejected/unmatched/duplicate outcomes through the existing delivery path; no arbitrary run association or cross-store atomicity claim. Source metadata first seen at a later checkpoint cannot be backdated as an earlier durable receipt.

An ingress observation transaction shares the same scoped head CAS/immutable-event identity rules. It verifies exact retained run/scope ownership and serializes against run deletion, so a late observation cannot recreate deleted history. It cannot advance last-covered-runtime-checkpoint, change Required capture policy or assert runtime capture completeness. Contention with a runtime checkpoint is a narrow allocation retry, never authority to bypass that checkpoint's fence.

## Retention and compatibility

Additive Runtime migrations preserve the historical Initial chain on all four providers. Old no-history markers remain replay-compatible. Run deletion removes all owned projections in its existing transaction. Explicit earlier purge retains an honest lower/coverage boundary. Sensitive policy applies before either semantic events or recoverable responsibility is persisted.
