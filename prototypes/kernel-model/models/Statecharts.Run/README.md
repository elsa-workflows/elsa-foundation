# StateMachine composition spike

Run from `prototypes/kernel-model`:

```sh
dotnet build models/Statecharts.Run -c Release
dotnet models/Statecharts.Run/bin/Release/net10.0/Kernel.Statecharts.Run.dll
```

The runner sends frame-addressed business inputs through the simulation's real inbox. A transition from a compound state enters two parallel regions. One entry runs a normal native Event activity. A second signal remains in the inbox until that entry resumes and then transitions both regions. A denied guard and an unknown event each produce an unhandled outcome. A final transition exits both regions and returns control to the enclosing Flowchart when nested.

The twenty schedules cross root and nested placement, Balanced and Durable modes, and five cuts: no failure, failed commit before applying it, applied commit with a lost acknowledgement, crash after holding a signal, and overlapping owners with a stale fenced write. Each simulation action runs the kernel's `World.Check()`. The witness reads committed configurations, journal entries and the outstanding inbox row independently of the statechart plan. It checks event serialization (S1), one handled or unhandled outcome per accepted signal (S2), one active state per region (S3), and the exit/transition/entry barriers and hierarchy order (S4).

Four isolated negative controls were run: accepting an event while an action is pending loses its held inbox row; recording an unhandled event as handled fails S2; retaining exited states fails S3; visiting exited parents before their descendants fails S4.

The engine takes no dependency on the kernel. The native composite stores its settled configuration and its selected reaction separately so a suspended child resumes at the recorded cursor. A phase without authored activity work runs synchronously. In this spike independent regions progress serially within each phase. Sender-facing inputs target an exact live frame. A stale or unsupported frame receives a rejection entry. A held signal that becomes deliverable during this turn gets a continuation because held rows alone sleep; while the child still waits there is no continuation loop.

## Limits

The witness covers business inputs, value-equality guards, and transitions between distinct sibling states in one region, including compound siblings. The top-level final leaf completes the native container. Other transition topologies are rejected when the chart is constructed. Automatic/completion transitions, full SCXML, suspended guard evaluation, a public admission API, retention, and activity-fault recovery are outside the witness. The trace reports committed state and the in-flight plan separately. These experiments supply evidence for #3815's M0 and do not settle its placement or admission decisions.

The native smoke run passed. The native cost tables for SQLite and PostgreSQL 17, with both fences, matched the baseline at `e5f3955adc` byte for byte. Those cost runs exercise the existing native workloads. Database costs of a statechart workflow and randomly generated statecharts have not been measured.
