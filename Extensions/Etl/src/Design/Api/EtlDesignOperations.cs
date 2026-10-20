using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Design.Api.Models;
using Elsa.Etl.Design.Stores;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Etl.Design.Api;

internal sealed class EtlDesignOperations(IServiceProvider services, EtlRequestScope requestScope, EtlCursorCodec cursors) : IEtlDesignOperations
{
    public Task<EtlPage<EtlBindingSummary>> ListBindingsAsync(EtlPageRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scope = requestScope.Get();
        var cursor = Cursor(request.Cursor, request.Limit, "bindings", scope);
        var rows = Required<IEtlBindingCatalog>().ListBindings(scope, cursor?.LastAlias, cursor?.LastRole, request.Limit + 1);
        var items = rows.Take(request.Limit).ToArray();
        var next = rows.Count > request.Limit ? cursors.Encode(State("bindings", scope, request.Limit) with
        { LastAlias = items[^1].Alias, LastRole = items[^1].Role }) : null;
        return Task.FromResult(new EtlPage<EtlBindingSummary>(items, next));
    }

    public async Task<EtlPage<EtlDefinitionSummaryView>> ListDefinitionsAsync(EtlPageRequest request, CancellationToken cancellationToken)
    {
        var scope = requestScope.Get();
        var cursor = Cursor(request.Cursor, request.Limit, "definitions", scope);
        var rows = await Required<IEtlDefinitionCatalog>().ListDefinitionsAsync(scope, cursor?.LastId, request.Limit + 1, cancellationToken);
        var items = rows.Take(request.Limit).Select(row => new EtlDefinitionSummaryView(row.DefinitionId, row.Revision, row.LastPublishedVersion, row.UpdatedAt)).ToArray();
        var next = rows.Count > request.Limit ? cursors.Encode(State("definitions", scope, request.Limit) with { LastId = items[^1].DefinitionId }) : null;
        return new(items, next);
    }

    public async Task<EtlDraftView> CreateAsync(EtlCreateDefinitionRequest request, CancellationToken cancellationToken)
    {
        var scope = requestScope.Get();
        ValidateId(request.Id);
        ArgumentNullException.ThrowIfNull(request.Pipeline);
        return Draft(await Required<IEtlDefinitionStore>().CreateDraftAsync(scope, request.Id, request.Pipeline, cancellationToken));
    }

    public async Task<EtlDraftView> GetAsync(EtlDefinitionRequest request, CancellationToken cancellationToken) =>
        View(await FindDraftAsync(requestScope.Get(), request.Id, cancellationToken));

    public async Task<EtlDraftView> UpdateAsync(EtlUpdateDraftRequest request, CancellationToken cancellationToken)
    {
        var scope = requestScope.Get();
        ValidateId(request.Id);
        Revision(request.ExpectedRevision);
        ArgumentNullException.ThrowIfNull(request.Pipeline);
        return Draft(await Required<IEtlDefinitionStore>().UpdateDraftAsync(scope, request.Id, request.ExpectedRevision, request.Pipeline, cancellationToken));
    }

    public async Task<EtlValidationView> ValidateAsync(EtlValidateRequest request, CancellationToken cancellationToken)
    {
        var draft = await FindDraftAsync(requestScope.Get(), request.Id, cancellationToken);
        Revision(request.ExpectedRevision);
        if (draft.Revision != request.ExpectedRevision) throw Conflict();
        var version = NextVersion(draft);
        var result = Required<IEtlArtifactCompiler>().Compile(draft.DefinitionId, version, draft.Pipeline);
        return new(draft.Revision, version, result.Artifact?.ContentHash, result.Diagnostics);
    }

    public async Task<EtlPublicationView> PublishAsync(EtlPublishRequest request, CancellationToken cancellationToken)
    {
        var scope = requestScope.Get();
        Revision(request.ExpectedRevision);
        // The explicit version/hash tuple makes a lost-response retry independent of later draft edits.
        _ = Selection(request.Id, request.DefinitionVersion, request.ContentHash);
        var store = Required<IEtlDefinitionStore>();
        if (await FindReplayAsync(store, scope, request, cancellationToken) is { } replay) return replay;

        var draft = await FindDraftAsync(scope, request.Id, cancellationToken);
        // A concurrent identical publisher can commit between the first immutable read and the draft read.
        // Recheck that immutable version before reporting a stale revision/next-version conflict.
        if (draft.Revision != request.ExpectedRevision || draft.LastPublishedVersion == int.MaxValue || draft.LastPublishedVersion + 1 != request.DefinitionVersion)
            return await FindReplayAsync(store, scope, request, cancellationToken) ?? throw Conflict();
        var compiled = Required<IEtlArtifactCompiler>().Compile(request.Id, request.DefinitionVersion, draft.Pipeline);
        if (compiled.Artifact is not { } artifact) throw new EtlApiException(400, "etl.validation-failed");
        if (artifact.ContentHash != request.ContentHash) throw Conflict();
        var result = await store.PublishAsync(scope, request.Id, request.ExpectedRevision, artifact, cancellationToken);
        return result.Status switch
        {
            EtlPublicationStatus.Published or EtlPublicationStatus.Replay when result.Artifact is not null => new(result.Status.ToString().ToLowerInvariant(), Version(result.Artifact)),
            EtlPublicationStatus.NotFound => throw NotFound(),
            EtlPublicationStatus.InvalidArtifact => throw new EtlApiException(400, "etl.validation-failed"),
            _ => throw Conflict()
        };
    }

