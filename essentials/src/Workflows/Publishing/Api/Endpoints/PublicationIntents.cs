using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Core.Models;

namespace Elsa.Workflows.Publishing.Api.Endpoints;

/// <summary>Resolves the caller's requested publication intent, shared by the preflight endpoints.</summary>
internal static class PublicationIntents
{
    public static PublicationRequestIntent? RequestIntent(PublicationActionView? action, string? slotName) =>
        action is { } requestedAction
            ? new PublicationRequestIntent(PublicationIntentContract.ToModel(requestedAction), slotName)
            : slotName is not null
                ? new PublicationRequestIntent(PublicationAction.Replace, slotName)
                : null;
}
