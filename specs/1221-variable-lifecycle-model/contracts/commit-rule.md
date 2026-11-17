# Contract: the variable commit rule

The behavioral contract every write site honors. It is internal engine behavior, not a public API; the "contract" is the
guaranteed outcome of a write, stated so tests can pin it.

## Inputs to a commit of a write to variable X

- The write's **base** for X: the version from the writer's read set (plus the stored digest for a `Fault` node), or
  *none* (blind write).
- X's **current** version (and, for a `Fault` node on a version mismatch, its stored digest) at commit time.
- The write's **new value** (and its digest).
- The node's **lost-update mode**: default (light tracking) or `Fault` (opt-in by activity type or per node).

## Guaranteed outcomes

| Case | Condition | Outcome |
|---|---|---|
| Blind | no base | Apply. Last commit wins among unordered branches (D-8). Recorded as a blind write. |
| Current | base.version == current.version | Apply. |
| Lost update, default | base.version < current.version | Apply; the last write wins (D-8). Raise the lost-update **warning**: an instance diagnostic in Studio and a write-history entry naming the variable, base and current versions and both writers, keeping the overwritten value so it can be restored. No digest is compared, so `0 → 1 → 0` also warns. |
| Returned value, `Fault` | base.version < current.version AND base.digest == current.digest AND mode == `Fault` | Apply. Record the version mismatch (e.g. `0 → 1 → 0`). |
| Lost update, `Fault` | base.version < current.version AND base.digest != current.digest AND mode == `Fault` | Do not apply pending changes. Fault with a diagnostic (variable, base version, current version, writers). Incident records the rejected completion. |
| No-op | new value digest == current digest, after the above applies the write | Apply nothing; version unchanged. Two branches reading 0 and writing 1 are **not** hidden as a no-op — the second is a lost update per the rows above. |

## Invariants

- Identical for inline and background steps, captures and session writes, frames and rows.
- A read alone never produces a write and never produces a lost update.
- An invocation never conflicts with its own writes: its applied write advances its own base for X.
- Input bases are immutable per invocation; a newer session read never erases an older input dependency in use.
- In the default mode no read set stores a digest and no commit compares one. Under `Fault`, digests are compared only
  on a version mismatch.
- The digest is over ADR 0091's canonical form (meaning, not text).
- The default holds only once the no-inline-gap guarantee is proven for `External` and `ReplaySafe` (Open question A).

## Test hooks (acceptance)

- Parallel inline `External` read-modify-write → `2` (no gap; reproduction fails before with a silent `1`).
- Parallel background read-modify-write → `1` with a lost-update warning naming both writers and keeping the
  overwritten value.
- Two branches read 0, write 1 → second is a lost update (warning), not a no-op.
- `0 → 1 → 0`, default mode → applies with a warning; `Fault` mode → applies, recorded, no fault.
- Real lost update, `Fault` (activity type and node) → fault through the incident path.
- Blind write → applies, never a lost update.
- Invocation writes X on suspend and captures X on complete → no self-conflict.
