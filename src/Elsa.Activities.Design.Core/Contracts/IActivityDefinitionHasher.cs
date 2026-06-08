namespace Elsa.Activities.Design.Core.Contracts;

/// <summary>
/// Computes a stable content hash over an activity definition + version pair. The hash is generated
/// once at construction (by the activity-version factory) and stored on the version's
/// <see cref="IActivityDefinitionVersion.Hash"/>; reconciliation compares a candidate's hash to the
/// persisted hash to detect whether a source-side projection has changed. The default implementation
/// is SHA-256 over a canonicalised JSON serialisation; providers may replace it (per §2.6.2).
/// </summary>
public interface IActivityDefinitionHasher
{
    string Hash(IActivityDefinition definition, IActivityDefinitionVersion version);
}
