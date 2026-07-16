using System.Text.Json;
using Elsa.Activities.Design.Core.Models;

namespace Elsa.Activities.Design.Api.Models;

public sealed record ActivityDefinitionVersionDetailsView(
    string Id,
    string Version,
    string ProviderKey,
    string ProviderSchemaVersion,
    string ConsumerKey,
    string ConsumerSchemaVersion,
    JsonElement DescriptorPayload,
    ActivityDefinitionView Definition,
    IEnumerable<InputDefinition>? Inputs,
    IEnumerable<OutputDefinition>? Outputs,
    IEnumerable<ActivityDesignFacet>? DesignFacets,
    ActivityExecutionType ExecutionType
);
