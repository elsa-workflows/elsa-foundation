using Elsa.Mediator.Core.Contracts;

namespace Elsa.Workflows.Design.Api.Endpoints.Drafts.Discard;

public sealed record DiscardDraft(string? OperationKey, string DraftId) : ICommand;
