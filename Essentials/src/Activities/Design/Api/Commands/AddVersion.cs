using System.Text.Json;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Core.Models;
using Elsa.Mediator.Core.Contracts;

namespace Elsa.Activities.Design.Api.Commands;

public sealed record AddVersion(
    string OperationKey,
    string DefinitionId,
    string Version,
    string ProviderKey,
    string ProviderSchemaVersion,
    string ConsumerKey,
    string ConsumerSchemaVersion,
    JsonElement DescriptorPayload,
    IEnumerable<InputDefinition>? Inputs,
    IEnumerable<OutputDefinition>? Outputs,
    IEnumerable<ActivityDesignFacet>? DesignFacets,
    ActivityExecutionType? ExecutionType
)
: ICommand<ActivityDefinitionVersionDetailsView>;
