using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText.Services;

namespace Elsa.Activities.Testing;

/// <summary>Factory for the default System.Text.Json payload serializer used across activity tests.</summary>
public static class TestPayloadSerializers
{
    /// <summary>A fresh <see cref="IPayloadSerializer"/> backed by an empty converter registry.</summary>
    public static IPayloadSerializer NewPayloadSerializer()
        => new JsonPayloadSerializer(new JsonPayloadConverterRegistry());
}
