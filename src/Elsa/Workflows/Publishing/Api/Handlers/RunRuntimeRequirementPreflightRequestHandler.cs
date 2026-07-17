using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Requests;
using Elsa.Workflows.Publishing.Api.Services;

namespace Elsa.Workflows.Publishing.Api.Handlers;

public sealed class RunRuntimeRequirementPreflightRequestHandler(RuntimeRequirementPreflight preflight)
    : IRequestHandler<RunRuntimeRequirementPreflight, RuntimeRequirementPreflightView>
{
    public Task<RuntimeRequirementPreflightView> Handle(RunRuntimeRequirementPreflight request, CancellationToken cancellationToken) =>
        preflight.RunAsync(request.Scope, request.ArtifactIds, cancellationToken).AsTask();
}
