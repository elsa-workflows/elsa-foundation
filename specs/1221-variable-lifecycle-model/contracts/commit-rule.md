# Contract: the variable commit rule

The behavioral contract every write site honors. It is internal engine behavior, not a public API; the "contract" is the
guaranteed outcome of a write, stated so tests can pin it.

## Inputs to a commit of a write to variable X

- The write's **base** for X: the (version, digest) from the writer's read set, or *none* (blind write).
- X's **current** (version, digest) at commit time.
- The write's **new value** (and its digest).
- The step's **lost-update policy**: `Fault` (default) or `LastWins`.

## Guaranteed outcomes

| Case | Condition | Outcome |
|---|---|---|
| Blind | no base | Apply. Last commit wins among unordered branches (D-8). Recorded as a blind write. |
| Current | base.version == current.version | Apply. |
| Returned value | base.version < current.version AND base.digest == current.digest | Apply. Record the version mismatch (e.g. `0 → 1 → 0`). |
| Lost update, Fault | base.version < current.version AND base.digest != current.digest AND policy == Fault | Do not apply pending changes. Fault with a diagnostic (variable, base version, current version, writers). Incident records the rejected completion. |
| Lost update, LastWins | same, policy == LastWins | Apply. Record as a `LastWins` resolution (never silent). |
| No-op | new value digest == current digest, after the above allows the write | Apply nothing; version unchanged. Two branches reading 0 and writing 1 are **not** hidden as a no-op — the second is a lost update per the rows above. |

## Invariants

- Identical for inline and background steps, captures and session writes, frames and rows.
- A read alone never produces a write and never faults.
- An invocation never conflicts with its own writes: its applied write advances its own base for X.
- Input bases are immutable per invocation; a newer session read never erases an older input dependency in use.
- The digest is over ADR 0091's canonical form (meaning, not text).

## Test hooks (acceptance)

- Parallel `External` read-modify-write → lost update (reproduction; fails before, faults after).
- Two branches read 0, write 1 → second is a lost update, not a no-op.
- `0 → 1 → 0` → applies, recorded, no fault.
- Blind write → applies, never a lost-update fault.
- `LastWins` (type and node) → applies, recorded.
- Invocation writes X on suspend and captures X on complete → no self-conflict.
