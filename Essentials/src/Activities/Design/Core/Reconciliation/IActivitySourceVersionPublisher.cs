using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;

namespace Elsa.Activities.Design.Core.Reconciliation;

/// <summary>
/// Optional Publishing-owned bridge that atomically closes a source-owned catalog version into its
/// immutable publication and Runtime template. Reconciliation remains provider-neutral and falls
/// back to catalog-only persistence when no publishing bridge is installed.
/// </summary>
public interface IActivitySourceVersionPublisher
{
    /// <summary>
    /// Publishes the version, or refuses it with the diagnostics that say why. A refused version is not written, so the
    /// reconciler skips it and goes on with the remaining candidates. A failure to commit content that passed throws.
    /// </summary>
    Task<ActivitySourceVersionPublicationResult> PublishAsync(
        IActivityDefinition definition,
        IActivityDefinitionVersion version,
        CancellationToken cancellationToken = default);
}

/// <summary>What one source-owned version's publication did: published (or already published), or refused with its diagnostics.</summary>
public sealed record ActivitySourceVersionPublicationResult(bool IsPublished, IReadOnlyList<ActivityDiagnostic> Diagnostics)
{
    public static ActivitySourceVersionPublicationResult Published { get; } = new(true, []);

    public static ActivitySourceVersionPublicationResult Refused(IReadOnlyList<ActivityDiagnostic> diagnostics) => new(false, diagnostics);
}