    public async Task<EtlPage<EtlPublishedVersionView>> ListVersionsAsync(EtlVersionsRequest request, CancellationToken cancellationToken)
    {
        var scope = requestScope.Get();
        ValidateId(request.Id);
        var cursor = Cursor(request.Cursor, request.Limit, "versions", scope, request.Id);
        var draft = await FindDraftAsync(scope, request.Id, cancellationToken);
        var ceiling = cursor?.ThroughVersion ?? draft.LastPublishedVersion;
        var rows = await Required<IEtlDefinitionCatalog>().ListVersionsAsync(scope, request.Id, cursor?.LastVersion ?? 0, ceiling, request.Limit + 1, cancellationToken);
        var items = rows.Take(request.Limit).Select(row => new EtlPublishedVersionView(row.DefinitionId, row.DefinitionVersion,
            row.ContentHash, Selection(row.DefinitionId, row.DefinitionVersion, row.ContentHash).ToToken(), row.PublishedAt)).ToArray();
        var next = rows.Count > request.Limit ? cursors.Encode(State("versions", scope, request.Limit, request.Id) with
        { LastVersion = items[^1].DefinitionVersion, ThroughVersion = ceiling }) : null;
        return new(items, next);
    }

    public async Task<EtlPublishedArtifactView> GetVersionAsync(EtlVersionRequest request, CancellationToken cancellationToken)
    {
        var scope = requestScope.Get();
        ValidateId(request.Id);
        if (request.Version <= 0) throw new ArgumentOutOfRangeException(nameof(request));
        var artifact = await Required<IEtlDefinitionStore>().FindPublishedAsync(scope, request.Id, request.Version, cancellationToken) ?? throw NotFound();
        return new(Version(artifact), EtlArtifactJson.Read(artifact.Pipeline));
    }

    private async Task<EtlDefinitionDraft> FindDraftAsync(EtlScope scope, string id, CancellationToken cancellationToken)
    {
        ValidateId(id);
        return await Required<IEtlDefinitionStore>().FindDraftAsync(scope, id, cancellationToken) ?? throw NotFound();
    }

    private static async Task<EtlPublicationView?> FindReplayAsync(IEtlDefinitionStore store, EtlScope scope, EtlPublishRequest request, CancellationToken cancellationToken)
    {
        var existing = await store.FindPublishedAsync(scope, request.Id, request.DefinitionVersion, cancellationToken);
        if (existing is null) return null;
        if (existing.ContentHash != request.ContentHash || existing.ArtifactId !=
            EtlArtifactJson.CreateArtifactId(EtlArtifact.CurrentFormatVersion, request.Id, request.DefinitionVersion, request.ContentHash))
            throw Conflict();
        return new("replay", Version(existing));
    }

    private EtlCursorState? Cursor(string? token, int limit, string kind, EtlScope scope, string? resourceId = null)
    {
        if (limit is < 1 or > 200) throw new EtlApiException(400, "etl.page-limit-invalid");
        if (token is null) return null;
        EtlCursorState state;
        try { state = cursors.Decode(token); }
        catch (FormatException) { throw InvalidCursor(); }
        if (state.Kind != kind || state.ScopeKey != EtlRuntimeIdentity.ScopeKey(scope) || state.ResourceId != resourceId || state.Limit != limit)
            throw InvalidCursor();
        var validBoundary = kind switch
        {
            "definitions" => state.LastId is not null && state.LastVersion == 0 && state.ThroughVersion == 0 && state.LastAlias is null && state.LastRole is null,
            "versions" => state.LastId is null && state.LastVersion > 0 && state.ThroughVersion >= state.LastVersion && state.LastAlias is null && state.LastRole is null,
            "bindings" => state.LastAlias is not null && state.LastRole is not null && state.LastId is null && state.LastVersion == 0 && state.ThroughVersion == 0,
            _ => false
        };
        return validBoundary ? state : throw InvalidCursor();
    }

    private T Required<T>() where T : class => services.GetService<T>() ?? throw new EtlApiException(503, "etl.provider-unavailable");
    private static EtlCursorState State(string kind, EtlScope scope, int limit, string? resource = null) =>
        new(1, kind, EtlRuntimeIdentity.ScopeKey(scope), resource, limit, null, 0, 0, null, null);
    private static void ValidateId(string id) => _ = Selection(id, 1, new string('0', 64));
    private static EtlPublishedSelection Selection(string id, int version, string hash) => new(id, version, hash);
    private static void Revision(long revision) { if (revision <= 0) throw new ArgumentOutOfRangeException(nameof(revision)); }
    private static int NextVersion(EtlDefinitionDraft draft) => draft.LastPublishedVersion == int.MaxValue ? throw Conflict() : draft.LastPublishedVersion + 1;
    private static EtlPublishedVersionView Version(EtlArtifact artifact) => new(artifact.DefinitionId, artifact.DefinitionVersion,
        artifact.ContentHash, Selection(artifact.DefinitionId, artifact.DefinitionVersion, artifact.ContentHash).ToToken(), null);
    private static EtlDraftView View(EtlDefinitionDraft draft) => new(draft.DefinitionId, draft.Revision, draft.LastPublishedVersion, draft.Pipeline, draft.UpdatedAt);
    private static EtlDraftView Draft(EtlDefinitionWriteResult result) => result.Status switch
    {
        EtlDefinitionWriteStatus.Created or EtlDefinitionWriteStatus.Updated when result.Draft is not null => View(result.Draft),
        EtlDefinitionWriteStatus.NotFound => throw NotFound(),
        _ => throw Conflict()
    };
    private static EtlApiException Conflict() => new(409, "etl.definition-conflict");
    private static EtlApiException NotFound() => new(404, "etl.definition-not-found");
    private static EtlApiException InvalidCursor() => new(400, "etl.cursor-invalid");
}

internal sealed class EtlApiException(int status, string code) : Exception(code)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}
