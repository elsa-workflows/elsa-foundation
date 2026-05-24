using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Api.Models;

namespace Elsa.Workflows.Design.Api.Requests;

public sealed record ListDefinitionVersions(string DefinitionId)
    : IRequest<IEnumerable<WorkflowDefinitionVersionView>>;
