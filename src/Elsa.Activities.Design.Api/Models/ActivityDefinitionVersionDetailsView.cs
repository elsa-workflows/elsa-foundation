using System.Text.Json;
using Elsa.Activities.Design.Core.Models;

namespace Elsa.Activities.Design.Api.Models;

public sealed record ActivityDefinitionVersionDetailsView(
    string Id,
    string Version,
    string DescriptorType,
    JsonElement DescriptorPayload,
    ActivityDefinitionView Definition,
    IEnumerable<InputDefinition>? Inputs,
    IEnumerable<OutputDefinition>? Outputs,
    IEnumerable<ActivityPortDefinition>? Ports,
    ActivityExecutionType ExecutionType
);
