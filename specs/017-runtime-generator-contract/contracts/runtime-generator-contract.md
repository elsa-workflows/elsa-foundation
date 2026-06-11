# Runtime Generator Contract

## Runtime Rules

- A generator is an in-workflow activity, not an external trigger.
- A generator owns a long-lived `ActivityExecution`.
- A generated event is scheduler/history data; it is not itself an `ActivityExecution`.
- Each generator emission creates scheduler work.
- Downstream activities scheduled from an emission still receive their own activity executions through ordinary scheduler work.
- Generator lifetime is tied to the owning execution scope by default.
- Generated event durability is explicit: volatile, durable, or policy controlled.
- Captured generated-event payload data is referenced through durable value boundaries rather than authored/design payload models.

## Contract Surface

- `GeneratorRegistration`
- `GeneratedEvent`
- `SchedulerGeneratedEventWorkItem`
- `SchedulerState.ActiveGenerators`
- `SchedulerState.PendingGeneratedEvents`
- `GeneratorStatus`
- `GeneratorStopPolicy`
- `GeneratorBackpressurePolicy`
- `GeneratedEventDurability`

## Compatibility Boundary

The contract does not expose authored workflow document types, design model namespaces, C# callback method names, or Elsa 3 live instance shapes.
