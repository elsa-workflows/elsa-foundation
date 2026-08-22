using System.Text.Json;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Api.Models;

namespace Elsa.Workflows.Design.Api.Endpoints.Definitions.UpdateMetadata;

public sealed record UpdateDefinitionMetadata(
    string? OperationKey,
    string DefinitionId,
    string? Name = null,
    JsonElement? Description = null) : ICommand<WorkflowDefinitionDetailsView>;
