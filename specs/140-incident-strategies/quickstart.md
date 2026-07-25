# Quickstart: Add and Select an Incident Strategy

## Define a strategy

```csharp
[IncidentStrategy(
    alias: "Acme.Operations.Review",
    version: "1",
    DisplayName = "Request operations review",
    Description = "Keeps the incident blocking until an operator resolves it.")]
public sealed class ReviewIncidentStrategy : IIncidentStrategy
{
    public ValueTask<IIncidentResolutionAction> ResolveAsync(
        IncidentStrategyContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<IIncidentResolutionAction>(
            new ReviewIncidentAction());
    }
}

public sealed class ReviewIncidentAction : IIncidentResolutionAction
{
    public string Kind => "Acme.Operations.RequestReview";

    public ValueTask ExecuteAsync(
        IncidentResolutionActionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        context.KeepIncidentBlocking();
        context.AddMetadata("queue", "operations");
        context.AddPostCommitIntent(new IncidentStrategySafePostCommitIntent(
            "Acme.Operations.Notify",
            JsonSerializer.SerializeToElement(new { reason = "incident-review" })));
        return ValueTask.CompletedTask;
    }
}
```

The returned decision is an executable object, analogous to an MVC action result. Its dotted
`Kind` is durable classification only; Runtime calls the object directly through the guarded
`IncidentResolutionActionContext`.

## Register it

```csharp
services.AddIncidentStrategy<ReviewIncidentStrategy>();
services.AddIncidentStrategySafeIntent(
    new IncidentStrategySafeIntentDescriptor("Acme.Operations.Notify"));
services.AddRuntimePostCommitIntentHandler<NotifyOperationsHandler>("Acme.Operations.Notify");
```

The reflection overload reads one non-inherited `IncidentStrategyAttribute`. Durable alias/version
are mandatory and are never derived from the CLR type. For generated or dynamic metadata, use:

```csharp
services.AddIncidentStrategy<ReviewIncidentStrategy>(
    new IncidentStrategyDescriptor(
        new IncidentStrategyReference("Acme.Operations.Review", "1"),
        "Request operations review",
        "Keeps the incident blocking until an operator resolves it.")));
```

Duplicate identities, invalid custom aliases, and attempts to claim built-in aliases fail host
activation.

## Configure the publishing default

Configure the host default as an exact reference. If no default is configured, publishing uses
`Fault/1`.

```csharp
services.AddSingleton(new IncidentStrategyCatalogOptions
{
    DefaultStrategy = new IncidentStrategyReference("Acme.Operations.Review", "1")
});
services.AddWorkflowRuntime();
```

Authored workflow JSON may override it:

```json
{
  "strategyOptions": {
    "incidentStrategy": {
      "alias": "Acme.Operations.Review",
      "version": "1"
    }
  }
}
```

If `incidentStrategy` is absent/null, Design retains null and publication resolves the host default.
The exact effective reference is pinned into the executable and remains stable if host configuration
changes later.

## Discover strategies

Call the permission-protected publishing endpoint:

```http
GET /publishing/incident-strategies
```

The response lists deterministic descriptor metadata plus the effective publishing default. The
endpoint does not construct strategy implementations.

## Operational semantics

- `Fault/1`: leaves the incident Blocking and faults the workflow.
- `ContinueWithIncidents/1`: leaves the activity Faulted, opens the incident, and permits already
  scheduled independent work to continue.
- `WaitForIntervention`: deliberate no-progress action; leaves the incident Blocking and preserves
  workflow lifecycle without retry.
- Strategy/action failure: runtime substitutes a fresh Fault action and records safe
  `IncidentStrategyFailure` provenance.

Actions may not retry/suspend/complete/cancel workflows, mutate activity state, absorb/suppress
structural incidents, or enqueue core scheduler/dispatch/retry intents.
