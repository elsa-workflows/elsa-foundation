namespace Elsa.Agent.Specialists.Core.Exceptions;

/// <summary>A refused or unconfirmed journal operation. It never authorizes another provider request.</summary>
public sealed class SpecialistJournalException(string code, string? handle, string message, Exception innerException)
    : Exception(message, innerException)
{
    /// <summary>Stable specialist failure family, without provider payload or credentials.</summary>
    public string Code { get; } = code;
    /// <summary>The canonical opaque turn handle when available.</summary>
    public string? Handle { get; } = handle;
}
