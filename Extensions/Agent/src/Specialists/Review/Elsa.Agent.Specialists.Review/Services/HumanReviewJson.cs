using System.Text.Json;

namespace Elsa.Agent.Specialists.Review.Services;

/// <summary>
/// The one serializer configuration for each persisted human review shape. Every shape is written and read with the
/// same options, so a change here is a stored-data compatibility change, not a refactor.
/// </summary>
internal static class HumanReviewJson
{
    /// <summary>
    /// Private state and the decision delivery: the native runtime writes both with default options (PascalCase),
    /// and the delivery is serialized by the decision service before dispatch.
    /// </summary>
    public static readonly JsonSerializerOptions Runtime = JsonSerializerOptions.Default;

    /// <summary>The protected envelope plaintext and the typed terminal result (camelCase).</summary>
    public static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
}
