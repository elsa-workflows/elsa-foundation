using Elsa.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Documents.Store;

namespace Elsa.Workflows.Publishing.Persistence.Groundwork.Services;

/// <summary>
/// The runtime document a publication writes before its design commit, shared by both publication paths so
/// the two cannot drift apart in how they write or read it.
/// </summary>
internal sealed record SourceReferenceDocument(
    string Collection,
    string ArtifactId,
    WorkflowExecutableSourceReference Reference);

/// <summary>
/// The runtime template document a publication writes, shared by both publication paths for the same reason
/// as <see cref="SourceReferenceDocument"/>: two private copies of a persisted shape drift silently.
/// </summary>
internal sealed record TemplateDocument(
    string Collection,
    string TemplateHash,
    ExecutableActivityTemplate Template);

/// <summary>
/// The runtime phase of a split publication.
/// <para>
/// Both publication paths write runtime documents before the design commit, so both can be interrupted
/// between the two. The template survives that harmlessly — it is content-addressed, and an identical one
/// already present is skipped — but the source reference is create-only with no such check, so a naive retry
/// conflicts on it and the publication becomes permanently unfinishable. Recognising a source reference as
/// <em>this</em> publication's own is what makes the retry safe.
/// </para>
/// </summary>
internal static class RuntimePublicationPhase
{
    /// <summary>
    /// Whether this publication's runtime phase already committed, so the retry should skip it rather than
    /// re-write a create-only document.
    /// </summary>
    /// <param name="colocated">
    /// A co-located publication commits every lane in one transaction and can never observe a half-written
    /// runtime phase, so it keeps the strict create-only behaviour: an existing source reference is a clash.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// The source reference belongs to a different artifact, or the publication is co-located and the
    /// reference already exists.
    /// </exception>
    public static async Task<bool> AlreadyCommittedAsync(
        IDocumentStore runtimeStore,
        IGroundworkRuntimeDocumentSerializer runtimeSerializer,
        string sourceReferenceId,
        string artifactId,
        bool colocated,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runtimeStore);
        ArgumentNullException.ThrowIfNull(runtimeSerializer);

        var existing = await runtimeStore.LoadAsync(
            ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceDocumentKind,
            sourceReferenceId,
            cancellationToken);
        if (existing is null)
            return false;

        if (colocated)
        {
            throw new InvalidOperationException(
                $"Document '{ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceDocumentKind}/" +
                $"{sourceReferenceId}' already exists.");
        }

        // A runtime document, so it is read back through the runtime serializer: design JSON options would
        // risk a mis-parse, and would skip the per-kind schema-version policy that serializer enforces.
        var stored = runtimeSerializer.Deserialize<SourceReferenceDocument>(existing);
        if (!StringComparer.Ordinal.Equals(stored.ArtifactId, artifactId))
        {
            throw new InvalidOperationException(
                $"Source reference '{sourceReferenceId}' already exists for a different artifact.");
        }

        return true;
    }
}
