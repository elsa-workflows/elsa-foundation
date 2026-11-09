using Elsa.Etl.Api;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Design.Api.Models;
using Elsa.Etl.Design.Stores;
using Microsoft.Extensions.DependencyInjection;
using static Elsa.Etl.Design.Api.EtlDesignOperationSupport;

namespace Elsa.Etl.Design.Api;

/// <summary>Scoped v2 authoring operations over the existing ETL identity and publication stores.</summary>
internal sealed class EtlConnectorDesignOperations(
    IServiceProvider services,
    EtlRequestScope requestScope,
    [FromKeyedServices(EtlDesignApi.OwnerId)] EtlCursorCodec cursors) : IEtlConnectorDesignOperations
{
    private const int ConnectorFormatVersion = EtlArtifactV2Json.FormatVersion;

    public Task<EtlPage<EtlConnectorBindingSummary>> ListBindingsAsync(EtlPageRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scope = requestScope.Get();
        var cursor = Cursor(cursors, request.Cursor, request.Limit, "connector-bindings", scope);
        var rows = Required<IEtlConnectorBindingCatalog>().ListBindings(scope, cursor?.LastAlias, cursor?.LastRole, request.Limit + 1);
        var items = rows.Take(request.Limit).ToArray();
        var next = rows.Count > request.Limit
            ? cursors.Encode(State("connector-bindings", scope, request.Limit) with { LastAlias = items[^1].Alias, LastRole = items[^1].Role })
            : null;
        return Task.FromResult(new EtlPage<EtlConnectorBindingSummary>(items, next));
    }

    public async Task<EtlPage<EtlDefinitionSummaryView>> ListDefinitionsAsync(EtlPageRequest request, CancellationToken cancellationToken)
    {
        var scope = requestScope.Get();
        var cursor = Cursor(cursors, request.Cursor, request.Limit, "connector-definitions", scope);
        var rows = await Required<IEtlDefinitionCatalog>().ListDefinitionsAsync(scope, cursor?.LastId, request.Limit + 1, cancellationToken);
        // The shared identity store may contain both formats. Advance by the scanned raw page so v1 rows do not
        // appear in this response, and so an empty filtered page still makes bounded pagination progress.
        var scanned = rows.Take(request.Limit).ToArray();
        var items = scanned.Where(row => row.FormatVersion == ConnectorFormatVersion)
            .Select(row => new EtlDefinitionSummaryView(row.DefinitionId, row.Revision, row.LastPublishedVersion, row.UpdatedAt)).ToArray();
        var next = rows.Count > request.Limit
            ? cursors.Encode(State("connector-definitions", scope, request.Limit) with { LastId = scanned[^1].DefinitionId })
            : null;
        return new(items, next);
    }

    public async Task<EtlConnectorDraftView> CreateAsync(EtlConnectorCreateDefinitionRequest request, CancellationToken cancellationToken)
    {
        var scope = requestScope.Get();
        ValidateId(request.Id);
        ArgumentNullException.ThrowIfNull(request.Pipeline);
        var result = await Required<IEtlDefinitionStoreV2>().CreateDraftAsync(scope, request.Id, request.Pipeline, cancellationToken);
        return Draft(result);
    }

    public async Task<EtlConnectorDraftView> GetAsync(EtlDefinitionRequest request, CancellationToken cancellationToken) =>
        View(await FindDraftAsync(requestScope.Get(), request.Id, cancellationToken));

    public async Task<EtlConnectorDraftView> UpdateAsync(EtlConnectorUpdateDraftRequest request, CancellationToken cancellationToken)
    {
        var scope = requestScope.Get();
        ValidateId(request.Id);
        Revision(request.ExpectedRevision);
        ArgumentNullException.ThrowIfNull(request.Pipeline);
        var result = await Required<IEtlDefinitionStoreV2>().UpdateDraftAsync(scope, request.Id, request.ExpectedRevision, request.Pipeline, cancellationToken);
        return Draft(result);
    }

    public async Task<EtlValidationView> ValidateAsync(EtlValidateRequest request, CancellationToken cancellationToken)
    {
        var scope = requestScope.Get();
        var draft = await FindDraftAsync(scope, request.Id, cancellationToken);
        Revision(request.ExpectedRevision);
        if (draft.Revision != request.ExpectedRevision)
            throw Conflict();

        var version = NextVersion(draft.LastPublishedVersion);
        var compilation = Required<IEtlArtifactCompilerV2>().Compile(draft.DefinitionId, version, draft.Pipeline);
        if (compilation.Artifact is null)
            return new(draft.Revision, version, null, compilation.Diagnostics);

        if (!await BindingsApprovedAsync(scope, draft.Pipeline, cancellationToken))
            return new(draft.Revision, version, null, [BindingUnavailableDiagnostic()]);

        return new(draft.Revision, version, compilation.Artifact.ContentHash, compilation.Diagnostics);
    }

    public async Task<EtlPublicationView> PublishAsync(EtlPublishRequest request, CancellationToken cancellationToken)
    {
        var scope = requestScope.Get();
        Revision(request.ExpectedRevision);
        _ = Selection(request.Id, request.DefinitionVersion, request.ContentHash);
        var store = Required<IEtlDefinitionStore>();

        // An identical immutable publication remains replayable even if the binding was withdrawn later.
        if (await FindReplayAsync(store, scope, request, cancellationToken) is { } replay)
            return replay;

        var draft = await FindDraftAsync(scope, request.Id, cancellationToken);
        if (draft.Revision != request.ExpectedRevision || draft.LastPublishedVersion == int.MaxValue ||
            draft.LastPublishedVersion + 1 != request.DefinitionVersion)
            return await FindReplayAsync(store, scope, request, cancellationToken) ?? throw Conflict();

        var compilation = Required<IEtlArtifactCompilerV2>().Compile(request.Id, request.DefinitionVersion, draft.Pipeline);
        if (compilation.Artifact is not { } artifact)
            throw new EtlApiException(400, "etl.validation-failed");
        if (artifact.ContentHash != request.ContentHash)
            throw Conflict();

        if (!await BindingsApprovedAsync(scope, draft.Pipeline, cancellationToken))
            return await FindReplayAsync(store, scope, request, cancellationToken) ??
                   throw new EtlApiException(400, "etl.validation-failed");

        var result = await store.PublishAsync(scope, request.Id, request.ExpectedRevision, artifact, cancellationToken);
        return result.Status switch
        {
            EtlPublicationStatus.Published or EtlPublicationStatus.Replay when result.Artifact is { FormatVersion: ConnectorFormatVersion } =>
                new(result.Status.ToString().ToLowerInvariant(), Version(result.Artifact)),
            EtlPublicationStatus.NotFound => throw NotFound(),
            EtlPublicationStatus.InvalidArtifact => throw new EtlApiException(400, "etl.validation-failed"),
            _ => throw Conflict()
        };
    }

    public async Task<EtlPage<EtlPublishedVersionView>> ListVersionsAsync(EtlVersionsRequest request, CancellationToken cancellationToken)
    {
        var scope = requestScope.Get();
        ValidateId(request.Id);
        var cursor = Cursor(cursors, request.Cursor, request.Limit, "connector-versions", scope, request.Id);
        var draft = await FindDraftAsync(scope, request.Id, cancellationToken);
        var ceiling = cursor?.ThroughVersion ?? draft.LastPublishedVersion;
        var rows = await Required<IEtlDefinitionCatalog>().ListVersionsAsync(scope, request.Id, cursor?.LastVersion ?? 0,
            ceiling, request.Limit + 1, cancellationToken);
        var scanned = rows.Take(request.Limit).ToArray();
        var items = scanned.Where(row => row.FormatVersion == ConnectorFormatVersion).Select(row =>
            new EtlPublishedVersionView(row.DefinitionId, row.DefinitionVersion, row.ContentHash,
                Selection(row.DefinitionId, row.DefinitionVersion, row.ContentHash).ToToken(), row.PublishedAt)).ToArray();
        var next = rows.Count > request.Limit
            ? cursors.Encode(State("connector-versions", scope, request.Limit, request.Id) with
            { LastVersion = scanned[^1].DefinitionVersion, ThroughVersion = ceiling })
            : null;
        return new(items, next);
    }

    public async Task<EtlConnectorPublishedArtifactView> GetVersionAsync(EtlVersionRequest request, CancellationToken cancellationToken)
    {
        var scope = requestScope.Get();
        ValidateId(request.Id);
        if (request.Version <= 0)
            throw new ArgumentOutOfRangeException(nameof(request));
        var artifact = await Required<IEtlDefinitionStore>().FindPublishedAsync(scope, request.Id, request.Version, cancellationToken)
                       ?? throw NotFound();
        if (artifact.FormatVersion != ConnectorFormatVersion)
            throw NotFound();
        return new(Version(artifact), EtlArtifactV2Json.Read(artifact.Pipeline));
    }

    private async ValueTask<bool> BindingsApprovedAsync(EtlScope scope, EtlPipelineV2 pipeline, CancellationToken cancellationToken)
    {
        var resolution = await Required<IEtlConnectorBindingResolver>().ResolveAsync(scope, pipeline, cancellationToken);
        return resolution is { Bindings: not null, FailureCode: null };
    }

    private async Task<EtlDefinitionDraftV2> FindDraftAsync(EtlScope scope, string id, CancellationToken cancellationToken)
    {
        ValidateId(id);
        try
        {
            return await Required<IEtlDefinitionStoreV2>().FindDraftAsync(scope, id, cancellationToken) is { } draft
                ? draft
                : throw NotFound();
        }
        catch (NotSupportedException)
        {
            throw NotFound();
        }
    }

    private static async Task<EtlPublicationView?> FindReplayAsync(IEtlDefinitionStore store, EtlScope scope,
        EtlPublishRequest request, CancellationToken cancellationToken)
    {
        var existing = await store.FindPublishedAsync(scope, request.Id, request.DefinitionVersion, cancellationToken);
        if (existing is null)
            return null;
        if (existing.FormatVersion != ConnectorFormatVersion || existing.ContentHash != request.ContentHash || existing.ArtifactId !=
            EtlArtifactJson.CreateArtifactId(ConnectorFormatVersion, request.Id, request.DefinitionVersion, request.ContentHash))
            throw Conflict();
        return new("replay", Version(existing));
    }

    private static EtlConnectorDraftView View(EtlDefinitionDraftV2 draft) => new(
        draft.DefinitionId, draft.Revision, draft.LastPublishedVersion, draft.Pipeline, draft.UpdatedAt);

    private static EtlConnectorDraftView Draft(EtlDefinitionWriteResultV2 result) => result.Status switch
    {
        EtlDefinitionWriteStatus.Created or EtlDefinitionWriteStatus.Updated when result.Draft is not null => View(result.Draft),
        EtlDefinitionWriteStatus.NotFound => throw NotFound(),
        _ => throw Conflict()
    };

    private static EtlDiagnostic BindingUnavailableDiagnostic() => new("binding.alias.unavailable", "bindings",
        "The host does not currently approve the connector bindings for this scope.");
    private T Required<T>() where T : class => services.GetService<T>() ?? throw new EtlApiException(503, "etl.provider-unavailable");
}
