using System.Text.Json;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Api.Models;

namespace Elsa.Workflows.Design.Api.Endpoints.Definitions.SoftDelete;

public sealed record SoftDeleteDefinition(string? OperationKey, string DefinitionId, string? Reason = null) : ICommand;
