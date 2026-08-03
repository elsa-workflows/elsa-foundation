using System.Text.Json;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Services;

internal static class AuthoredValueConversionMapper
{
    public static ValueConversionMode Mode(AuthoredValueConversionRequest? request) =>
        request?.Mode switch
        {
            AuthoredValueConversionMode.None => ValueConversionMode.None,
            AuthoredValueConversionMode.Json => ValueConversionMode.Json,
            AuthoredValueConversionMode.Xml => ValueConversionMode.Xml,
            AuthoredValueConversionMode.Profile => ValueConversionMode.Profile,
            _ => ValueConversionMode.Auto
        };

    public static ValueConversionProfileReference? Profile(AuthoredValueConversionRequest? request) =>
        request?.Profile is null
            ? null
            : new ValueConversionProfileReference(request.Profile.Id, request.Profile.Version);

    public static ValueConversionLimits? Limits(AuthoredValueConversionRequest? request)
    {
        if (request?.Limits is null)
            return null;

        var defaults = ValueConversionLimits.Default;
        return new ValueConversionLimits(
            request.Limits.MaxDepth ?? defaults.MaxDepth,
            request.Limits.MaxCollectionItems ?? defaults.MaxCollectionItems,
            request.Limits.MaxPayloadBytes ?? defaults.MaxPayloadBytes);
    }

    public static JsonElement? Options(AuthoredValueConversionRequest? request) => request?.Options?.Clone();

    public static bool IsExplicitFormattedContent(AuthoredValueConversionRequest? request) =>
        Mode(request) is ValueConversionMode.Json or ValueConversionMode.Xml or ValueConversionMode.Profile;

    public static RuntimeValueConversionRequest? ToRuntimeRequest(AuthoredValueConversionRequest? request)
    {
        if (request is null)
            return null;

        return new RuntimeValueConversionRequest(Mode(request), Profile(request), Limits(request), Options(request));
    }
}
