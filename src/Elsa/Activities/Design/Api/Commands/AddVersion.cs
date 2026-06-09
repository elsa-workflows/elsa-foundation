using System.Text.Json;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Core.Models;
using Elsa.Mediator.Core.Contracts;

namespace Elsa.Activities.Design.Api.Commands;

public sealed record AddVersion(
    string DefinitionId,
    string Version,
    string DescriptorType,
    JsonElement DescriptorPayload,
    IEnumerable<InputDefinition>? Inputs,
    IEnumerable<OutputDefinition>? Outputs,
    IEnumerable<ActivityPortDefinition>? Ports,
    ActivityExecutionType? ExecutionType
)
: ICommand<ActivityDefinitionVersionDetailsView>;
