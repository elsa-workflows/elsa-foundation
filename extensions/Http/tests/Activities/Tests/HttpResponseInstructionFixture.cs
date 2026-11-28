using System.Text.Json;

namespace Elsa.Activities.Http.Tests;

internal static class HttpResponseInstructionFixture
{
    internal static JsonElement Text(string value) => JsonSerializer.SerializeToElement(value);

    internal static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
