using System.Text.Json;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Core.Models;
using Elsa.Mediator.Core.Contracts;

namespace Elsa.Activities.Design.Api.Commands;

public sealed record AddDefinition(
    string ActivityTypeKey,
    string SourceKind,
    string SourceId,
    string ProviderKey,
    string ProviderSchemaVersion,
    string ConsumerKey,
    string ConsumerSchemaVersion,
    JsonElement DescriptorPayload,
    string Category,
    string DisplayName,
    string? Description = null,
    ActivityExecutionType? ExecutionType = null,
    IEnumerable<InputDefinition>? Inputs = null,
    IEnumerable<OutputDefinition>? Outputs = null,
    IEnumerable<ActivityDesignFacet>? DesignFacets = null
)

: ICommand<ActivityDefinitionVersionDetailsView>;
