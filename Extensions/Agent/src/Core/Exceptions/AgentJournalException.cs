using Elsa.Agent.Core.Models;
namespace Elsa.Agent.Core.Exceptions;

/// <summary>A refused or unconfirmed journal operation. It never authorizes another provider request.</summary>
public sealed class AgentJournalException(string code, string? handle, string message, Exception innerException)
    : Exception(message, innerException)
{
    /// <summary>Stable agent failure family, without provider payload or credentials.</summary>
    public string Code { get; } = code;
    /// <summary>The canonical opaque turn handle when available.</summary>
    public string? Handle { get; } = AgentTurnDocumentRules.IsToken(handle) ? handle : null;
}
