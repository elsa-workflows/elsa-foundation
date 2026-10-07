# Internal Contract: Durable-Value Page Reuse

This document defines Runtime Services implementation behavior. The memo implementation is `public sealed` for direct unit testing under framework §2.23.3; retained entries remain private. It does not add a provider cache capability interface or alter a public store API, paging model, persisted contract, or checkpoint durability rule.

## Eligibility and fallback

Reuse is considered only when all of these conditions hold:

1. The host selected Coalesced checkpoint persistence and the session's `CoalesceDurableValueReads` option is enabled.
2. The current call is owned by the active coalescing session for the same workflow execution.
3. Before decoration, existing runtime backend ownership metadata identified one unambiguous `EntityFramework` backend owning the current durable-value contract and effective underlying registrations.
4. The current context is a single ordinary persistence scope, and the resolved continuation codec is the built-in sealed HMAC codec from a proven effective singleton composition shared with the inner EF store. Custom, transient/scoped or unprovable codec compositions bypass reuse.
5. The current owner is not itself suspended by a nested owner, disposed, deactivated, writing, or otherwise outside the cache lifetime. A legitimate child owner may use its own independent empty memo; entering that child permanently disables the parent's memo for the remainder of the parent scope.

If any condition cannot be established, call the existing inner store and preserve its result, exception, rejection, or cancellation behavior. Missing, ambiguous, in-memory, custom, or overridden backend registrations bypass reuse; eligibility uncertainty must not become a new startup error. Backend ownership metadata is trusted host-composition metadata, not a security attestation. The current access check remains authoritative on every call.

## Read behavior

For an eligible request, capture the current access context and exact `DurableValueStatePageQuery` fields. The key includes scope, access policy, purpose, across-scopes flag, workflow execution ID, requested limit, exact opaque input continuation, and reference identity of the built-in codec instance. Do not inspect token contents or duplicate EF cursor-purpose/binding logic in Runtime Services.

Before lookup, keep cancellation, identity validation, current scope authorization, active owner, and execution-match checks live. For a miss, invoke the inner EF store. Admit only a successful complete page after the call returns and only if generation, active write count, owner, context, and codec still match the captured values. A page from an invalid/non-null cursor cannot be cached because the inner store validates before returning; failed validations are never entries. On a hit, the same codec instance and complete request key prove only that this exact cursor/request succeeded earlier under the same stable built-in codec. Custom codecs, different codec objects, or contexts that are no longer eligible bypass the memo.

Return a detached copy of the successful raw provider page. The existing coalescing page merger must run on every logical read and merge the then-current staged upserts and tombstones. This keeps staged changes visible and avoids stale merged pages.

## Write and ownership behavior

- Logical `BufferDeferred` does not write to the provider and does not invalidate the raw baseline.
- Every actual inner checkpoint commit and direct durable-value `SaveAsync`/`DeleteAsync` is a freshness boundary. Fence and clear before the attempt and again in `finally`.
- During an active write, bypass cache hits and fills. Count overlapping writes. A read already in flight can return its normal provider result but cannot refill a newer generation.
- A failed or cancelled write attempt ends reuse admission for the rest of that session. For a healthy memo, a successful boundary clears the old generation and starts a fresh segment budget. Cache fencing does not promise stronger isolation than the provider supplies and does not serialize provider reads with writes.
- A nested owner suspends the parent memo for the rest of its scope. A valid child owner starts empty. Disposal, deactivation, drain cancellation/interruption, and recovery clear cache state without changing existing overlay or session cancellation semantics.

## Bound and fallback behavior

Initial internal session-local limits for the active generation are 32 pages, 1,024 rows, and 4 MiB of deterministically accounted retained content. The estimator covers key and cursor strings, every durable-value field and metadata entry (including external-reference metadata), JSON payload bytes, and fixed entry/row overhead, using checked arithmetic before cloning. A successful boundary clears the old generation before a fresh bounded segment starts, keeping resident memo state within the same caps. These are conservative engineering guardrails, not exact heap accounting or benchmark-derived optima.

When a limit would be exceeded, return the complete provider result unchanged, clear the memo, and stop admissions until the next successful boundary/reset. Never truncate a result or populate a partial entry. Successful empty pages can be reused. Errors and cancelled/rejected reads are not cached.

## Configuration

Expose one `CoalesceDurableValueReads` boolean on the existing coalescing options and shell feature, defaulting to `true`. It is effective only for eligible Coalesced EF composition. Setting it to `false` disables page reuse while preserving the same coalescing cadence and other persistence behavior. Immediate mode remains unaffected. If a per-session authored checkpoint cap creates new options, copy both this flag and the existing inspection-read flag.

## Required proof

The enabled and disabled test runs must use identical Coalesced settings and an actual eligible first-party EF composition. A deterministic non-empty normal typed-start → deferred `ActivityStarted` → invoke flow must show fewer backing durable-value page calls with reuse enabled and byte-equivalent input values, identity, visibility, and result. Unit tests may inject an explicitly eligible fake store for focused lifecycle/clone/failure cases, but those tests do not establish host eligibility or normal-path reduction.

The proof must also retain overlay, identity/access, cursor rejection, codec-instance change, write success/failure/cancellation, read-during-write, late-fill, nesting, disposal, recovery, bounds, and authored-cap-option behavior. Existing C4 concurrency, recovery, partition, and provider gates remain applicable. The rebuilt PostgreSQL HTTP reference must return `200` and `Alice Smith`; the existing valid REST regression must retain its expected response and terminal state. Bounded latency comparison belongs to T17/T18; this contract does not promise speedup.
