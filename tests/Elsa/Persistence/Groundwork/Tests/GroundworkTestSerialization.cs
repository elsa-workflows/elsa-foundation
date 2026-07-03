using Elsa.Persistence.Groundwork.Serialization;

namespace Elsa.Persistence.Groundwork.Tests;

/// <summary>
/// Shared serializer instances for store tests. Stores now depend on
/// <see cref="IGroundworkRuntimeDocumentSerializer"/>; tests that construct a store directly pass
/// <see cref="Serializer"/>, which is the production default wired to an empty upcaster registry
/// (no historical versions exist yet, so no upcasters are needed).
/// </summary>
internal static class GroundworkTestSerialization
{
    /// <summary>The default upcaster registry with no contributed upcasters.</summary>
    public static readonly IGroundworkRuntimeDocumentUpcasterRegistry UpcasterRegistry =
        new GroundworkRuntimeDocumentUpcasterRegistry([]);

    /// <summary>The production default serializer, wired to an empty upcaster registry.</summary>
    public static readonly IGroundworkRuntimeDocumentSerializer Serializer =
        new GroundworkRuntimeDocumentSerializer(UpcasterRegistry);
}
