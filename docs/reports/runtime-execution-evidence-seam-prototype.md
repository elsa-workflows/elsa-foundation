# Runtime Execution Evidence Seam Prototype

Status: completed disposable prototype; no prototype code retained.

Date: 2026-08-05.

## Question

Can a separately composed Execution Evidence domain record complete evidence work atomically with a
runtime checkpoint, materialize it idempotently after commit, and support both in-memory and
Groundwork runtime persistence without adding evidence-specific concepts to existing Runtime modules?

## Result

Yes. The existing generic checkpoint and outbox extension surfaces are sufficient for the agreed
atomic-intent contract:

1. An `IRuntimeCheckpointCommitEnricher` deterministically derives an opaque evidence intent from the
   checkpoint commit before persistence and fingerprinting.
2. `RuntimeCheckpointCommitter` folds the intent into the generic post-commit outbox state and requires
   the checkpoint store to acknowledge every pending item.
3. An Execution Evidence-owned post-commit intent handler materializes evidence idempotently by its
   deterministic evidence identifier.
4. Failed materialization retries without rolling back committed workflow state; the evidence range
   remains incomplete until delivery succeeds.

The intent—not the materialized query-store row—shares the runtime checkpoint transaction. This is
enough to recover materialization and preserves the one-way domain dependency.

## Verified scenarios

- Two enrichment passes over the same commit produced identical intent identities, payloads, and
  fingerprints; replaying the commit did not conflict.
- A persistence policy that skipped a checkpoint containing the evidence intent returned
  `SkipHasPostCommitWork` and created no commit, outbox item, or evidence.
- A handler that wrote evidence and then simulated a crash left the checkpoint committed, marked the
  delivery retryable, and redelivered without creating duplicate evidence.
- Groundwork atomically acknowledged and exposed an opaque, non-scheduler evidence intent with its
  JSON payload intact through the generic runtime outbox store.

Focused verification while the disposable tests existed:

- Runtime prototype: 3 passed, 0 failed.
- Groundwork prototype: 1 passed, 0 failed.

The Groundwork build emitted pre-existing `SQLitePCLRaw.lib.e_sqlite3` vulnerability warnings. The
prototype introduced no build or test warnings of its own.

## Design constraints carried forward

- Intent and evidence identifiers must be deterministic from stable commit identity and fixed
  discriminators; enrichers must not read clocks, randomness, or mutable external state.
- Payload serialization must be canonical and bounded because it participates in checkpoint replay
  fingerprinting and outbox storage.
- The materializer must be idempotent because a crash can occur after writing evidence but before
  acknowledging outbox delivery.
- The Execution Evidence composition needs an explicit delivery driver for its intent kind; it must
  not assume the in-process scheduler fast path processes non-scheduler intents.
- The prototype does not promise cross-store ACID or exactly-once side effects. Neither is required by
  the agreed contract.
