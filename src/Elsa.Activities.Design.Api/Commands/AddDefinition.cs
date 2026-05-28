using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Mediator.Core.Contracts;

namespace Elsa.Activities.Design.Api.Commands;

public sealed record AddDefinition(
    string ActivityTypeKey,
    string SourceKind,
    string SourceId,
    IImplementationDescriptor ImplementationDescriptor,
    string Category,
    string DisplayName,
    string? Description = null,
    ActivityKind? Kind = null,
    IEnumerable<InputDefinition>? Inputs = null,
    IEnumerable<OutputDefinition>? Outputs = null,
    IEnumerable<ActivityPortDefinition>? Ports = null
)

: ICommand<ActivityDefinitionVersionDetailsView>;
