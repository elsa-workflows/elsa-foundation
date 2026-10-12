using System.Text.Json;

namespace Elsa.Workflows.Design.Core.Models;

/// <summary>
/// Authoring-time conversion intent. A null request on <see cref="ArgumentState.Conversion"/> means the
/// legacy/default Auto behavior; non-null values survive draft/API/code-first round-trips until publication can
/// pin a deterministic runtime conversion plan.
/// </summary>
public sealed record AuthoredValueConversionRequest(
    AuthoredValueConversionMode Mode = AuthoredValueConversionMode.Auto,
    AuthoredValueConversionProfile? Profile = null,
    AuthoredValueConversionLimits? Limits = null,
    JsonElement? Options = null);

public enum AuthoredValueConversionMode
{
    Auto,
    None,
    Json,
    Xml,
    Profile
}

public sealed record AuthoredValueConversionProfile(string Id, string Version);

public sealed record AuthoredValueConversionLimits(
    int? MaxDepth = null,
    int? MaxCollectionItems = null,
    long? MaxPayloadBytes = null);
