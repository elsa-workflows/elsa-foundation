using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Publishing.Api.Models;

namespace Elsa.Workflows.Publishing.Api.Requests;

public sealed record RunRuntimeRequirementPreflight(
    string Scope,
    IReadOnlyList<string>? ArtifactIds) : IRequest<RuntimeRequirementPreflightView>;
