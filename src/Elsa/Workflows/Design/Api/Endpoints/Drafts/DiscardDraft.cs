using System.Text.Json;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Api.Models;

namespace Elsa.Workflows.Design.Api.Endpoints.Drafts;

public sealed record DiscardDraft(string? OperationKey, string DraftId) : ICommand;
