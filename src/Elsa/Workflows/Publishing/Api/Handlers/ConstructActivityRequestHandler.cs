using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Requests;

namespace Elsa.Workflows.Publishing.Api.Handlers;

/// <summary>
/// Projects the persisted authored contract used to configure an activity node. Design tooling never
/// constructs a runtime activity object; activation is reserved for a pinned invocation attempt.
/// </summary>
public sealed class ConstructActivityRequestHandler(IActivityDefinitionVersionStore versions)
    : IRequestHandler<ConstructActivity, ConstructedActivityView>
{
    public async Task<ConstructedActivityView> Handle(ConstructActivity request, CancellationToken cancellationToken)
    {
        var version = await versions.GetWithDefinitionAsync(request.ActivityId, cancellationToken);
        return new ConstructedActivityView(
            version.Id,
            version.DescriptorType,
            version.DescriptorPayload,
            version.Inputs.Select(input => new ArgumentView(input.ReferenceKey, input.Name, input.Type.Alias)).ToArray(),
            version.Outputs.Select(output => new ArgumentView(output.ReferenceKey, output.Name, output.Type.Alias)).ToArray());
    }
}
