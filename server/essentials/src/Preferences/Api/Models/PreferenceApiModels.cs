using System.Text.Json;

namespace Elsa.Studio.Preferences.Api.Models;

public sealed class GetStudioPreferenceRequest
{
    public string Namespace { get; set; } = "";
}

public sealed class PutStudioPreferenceRequest
{
    public string Namespace { get; set; } = "";
    public int SchemaVersion { get; set; }
    public JsonElement Value { get; set; }
}
