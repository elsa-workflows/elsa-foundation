using Elsa.Activities.Design.Core.Contracts;

namespace Elsa.Activities.Design.Reconciliation.Core;

/// <summary>
/// Optional Publishing-owned bridge that atomically closes a source-owned catalog version into its
/// immutable publication and Runtime template. Reconciliation remains provider-neutral and falls
/// back to catalog-only persistence when no publishing bridge is installed.
/// </summary>
public interface IActivitySourceVersionPublisher
{
    Task PublishAsync(
        IActivityDefinition definition,
        IActivityDefinitionVersion version,
        CancellationToken cancellationToken = default);
}
