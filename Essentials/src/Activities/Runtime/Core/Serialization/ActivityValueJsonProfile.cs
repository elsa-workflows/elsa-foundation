using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Activities.Runtime.Core.Serialization;

/// <summary>The existing JSON contract for values crossing an activity boundary.</summary>
/// <remarks>
/// This profile is independent of the general payload serializer. Its identity belongs only to new
/// value-selection metadata; it does not change existing activity catalog or whole-value hashes.
/// </remarks>
public static class ActivityValueJsonProfile
{
    public const string Revision = "elsa.activity-value-json/1";

    /// <summary>Creates independent options with the unchanged activity-value defaults.</summary>
    public static JsonSerializerOptions CreateOptions() => new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        NumberHandling = JsonNumberHandling.Strict
    };
}
