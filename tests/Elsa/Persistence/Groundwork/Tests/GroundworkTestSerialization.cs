using Elsa.Persistence.Groundwork.Serialization;

namespace Elsa.Persistence.Groundwork.Tests;

/// <summary>
/// Shared serializer instances for store tests. Stores now depend on
/// <see cref="IGroundworkRuntimeDocumentSerializer"/>; tests that construct a store directly pass
/// <see cref="Serializer"/>, which is the production default with the pre-GA current-only policy.
/// </summary>
internal static class GroundworkTestSerialization
{
    /// <summary>The production default serializer with the pre-GA current-only schema policy.</summary>
    public static readonly IGroundworkRuntimeDocumentSerializer Serializer = new GroundworkRuntimeDocumentSerializer();
}
