using System.Text.Json;

namespace Elsa.Activities.Design.Persistence.Groundwork;

/// <summary>
/// Shared JSON serialization settings for the <b>simple</b> activity-design Groundwork document
/// (<c>ActivityDefinition</c>, which carries no authored payload or navigation). Web (camelCase) defaults so
/// serialized field paths match the declared index field names — the same convention the workflows-design
/// bridge's <c>GroundworkDesignJson</c> follows.
/// </summary>
public static class GroundworkActivitiesDesignJson
{
    /// <summary>The shared options instance used by the simple activity-design Groundwork document store.</summary>
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
