using System.Text.Json;
using Elsa.Serialization.Core;

namespace Elsa.Weaver.Tests;

/// <summary>Test producers cross the CLR-to-data boundary explicitly, using the producer type's contract.</summary>
internal static class WeaverJsonData
{
    internal static JsonElement? From(object? value) =>
        value is null ? null : JsonConversionContract.SerializeToElement(value, value.GetType());
}
