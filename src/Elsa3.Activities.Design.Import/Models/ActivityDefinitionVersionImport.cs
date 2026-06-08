using System.Text.Json;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;

namespace Elsa3.Mapping.Models;

public sealed record ActivityDefinitionVersionImport(
    string Id,
    string Version,
    string DefinitionId,
    string ActivityTypeKey,
    string DescriptorType,
    JsonElement DescriptorPayload,
    IActivityDefinition Definition,
    IEnumerable<InputDefinition> Inputs,
    IEnumerable<OutputDefinition> Outputs,
    IEnumerable<ActivityPortDefinition> Ports,
    ActivityExecutionType ExecutionType
)
: IActivityDefinitionVersion
{
    /// <summary>
    /// Imported Elsa-3 versions do not carry a Model-X content hash; the reconciler
    /// computes it at creation time on the Elsa-4 side. Under Model X the persisted
    /// hash is immutable, so this null is correct on the import surface.
    /// </summary>
    public string? ReconcilliationHash => null;
}
