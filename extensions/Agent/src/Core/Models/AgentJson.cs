using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Agent.Core.Models;

internal static class AgentJson
{
    public static JsonSerializerOptions Options { get; } = new(Elsa.Serialization.Core.JsonBaseline.Options)
    {
        MaxDepth = 32,
        NumberHandling = JsonNumberHandling.Strict
    };

    public static JsonSerializerOptions ManifestOptions { get; } = new(Options)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
}
