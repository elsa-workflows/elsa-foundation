# StateMachine composition spike

Run from `prototypes/kernel-model`:

```sh
dotnet build models/Statecharts.Run -c Release
dotnet models/Statecharts.Run/bin/Release/net10.0/Kernel.Statecharts.Run.dll
```

The runner sends frame-addressed business inputs through the simulation's real inbox. A transition from a compound state enters two parallel regions. One entry runs a normal native Event activity. A second signal remains in the inbox until that entry resumes and then transitions both regions. A denied guard and an unknown event each produce an unhandled outcome. A final transition exits both regions and returns control to the enclosing Flowchart when nested.

The twenty schedules cross root and nested placement, Balanced and Durable modes, and five cuts: no failure, failed commit before applying it, applied commit with a lost acknowledgement, crash after holding a signal, and overlapping owners with a stale fenced write. Each simulation action runs the kernel's `World.Check()`. The witness reads committed configurations, journal entries and the outstanding inbox row independently of the statechart plan. It checks event serialization (S1), one handled or unhandled outcome per accepted signal (S2), one active state per region (S3), and the exit/transition/entry barriers and hierarchy order (S4).

The original spike's four historical controls rejected accepting an input during pending work, a false handled outcome, retaining exited states and parent-first exits. Those runs do not qualify the later phase-progress implementation.

The engine takes no dependency on the kernel. The native composite stores its settled configuration, selected reaction and per-step prerequisites/completion/ordinary child frames. Independent regional work can run concurrently within a phase. Child-first exits and parent-first entries order each hierarchy chain; all exits finish before actions and all actions before entries. There is no global depth barrier between regions. Sender-facing inputs target an exact live frame. A stale or unsupported frame receives a rejection entry. Held signals wake when deliverable; a waiting child does not cause a continuation loop.

## Phase concurrency witness

Use `--phases-list` to discover56 cases and `--phases` to execute them. Forty-eight normal workflow schedules cross both modes, root/Flowchart placement, Exit/Action/Entry, regional completion order and one/two waiting children. They use ordinary Sequence/Event/value activities at unequal hierarchy depths, observe actual registered event deliveries, and reload under a new owner while phase work waits. Both children may remain waiting across reload; completing one cannot advance the phase or lose the other's wait. Additional parent entry/exit waits expose hierarchy ordering. Completed writes run once in the committed history, and ordinary shared values survive the controlled completion orders. These are selected business-input reactions, not publication-cohort proof.

Eight literal checkpoint-reader fixtures are listed separately from those workflows. Version2 progress preserves the serial format's completed prefix and exact pending child; unknown versions, invalid ownership indices and mixed shapes are refused. These fixtures are not historical-writer/binary or product-migration evidence. The sibling assembly grant is solely for this focused reader witness.

## General structural planner

`--selection-list` discovers 68 logical graph/visit fixtures and `--selection` runs them. The private compiled engine supports hierarchical and regional conflicts, action-without-exit, explicit self/parent re-entry, compound-parent preservation, whole-parallel re-entry, targeted/default regional entry and nested finals. It validates complete entered configurations, rejects activity work on structural roots and regions, and returns exited/source visit identities and structural phase dependencies. Input matching and guard results are supplied by the host; the engine does not admit inputs, discover cohorts, allocate physical visits or own values.

The existing public `Chart` constructors adapt the legacy sibling-only definition to that same engine; there is no second selector. The original20 and phase56 native schedules exercise this adapter. Full-graph fixtures are logical data, not actual native state-local scopes or support through a new public authored schema. That bridge remains mandatory before full hierarchy/product claims.

`--selection-export` produces JSON for the [Node reference comparison](../../../../specs/1210-state-machine/selection-model/README.md). Twenty-eight common profiles compare candidate/selected sets, exited visits, source visits and phase dependencies. Two explicitly proposed child-to-ancestor re-entry cases are exercised only by the private .NET profile and excluded from that comparison. This is neither full SCXML compliance nor production architecture approval.

## Limits

The witness covers business inputs, value-equality guards, and transitions between distinct sibling states in one region, including compound siblings. The top-level final leaf completes the native container. Other transition topologies are rejected when the chart is constructed. Automatic/completion transitions, full SCXML, suspended guard evaluation, a public admission API, retention, activity-fault recovery and successful-sibling preservation after fault are outside the witness. `NativeModel.Fail` still owns generic fault propagation/cancellation; this change adds no recovery mechanism. The trace reports committed state and the in-flight plan separately. These experiments supply evidence for #3815's M0 and do not settle placement, source-wave closure, provider or product/Studio adoption.

The native smoke run passed. The native cost tables for SQLite and PostgreSQL 17, with both fences, matched the baseline at `e5f3955adc` byte for byte. Those cost runs exercise the existing native workloads. Database costs of a statechart workflow and randomly generated statecharts have not been measured.
