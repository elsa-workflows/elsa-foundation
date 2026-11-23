using System.Text.Json;

namespace Elsa.Studio.Preferences.Api.Models;

public sealed class PutHomeLayoutRequest
{
    public int SchemaVersion { get; set; }
    public JsonElement Value { get; set; }
}

public sealed class HomeLayoutDocument
{
    public string Source { get; set; } = "builtin";
    public string Namespace { get; set; } = "";
    public int SchemaVersion { get; set; }
    public string? Revision { get; set; }
    public JsonElement Value { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
