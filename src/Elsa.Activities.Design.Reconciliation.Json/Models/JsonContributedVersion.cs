using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;

namespace Elsa.Activities.Design.Reconciliation.Json.Models;

/// <summary>
/// Read-only <see cref="IActivityDefinitionVersion"/> contribution produced by the JSON
/// source. Id stays empty so the reconciler generates one on first creation.
/// </summary>
internal sealed record JsonContributedVersion(
    int Version,
    string ActivityTypeKey,
    string ImplementationKind,
    IImplementationDescriptor ImplementationDescriptor,
    ActivityExecutionType ExecutionType,
    IEnumerable<InputDefinition> Inputs,
    IEnumerable<OutputDefinition> Outputs,
    IEnumerable<ActivityPortDefinition> Ports,
    IActivityDefinition Definition
) : IActivityDefinitionVersion
{
    public string Id => string.Empty;
    public string DefinitionId => string.Empty;

    /// <summary>
    /// Contributed candidates do not know their persisted hash; the reconciler computes
    /// it from the candidate's content via <c>IActivityDefinitionHasher</c> and writes
    /// it onto the persisted <c>ActivityDefinitionVersion</c> at creation time. Under
    /// Model X the persisted hash is immutable, so this null is correct contribution-side.
    /// </summary>
    public string? ReconcilliationHash => null;
}
