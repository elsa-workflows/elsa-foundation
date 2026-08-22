using System.Text.Json;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Api.Models;

namespace Elsa.Workflows.Design.Api.Endpoints.Definitions;

public sealed record DeleteDefinitionPermanently(string? OperationKey, string DefinitionId) : ICommand;
