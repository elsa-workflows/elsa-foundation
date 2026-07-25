# Incident Strategy Extension Contract

## Registration surface

```csharp
IServiceCollection AddIncidentStrategy<TStrategy>(
    this IServiceCollection services,
    IncidentStrategyDescriptor descriptor)
    where TStrategy : class, IIncidentStrategy;

IServiceCollection AddIncidentStrategy<TStrategy>(
    this IServiceCollection services)
    where TStrategy : class, IIncidentStrategy;
```

Both overloads register the implementation in workflow scope and contribute descriptor + service
identity atomically. Startup validates identity, descriptor, attribute shape, duplicates, built-in
reservations, default selection, and strategy-safe intent registrations.

## Strategy

```csharp
public interface IIncidentStrategy
{
    ValueTask<IIncidentResolutionAction> ResolveAsync(
        IncidentStrategyContext context,
        CancellationToken cancellationToken);
}
```

The non-null return contract is enforced at runtime. Implementations are replay-safe policy
evaluators and receive no mutable runtime services.

## Action

```csharp
public interface IIncidentResolutionAction
{
    string Kind { get; }

    ValueTask ExecuteAsync(
        IncidentResolutionActionContext context,
        CancellationToken cancellationToken);
}
```

The runtime executes the returned object directly. `Kind` is validated/persisted as classification
only and is never used to locate or recreate an action.

## Public action capabilities

The concrete context API will expose verbs, not mutable entities:

- keep target incident Blocking;
- make target incident Open;
- resolve target incident with meaningful custom semantics;
- request containing workflow Faulted;
- add bounded safe outcome metadata;
- add an explicitly registered strategy-safe post-commit intent.

Every call stages into an incident-local buffer. The context exposes no general stores, setters,
service provider, checkpoint committer, scheduler, activity mutation, workflow retry/suspend/
complete/cancel, cross-incident lookup, absorption, or suppression.

Strategy-visible incident metadata is restricted to an explicit set of correlation and failure-
classification keys. Exception messages, stack traces, payloads, variables, and private runtime
state are never projected. Strategy-owned outcome and intent metadata accepts at most 32 nonblank,
control-character-free string pairs, with 128-character keys and 1024-character values.

## Built-in public action factories

Runtime Core exposes stable ways to return:

- FaultWorkflow
- ContinueWithIncidents
- WaitForIntervention

AbsorbFault and SuppressIncident are internal runtime operations and cannot be constructed by
third-party strategies.

## Failure and cancellation

- Null return, Resolve exception, or Execute exception: discard that incident's stage and execute a
  new runtime-owned FaultWorkflow action.
- `OperationCanceledException` when the supplied token is cancelled: propagate and abort the entire
  resolution checkpoint.
- `OperationCanceledException` when the supplied token is not cancelled: ordinary failure fallback.
- Fallback or checkpoint failure: no outcome is manufactured; durable Blocking + null outcome remains
  authoritative.
