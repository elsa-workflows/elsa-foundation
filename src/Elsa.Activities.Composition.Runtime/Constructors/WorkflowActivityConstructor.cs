using System.Text.Json;
using Elsa.Activities.Composition.Runtime.Activities;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Primitives.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Composition.Runtime.Constructors;

/// <summary>
/// Constructs workflow-backed activities. The descriptor is <see cref="WorkflowIdentity"/>. Produces a
/// single well-known <see cref="WorkflowDefinitionActivity"/> for every workflow-backed row, configured
/// with the identity (applied as typed state) and the author inputs/outputs pre-set into the activity's
/// dynamic bag (the workflow's surfaced I/O is dynamic, not typed properties). Runtime-side, no Design
/// dependency. Does its own bag-filling — it does NOT use the CLR kind's binder (that would be a
/// feature → feature reference).
/// </summary>
public sealed class WorkflowActivityConstructor(IServiceProvider serviceProvider)
    : IActivityConstructor<WorkflowIdentity>
{
    public string DescriptorType => typeof(WorkflowIdentity).FullName!;

    ValueTask<IActivity> IActivityConstructor.Construct(
        JsonElement payload,
        IDictionary<string, InputArgument>? inputs,
        IDictionary<string, OutputArgument>? outputs,
        CancellationToken cancellationToken)
        => Construct(payload.Deserialize<WorkflowIdentity>()!, inputs, outputs, cancellationToken);

    public ValueTask<IActivity> Construct(
        WorkflowIdentity descriptor,
        IDictionary<string, InputArgument>? inputs,
        IDictionary<string, OutputArgument>? outputs,
        CancellationToken cancellationToken)
    {
        var activity = ActivatorUtilities.CreateInstance<WorkflowDefinitionActivity>(serviceProvider);
        activity.WorkflowIdentity = descriptor;

        if (inputs is not null)
            foreach (var (key, value) in inputs)
                activity.SyntheticProperties[key] = value;

        if (outputs is not null)
            foreach (var (key, value) in outputs)
                activity.SyntheticProperties[key] = value;

        return new(activity);
    }
}
