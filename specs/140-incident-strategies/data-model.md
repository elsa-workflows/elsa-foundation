# Data Model: Extensible Incident Strategies

## IncidentStrategyReference

Dependency-free immutable value in `Elsa.Workflows.Primitives`.

| Field | Type | Rules |
|---|---|---|
| `Alias` | string | Nonblank; ordinal case-insensitive lookup; canonical descriptor spelling persisted |
| `Version` | string | Nonblank opaque token; exact ordinal comparison |

Identity equality uses case-insensitive Alias plus ordinal Version.

## IncidentStrategyDescriptor

Immutable discovery/registration metadata in Runtime Core.

| Field | Type | Rules |
|---|---|---|
| `Reference` | IncidentStrategyReference | Exact registered identity |
| `DisplayName` | string | Nonblank presentation text |
| `Description` | string? | Optional safe presentation text |

Built-in aliases are `Fault` and `ContinueWithIncidents`, version `1`. Third-party aliases must be
dotted/namespaced. Descriptor registration is immutable after startup.

## WorkflowStrategyOptions

`IncidentStrategyType: string?` is deleted and replaced by:

```csharp
public IncidentStrategyReference? IncidentStrategy { get; set; }
```

Null means inherit during publication and remains null in authored Design state.

## WorkflowExecutable

Adds a required effective `IncidentStrategyReference IncidentStrategy` to the JSON constructor and
public immutable surface. Publishing supplies it. Runtime never recomputes it. The reference is part
of the executable behavior hash.

## IncidentResolutionOutcome

Immutable durable evidence stored on `IncidentState`.

```csharp
public sealed record IncidentResolutionOutcome(
    string ActionKind,
    DateTimeOffset AppliedAt,
    IncidentStrategyReference? Strategy,
    string? SystemSource,
    IReadOnlyDictionary<string, string> Metadata);
```

Rules:

- `ActionKind` is nonblank, exact ordinal, and immutable.
- At least one of `Strategy` or `SystemSource` is present.
- Both may be present when a selected strategy is replaced by runtime fallback.
- Metadata keys/values are bounded, safe strings.
- No exception type, message, stack, payload, variable, CLR type, or action object is stored.

## IncidentState

`ResolutionAction: IncidentResolutionAction` is deleted and replaced by
`ResolutionOutcome: IncidentResolutionOutcome?`.

Lifecycle invariants:

| Status | Terminal | `ResolvedAt` | Typical outcome |
|---|---:|---:|---|
| Open | No | null | ContinueWithIncidents |
| Blocking | No | null | null, FaultWorkflow, or WaitForIntervention |
| Resolved | Yes | required | AbsorbFault or custom resolution |
| Suppressed | Yes | required | SuppressIncident |

An outcome is write-once. Resolved/Suppressed cannot reopen or be resolved again. Ordinary strategy
evaluation selects only Blocking ordinary activity-fault incidents with null outcome.

## Stable Action Kinds

- `FaultWorkflow`
- `ContinueWithIncidents`
- `WaitForIntervention`
- `AbsorbFault` (internal)
- `SuppressIncident` (internal)

Custom kinds are dotted/namespaced. Kinds classify outcomes; they do not resolve executable types.

## Stable System Sources

- `StructuralFaultAbsorption`
- `SubtreeCancellation`
- `ActivityActivationFailure`
- `PoisonedSchedulerWork`
- `MissingStrategyImplementation`
- `IncidentStrategyFailure`

Only bounded safe metadata is allowed. Strategy failure metadata may use `phase=Resolve` or
`phase=Execute`.

## Policy-Safe Context

`IncidentStrategyContext` contains immutable snapshots of:

- incident identity, status, severity, failure classification, timestamps, and safe metadata;
- associated activity execution identity/type/state classification;
- workflow execution identity/lifecycle classification;
- executable identity and exact pinned strategy reference.

It excludes raw exceptions, unrestricted incident message, variables, payloads, private state,
services that mutate runtime state, and checkpoint objects.

## Strategy-Safe Post-Commit Intent

Custom action input is only:

- registered namespaced strategy-safe kind;
- validated safe payload/metadata;
- action-local ordinal.

The runtime derives deterministic intent ID, idempotency key, creation time, workflow/activity
correlation, and batch correlation. Core scheduler, dispatch, retry, and runtime-control kinds are
not strategy-safe.

## State Transitions

```text
ordinary fault
  -> Blocking + outcome null
  -> FaultWorkflow: Blocking + outcome, workflow Faulted
  -> ContinueWithIncidents: Open + outcome, workflow preserved
  -> WaitForIntervention: Blocking + outcome, workflow preserved
  -> custom resolution: Resolved + outcome + ResolvedAt

structural parent handling
  -> Resolved + AbsorbFault outcome + ResolvedAt

subtree cancellation/reclamation
  -> Suppressed + SuppressIncident outcome + ResolvedAt
```
