using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Core.Models;

/// <summary>Publication-owner source read failure, retaining the physical scope and original infrastructure cause.</summary>
public sealed class PublicationSourceSnapshotPersistenceException(string publicationId, PersistenceScope scope, Exception innerException)
    : Exception("The publication source snapshot could not be read.", innerException)
{
    public string PublicationId { get; } = publicationId;
    public PersistenceScope Scope { get; } = scope;
}
