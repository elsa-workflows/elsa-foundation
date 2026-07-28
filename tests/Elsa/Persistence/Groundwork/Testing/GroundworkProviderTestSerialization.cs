using Elsa.Persistence.Groundwork.Serialization;

namespace Elsa.Persistence.Groundwork.Testing;

/// <summary>Production-equivalent runtime serializer shared by provider-level conformance suites.</summary>
public static class GroundworkProviderTestSerialization
{
    public static IGroundworkRuntimeDocumentSerializer Serializer { get; } = new GroundworkRuntimeDocumentSerializer();
}
