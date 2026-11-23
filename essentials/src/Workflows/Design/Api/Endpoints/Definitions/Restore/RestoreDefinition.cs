using System.Text.Json;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Api.Models;

namespace Elsa.Workflows.Design.Api.Endpoints.Definitions.Restore;

public sealed record RestoreDefinition(string? OperationKey, string DefinitionId) : ICommand;
