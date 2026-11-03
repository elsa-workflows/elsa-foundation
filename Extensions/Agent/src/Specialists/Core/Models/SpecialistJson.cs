using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Agent.Specialists.Core.Models;

internal static class SpecialistJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 32,
        NumberHandling = JsonNumberHandling.Strict
    };

    public static JsonSerializerOptions ManifestOptions { get; } = new(Options)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
}
