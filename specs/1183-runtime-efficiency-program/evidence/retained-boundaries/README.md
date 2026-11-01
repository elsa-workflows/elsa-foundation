# Retained-boundary process witness

This bounded qualification fixture exercises real SQLite persistence, native Event suspension, pinned DispatchWorkflow,
recovery sweeps and actual bookmark resume. It is evidence for #2828 under Runtime Efficiency #2531. It is not a capacity
benchmark or a timing CI gate. The production engine and persistence provider are project references; no runtime behavior
is replaced by the fixture.

From the repository root, on a POSIX host with .NET 10 and Python 3:

```sh
dotnet build specs/1183-runtime-efficiency-program/evidence/retained-boundaries/Worker.csproj
python3 specs/1183-runtime-efficiency-program/evidence/retained-boundaries/controls.py /tmp/elsa-boundary-controls
python3 specs/1183-runtime-efficiency-program/evidence/retained-boundaries/crashes.py /tmp/elsa-boundary-crashes
python3 specs/1183-runtime-efficiency-program/evidence/retained-boundaries/takeovers.py /tmp/elsa-boundary-takeovers
python3 specs/1183-runtime-efficiency-program/evidence/retained-boundaries/crashes.py /tmp/elsa-parent-crashes --parent
python3 specs/1183-runtime-efficiency-program/evidence/retained-boundaries/takeovers.py /tmp/elsa-parent-takeovers --parent
```

Use fresh output directories. Each controller retains configurations, logs, JSON results, calls and databases there.
Without a directory argument, it creates a private temporary directory. Build through the normal machine build-slot
wrapper. The controllers launch the built DLL directly and only signal child process groups they created. The bounded
waits detect a missing fixture gate or recovery outcome; their durations do not qualify performance.

The six controls complete both shapes in Durable, Balanced and Ephemeral. The crash controller physically kills its
worker before a boundary transaction commits, immediately after that commit, after the coalescing store has advanced
its queue, or after the third replay-safe work body. A new process reads the same database and recovers it. It checks
one logical activity state per node, completion, no queued work, undelivered outbox or incident, preservation of every
previously completed activity identity, and no re-execution of completed probe bodies. Uncommitted replay-safe work may
run again, as the persistence modes allow.

With `--parent`, another 16 crash cases target the actual physical `ActivityInspectionCaptured` parent continuation
decision and the following body, in Durable and Balanced with an explicit cap of one. Ephemeral has no intermediate
activity cap, so these particular cap-triggered parent checkpoints do not arise there. The after-advance gate requires
an observed physical commit; an earlier deferred checkpoint cannot satisfy it. Four additional takeovers pause A
after that parent decision has committed and advanced its queue.

The six original takeover cases leave A paused after that body while B recovers and completes the workflow, then release A.
B's clock is offset by two hours to expire A's one-hour lease without waiting an hour. A must be rejected by the real
fence and must leave the workflow, activity, incident, scheduler and outbox rows byte-for-byte unchanged. A's uncaught
stale-fence exception gives the child a nonzero exit; it is retained as expected rejection evidence, not a successful
workflow run. This tests fresh processes on one machine; it does not qualify independent hosts or production clock skew. A resumed stale owner can reach ReplaySafe code
before its next physical fence; only its durable writes are rejected. The body witness distinguishes that stale owner
from the fresh recovery process and makes no exactly-once external-effect claim.

The EF transaction interceptor observes only the target checkpoint marker. The after-advance wrapper forwards every
checkpoint-store capability and pauses only after the real coalescing store returns. Recovery never republishes the
create-only source reference. The UUIDv7 production generator replaces the behavioral harness's deterministic generator.
The counting activity is deliberately ReplaySafe; this fixture makes no exactly-once promise for external side effects.

`observed-results.json` records the macOS run of this fixture, with fixture and production source hashes.
Subsequent runs have their own result ledgers; an old receipt does not qualify a changed head. The full program report names the remaining
provider and independent-host acceptance.
