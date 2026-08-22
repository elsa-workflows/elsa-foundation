using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Workflows.Design.Api.Endpoints.Versions;

public sealed record ListDefinitionVersions(string DefinitionId)
    : IRequest<IEnumerable<WorkflowDefinitionVersionSummary>>;
