using Elsa.Activities.Design.Core.Contracts;

namespace Elsa.Activities.Design.Reconciliation.Core;

/// <summary>
/// Computes a stable hash over an activity definition + version pair. Used by the reconciler
/// to detect whether a source-side projection has changed since the last pass — when the
/// hash matches, write-skip the parent and refresh the reconciliation-state's
/// <c>LastSeenAt</c> instead of rewriting the row. The default implementation is SHA-256
/// over a canonicalised JSON serialisation; providers may replace it (per §2.6.2).
/// </summary>
public interface IActivityDefinitionHasher
{
    string Hash(IActivityDefinition definition, IActivityDefinitionVersion version);
}
