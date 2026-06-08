using System.Text.Json;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Primitives.Contracts;

namespace Elsa.Activities.Design.Persistence.Core.Services;

/// <summary>
/// Default <see cref="IActivityDefinitionVersionFactory"/> — generates the id, computes the content
/// <see cref="IActivityDefinitionVersion.Hash"/> via <see cref="IActivityDefinitionHasher"/>, and
/// returns a read-model. No EF entity is built here (the persistence layer's <c>From</c> does that).
/// </summary>
public sealed class ActivityDefinitionVersionFactory(
    IIdentityGenerator identityGenerator,
    IActivityDefinitionHasher hasher) : IActivityDefinitionVersionFactory
{
    public IActivityDefinitionVersion Create(
        IActivityDefinition definition,
        string version,
        string descriptorType,
        JsonElement descriptorPayload,
        string sourceKind,
        string sourceId,
        IEnumerable<InputDefinition> inputs,
        IEnumerable<OutputDefinition> outputs,
        IEnumerable<ActivityPortDefinition> ports,
        ActivityExecutionType executionType = ActivityExecutionType.Action,
        string? id = null)
    {
        var model = new ActivityDefinitionVersionModel(
            Id: string.IsNullOrWhiteSpace(id) ? identityGenerator.Generate() : id,
            Version: version,
            DefinitionId: definition.Id,
            DescriptorType: descriptorType,
            DescriptorPayload: descriptorPayload,
            SourceKind: sourceKind,
            SourceId: sourceId,
            Definition: definition,
            Inputs: inputs,
            Outputs: outputs,
            Ports: ports,
            ExecutionType: executionType,
            Hash: string.Empty);

        // Hash excludes Id/Hash/provenance, so computing it from the (hash-less) model is stable.
        return model with { Hash = hasher.Hash(definition, model) };
    }
}
