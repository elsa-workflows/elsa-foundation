using System.Text.Json;

namespace Elsa.Agent.Review.Services;

/// <summary>
/// The one serializer configuration for each persisted human review shape. Every shape is written and read with the
/// same options, so a change here is a stored-data compatibility change, not a refactor.
/// </summary>
internal static class HumanReviewJson
{
    /// <summary>
    /// Private state and decision delivery share the native runtime's scoped, exact camelCase conversion contract.
    /// </summary>
    public static readonly JsonSerializerOptions Runtime = Elsa.Serialization.Core.JsonConversionContract.Default.Options;

    /// <summary>The protected envelope plaintext and the typed terminal result (camelCase).</summary>
    public static readonly JsonSerializerOptions Web = new(Elsa.Serialization.Core.JsonBaseline.Options);
}
