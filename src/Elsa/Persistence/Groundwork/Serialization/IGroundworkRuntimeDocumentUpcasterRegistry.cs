using System.Text.Json.Nodes;

namespace Elsa.Persistence.Groundwork.Serialization;

/// <summary>
/// Resolves and applies the <see cref="IGroundworkRuntimeDocumentUpcaster"/> chain that brings a
/// runtime document's content JSON from a historical schema version to a target version.
/// </summary>
/// <remarks>
/// Replacement contract: exactly one implementation is active per runtime host. The default is
/// <see cref="GroundworkRuntimeDocumentUpcasterRegistry"/>.
/// </remarks>
public interface IGroundworkRuntimeDocumentUpcasterRegistry
{
    /// <summary>
    /// Applies registered upcaster steps to <paramref name="content"/>, one version at a time, from
    /// <paramref name="fromVersion"/> until <paramref name="toVersion"/> is reached.
    /// </summary>
    /// <exception cref="Exceptions.GroundworkRuntimeDocumentVersionException">
    /// A step in the chain is missing, or multiple upcasters claim the same step.
    /// </exception>
    JsonObject Upcast(string documentKind, int fromVersion, int toVersion, JsonObject content);
}
