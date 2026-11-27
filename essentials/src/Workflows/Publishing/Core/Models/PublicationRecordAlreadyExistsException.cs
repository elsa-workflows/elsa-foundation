namespace Elsa.Workflows.Publishing.Core.Models;

/// <summary>A create-only journal identity is already occupied by a different record snapshot.</summary>
public sealed class PublicationRecordAlreadyExistsException(string publicationId)
    : InvalidOperationException($"Publication '{publicationId}' already exists.")
{
    public string PublicationId { get; } = publicationId;
}
