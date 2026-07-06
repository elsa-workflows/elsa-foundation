using Elsa.Agent.Core.Models;

namespace Elsa.Agent.Core.Services;

/// <summary>
/// Small primitives shared verbatim by the provider adapters (issue #414 item 3): the event id
/// generator, the error-event factory, and the context-attachment sensitivity gate. Extracting these
/// gives the security-relevant <see cref="CanIncludeContent"/> gate a single source of truth, so a
/// change to what content may reach a provider lands in one place for every adapter.
/// </summary>
public static class AgentProviderPrimitives
{
    /// <summary>Generates a fresh event/call id (compact GUID), matching the id shape all adapters emit.</summary>
    public static string NewId() => Guid.NewGuid().ToString("N");

    /// <summary>Builds a provider error <see cref="AgentStreamEvent"/> with a fresh id and current timestamp.</summary>
    public static AgentStreamEvent Error(string code, string message, int statusCode)
        => new(NewId(), AgentStreamEventKind.Error, null, null, new AgentError(code, message, statusCode), DateTimeOffset.UtcNow, AgentResultKind.Error);

    /// <summary>
    /// Sensitivity gate deciding whether a context attachment's <see cref="AgentContextAttachment.Content"/>
    /// may be sent to a provider. Internal-and-below content is always allowed; Sensitive content is allowed
    /// only when the provider is configured to include it. Confidential and above are never included.
    /// </summary>
    public static bool CanIncludeContent(AgentContextAttachment attachment, bool includeSensitiveContextContent)
        => attachment.Sensitivity <= AgentContextSensitivity.Internal
           || (includeSensitiveContextContent && attachment.Sensitivity <= AgentContextSensitivity.Sensitive);
}
