# Research: Variable lifecycle model

Phase 0 for S-10. Each item is a decision, its rationale, and the alternatives considered. R-7 and R-9 resolve the
spec's two *Open questions* (A and B); they are empirical and the rationale states how the plan's tasks settle them.

## R-1 — Per-variable version vs. frame revision

**Decision**: add a per-variable version counter and value digest to `VariableFrameState`, beside the existing frame
revision. **Rationale**: the frame revision counts writes to *every* variable in the frame, so using it for conflict
detection reports conflicts between unrelated variables — Sipke's R4 ("conflict handling must concern the variable in
question"). A per-variable counter gives the write history its order for free. **Alternatives**: reuse the frame
revision (rejected: false conflicts); a global sequence only (rejected: cannot tell which variable moved).

## R-2 — Digest at write time, stored, not hashed on read

**Decision**: compute the digest once per applied write, over ADR 0091's canonical form, and store it with the version;
a read records the two stored values (version, digest) and hashes nothing. **Rationale**: the commit rule only needs to
compare two stored digests, and only on a version mismatch — never serialize a value it did not just write (ADR 0092
decision 5 *Why*). The canonical form compares meaning, so different escaping/notation of the same value match.
**Alternatives**: value comparison at every check (ADR 0090 §7 as proposed) — one serialization per checked variable,
rejected on cost.

## R-3 — Read set shape and lifetime

**Decision**: two parts — an input read set (committed with the input snapshot, carried to background admission) and
session reads (per attempt, dropped on a new attempt). Each entry is (variable ref, version, digest). Only actual,
instrumented reads; a visible-but-unread variable is excluded. **Rationale**: recording only actual reads avoids false
conflicts (a blind write to an unread variable is not a lost update); observing reads at run time covers computed names
without static analysis (ADR 0092 decision 3; Sipke follow-up 8). **Alternatives**: static read sets (rejected: computed
names make them "everything"); tracking all visible variables (rejected: false conflicts).

## R-4 — Base representation and the "older dependency" rule

**Decision**: a write's base is the read-set entry it was built on; input bases are immutable per invocation; a newer
session read never erases an older input dependency still in use; the invocation's own writes advance their own base,
persisted with the invocation. Where provenance is ambiguous, bind to the *oldest in-use* dependency. **Rationale**:
ADR 0092 decision 4 (as #3778 clarifies) requires preserving a dependency still relied on — e.g. a capture computed from
the original input must write against that input even after a later session read saw a newer value. The oldest-in-use
tie-break is the conservative direction: a false conflict is visible and overridable (decision 6), a missed one is
silent. This is a spec engineering determination with no ADR position. **Alternatives**: a single advancing "current
base" per variable per invocation (rejected: loses the older dependency); full per-write provenance graph (rejected:
cost without a case requiring it).

## R-5 — One commit rule at every write site

**Decision**: evaluate one `VariableCommitRule` at the capture, intrinsic, session and alteration write sites, identical
for frames and rows, inline and background. Blind → apply (D-8, last commit wins); base==current → apply; base old +
digest equal → apply + record the version mismatch (`0→1→0`); base old + digest differ → lost update. **Rationale**:
ADR 0092 decision 5; one rule, one place per site, avoids drift between surfaces (decision 1). **Alternatives**: a rule
per surface (rejected: holes between surfaces — the root cause A-2 found).

## R-6 — Fault default, LastWins override, existing incident path

**Decision**: default `Fault` with a diagnostic (variable, base version, current version, writers); `LastWins` per
activity type (attribute) and per node (setting), always recorded; both route through the existing incident strategy and
record the rejected completion. **Rationale**: a lost update is almost never intended, so stopping before the wrong value
spreads is safest (Sipke's position, agreed by Joey — reviewer Q3 **decided**). `LastWins` is the documented escape for
intended overwrites (aggregators); nothing silent. **Alternatives**: strict-only (decided against: forces intended
overwrites into incident handling — reconsider only when real `Merge`/`Reduce` operation semantics exist); "warn and
continue" (rejected: the write history already makes `LastWins` visible).

## R-7 — No-inline-gap for `External` leaves (Open question A)

**Decision (hypothesis to prove)**: extend the single-work-item dispatch (Schedule → Start → Invoke) that
`ReplaySafeFusionDriver` already runs for `ReplaySafe` leaves to `External` leaves, keeping the `ActivityAttemptClaimed`
claim/flush (ADR 0032 R2, spec 095 FR-019) as **intermediate commits within the one work item**, so no *other branch's*
work item interleaves and no database transaction wraps user code. **Rationale**: ADR 0092 decision 10's requirement is
no *inter-branch* interleaving, not a single commit; the ADR explicitly allows several commits within one work item. A
proof-of-concept task builds this and asserts: (a) no other branch's work item runs between input materialization and
transition commit for an `External` leaf; (b) the claim, ownership fencing and durability checkpoints still occur. **If
the PoC cannot achieve this without weakening R2**, that is a finding posted on #3411 that reopens decision 10 — the
spec does not silently diverge. **Alternatives**: defer the claim flush past the body (risk: weakens fencing/durability,
reopens R2); warn authors about the gap (rejected by the ADR: trains authors to ignore warnings).

## R-8 — Background-admission refresh as a new evaluation (D-9)

**Decision**: on admission, compare the input read set to current versions; a changed input triggers a new *evaluation*
of the same invocation (same `InvocationId`, own sequence number, idempotency keys derived from the evaluation),
opt-out per node, both snapshots retained and linked with the reason, the worker's snapshot immutable thereafter; covers
only the Start→admission gap. **Rationale**: prevents foreseeable lost-update faults; input evaluation may call effectful
functions (D-5), so the new-evaluation identity makes the repeat explicit and a node can refuse it (ADR 0092 decision 9;
reviewer Q6). **Alternatives**: silently re-read on the same identity (rejected: hides repeated effects); refresh during
the worker run too (rejected: covered by the commit rule, decision 5).

## R-9 — Write-history physical storage (Open question B)

**Decision (measured)**: mandatory durable metadata per applied write and conflict is always recorded; full values are
opt-in with retention; operational base/rejected-completion data is retained while an enabled merge or recovery needs
it. The **physical** shape — extra rows vs. derived from checkpoint commits — is chosen by a measurement task against a
large-payload, hot-loop benchmark under ADR 0032, under the invariant that every intermediate write (incl. `0→1→0`
within one buffered segment) is preserved. **Rationale**: ADR 0092 decision 13 fixes the invariants and delegates the
shape to measurement (reviewer Q12). **Alternatives**: always extra rows (may cost too much on hot loops); derive from
the final folded state only (rejected by the ADR: loses intermediate writes). This is not ADR 0084's commit journal.

## R-10 — Compatibility and versioning

**Decision**: increment the document/schema version for `VariableFrameState` (adds per-variable version/digest) and for
the write-history record; upcast old records (missing version → 0 / no base; a blind-write path always applies).
**Rationale**: §E2.6.1 — executables and instances written before the change stay runnable. **Alternatives**: in-place
reinterpret (rejected: ambiguous base for old records).

## Resolved unknowns

- Reviewer Q3 (strict-only vs. Fault+LastWins) → **decided**: Fault+LastWins (R-6).
- Reviewer Q8 (suspended keeps base) → **agreed**: keeps base, no resume refresh (plan P-9).
- Open question A (no-gap for `External`) → hypothesis + PoC (R-7); result flows back to #3411.
- Open question B (write-history storage) → measured (R-9); invariants fixed.
