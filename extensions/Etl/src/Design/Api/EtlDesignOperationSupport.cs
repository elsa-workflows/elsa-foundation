using Elsa.Etl.Api;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Design.Api.Models;

namespace Elsa.Etl.Design.Api;

/// <summary>Small shared rules for the versioned ETL authoring APIs.</summary>
internal static class EtlDesignOperationSupport
{
    internal static EtlCursorState? Cursor(EtlCursorCodec cursors, string? token, int limit, string kind, EtlScope scope, string? resourceId = null)
    {
        if (limit is < 1 or > 200)
            throw new EtlApiException(400, "etl.page-limit-invalid");
        if (token is null)
            return null;

        EtlCursorState state;
        try
        {
            state = cursors.Decode(token);
        }
        catch (FormatException)
        {
            throw InvalidCursor();
        }

        if (state.Kind != kind || state.ScopeKey != EtlRuntimeIdentity.ScopeKey(scope) || state.ResourceId != resourceId || state.Limit != limit)
            throw InvalidCursor();

        var validBoundary = kind switch
        {
            "definitions" or "connector-definitions" => state.LastId is not null && state.LastVersion == 0 && state.ThroughVersion == 0 && state.LastAlias is null && state.LastRole is null,
            "versions" or "connector-versions" => state.LastId is null && state.LastVersion > 0 && state.ThroughVersion >= state.LastVersion && state.LastAlias is null && state.LastRole is null,
            "bindings" or "connector-bindings" => state.LastAlias is not null && state.LastRole is not null && state.LastId is null && state.LastVersion == 0 && state.ThroughVersion == 0,
            _ => false
        };

        return validBoundary && state.LastOrdinal is null ? state : throw InvalidCursor();
    }

    internal static EtlCursorState State(string kind, EtlScope scope, int limit, string? resource = null) =>
        new(1, kind, EtlRuntimeIdentity.ScopeKey(scope), resource, limit, null, 0, 0, null, null);

    internal static EtlPublishedSelection Selection(string id, int version, string hash) => new(id, version, hash);

    internal static void ValidateId(string id) => _ = Selection(id, 1, new string('0', 64));

    internal static void Revision(long revision)
    {
        if (revision <= 0)
            throw new ArgumentOutOfRangeException(nameof(revision));
    }

    internal static int NextVersion(int lastPublishedVersion) => lastPublishedVersion == int.MaxValue
        ? throw Conflict()
        : lastPublishedVersion + 1;

    internal static EtlPublishedVersionView Version(EtlArtifact artifact) => new(artifact.DefinitionId, artifact.DefinitionVersion,
        artifact.ContentHash, Selection(artifact.DefinitionId, artifact.DefinitionVersion, artifact.ContentHash).ToToken(), null);

    internal static EtlApiException Conflict() => new(409, "etl.definition-conflict");

    internal static EtlApiException NotFound() => new(404, "etl.definition-not-found");

    private static EtlApiException InvalidCursor() => new(400, "etl.cursor-invalid");
}
