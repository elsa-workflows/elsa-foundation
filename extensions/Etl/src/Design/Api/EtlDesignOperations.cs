using Elsa.Etl.Api;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Design.Api.Models;
using Elsa.Etl.Design.Stores;
using Microsoft.Extensions.DependencyInjection;
using static Elsa.Etl.Design.Api.EtlDesignOperationSupport;

namespace Elsa.Etl.Design.Api;

internal sealed class EtlDesignOperations(IServiceProvider services, EtlRequestScope requestScope, [FromKeyedServices(EtlDesignApi.OwnerId)] EtlCursorCodec cursors) : IEtlDesignOperations
{
    public Task<EtlPage<EtlBindingSummary>> ListBindingsAsync(EtlPageRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scope = requestScope.Get();
        var cursor = Cursor(cursors, request.Cursor, request.Limit, "bindings", scope);
        var rows = Required<IEtlBindingCatalog>().ListBindings(scope, cursor?.LastAlias, cursor?.LastRole, request.Limit + 1);
        var items = rows.Take(request.Limit).ToArray();
        var next = rows.Count > request.Limit ? cursors.Encode(State("bindings", scope, request.Limit) with
        { LastAlias = items[^1].Alias, LastRole = items[^1].Role }) : null;
        return Task.FromResult(new EtlPage<EtlBindingSummary>(items, next));
    }

    public async Task<EtlPage<EtlDefinitionSummaryView>> ListDefinitionsAsync(EtlPageRequest request, CancellationToken cancellationToken)
    {
        var scope = requestScope.Get();
        var cursor = Cursor(cursors, request.Cursor, request.Limit, "definitions", scope);
        var rows = await Required<IEtlDefinitionCatalog>().ListDefinitionsAsync(scope, cursor?.LastId, request.Limit + 1, cancellationToken);
        var scanned = rows.Take(request.Limit).ToArray();
        var items = scanned.Where(row => row.FormatVersion == EtlArtifact.CurrentFormatVersion)
            .Select(row => new EtlDefinitionSummaryView(row.DefinitionId, row.Revision, row.LastPublishedVersion, row.UpdatedAt)).ToArray();
        var next = rows.Count > request.Limit ? cursors.Encode(State("definitions", scope, request.Limit) with { LastId = scanned[^1].DefinitionId }) : null;
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
        var scope = requestScope.Get();
        var draft = await FindDraftAsync(scope, request.Id, cancellationToken);
        Revision(request.ExpectedRevision);
        if (draft.Revision != request.ExpectedRevision) throw Conflict();
        var version = NextVersion(draft.LastPublishedVersion);
        var result = Required<IEtlArtifactCompiler>().Compile(draft.DefinitionId, version, draft.Pipeline);
        if (result.Artifact is not null && BindingDiagnostics(scope, draft.Pipeline.Bindings) is { Length: > 0 } diagnostics)
            return new(draft.Revision, version, null, diagnostics);
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
        // Approval can change after validation. This metadata check is independent of runtime admission,
        // which still resolves and pins the approved resources when a run is accepted.
        if (BindingDiagnostics(scope, draft.Pipeline.Bindings).Length > 0)
            return await FindReplayAsync(store, scope, request, cancellationToken) ?? throw new EtlApiException(400, "etl.validation-failed");
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
        var cursor = Cursor(cursors, request.Cursor, request.Limit, "versions", scope, request.Id);
        var ceiling = cursor?.ThroughVersion ?? await PublishedVersionCeilingAsync(scope, request.Id, cancellationToken);
        var rows = await Required<IEtlDefinitionCatalog>().ListVersionsAsync(scope, request.Id, cursor?.LastVersion ?? 0, ceiling, request.Limit + 1, cancellationToken);
        var scanned = rows.Take(request.Limit).ToArray();
        var items = scanned.Where(row => row.FormatVersion == EtlArtifact.CurrentFormatVersion).Select(row => new EtlPublishedVersionView(row.DefinitionId, row.DefinitionVersion,
            row.ContentHash, Selection(row.DefinitionId, row.DefinitionVersion, row.ContentHash).ToToken(), row.PublishedAt)).ToArray();
        var next = rows.Count > request.Limit ? cursors.Encode(State("versions", scope, request.Limit, request.Id) with
        { LastVersion = scanned[^1].DefinitionVersion, ThroughVersion = ceiling }) : null;
        return new(items, next);
    }

    public async Task<EtlPublishedArtifactView> GetVersionAsync(EtlVersionRequest request, CancellationToken cancellationToken)
    {
        var scope = requestScope.Get();
        ValidateId(request.Id);
        if (request.Version <= 0) throw new ArgumentOutOfRangeException(nameof(request));
        var artifact = await Required<IEtlDefinitionStore>().FindPublishedAsync(scope, request.Id, request.Version, cancellationToken) ?? throw NotFound();
        if (artifact.FormatVersion != EtlArtifact.CurrentFormatVersion) throw NotFound();
        return new(Version(artifact), EtlArtifactJson.Read(artifact.Pipeline));
    }

    private async Task<EtlDefinitionDraft> FindDraftAsync(EtlScope scope, string id, CancellationToken cancellationToken)
    {
        ValidateId(id);
        try
        {
            return await Required<IEtlDefinitionStore>().FindDraftAsync(scope, id, cancellationToken) ?? throw NotFound();
        }
        catch (NotSupportedException)
        {
            throw NotFound();
        }
    }

    private async Task<int> PublishedVersionCeilingAsync(EtlScope scope, string id, CancellationToken cancellationToken)
    {
        ValidateId(id);
        try
        {
            if (await Required<IEtlDefinitionStore>().FindDraftAsync(scope, id, cancellationToken) is { } v1Draft)
                return v1Draft.LastPublishedVersion;
        }
        catch (NotSupportedException)
        {
            // A v2 current draft can still have older v1 publications in the shared version history.
        }

        if (services.GetService<IEtlDefinitionStoreV2>() is { } v2Store)
        {
            try
            {
                if (await v2Store.FindDraftAsync(scope, id, cancellationToken) is { } v2Draft)
                    return v2Draft.LastPublishedVersion;
            }
            catch (NotSupportedException)
            {
                // The provider may not support the additive format capability.
            }
        }

        throw NotFound();
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

    private EtlDiagnostic[] BindingDiagnostics(EtlScope scope, EtlFileBindings bindings)
    {
        var catalog = Required<IEtlBindingCatalog>();
        (string Alias, string Role, string Path)[] required =
        [
            (bindings.Source, "Source", "bindings.source"),
            (bindings.Lookup, "Lookup", "bindings.lookup"),
            (bindings.Output, "MainOutput", "bindings.output"),
            (bindings.Errors, "ErrorOutput", "bindings.errors")
        ];
        return required.Where(binding => !catalog.ContainsBinding(scope, binding.Alias, binding.Role))
            .Select(binding => new EtlDiagnostic("binding.alias.unavailable", binding.Path,
                "The host does not currently approve this binding for the required role in this scope."))
            .ToArray();
    }

    private T Required<T>() where T : class => services.GetService<T>() ?? throw new EtlApiException(503, "etl.provider-unavailable");

    private static EtlDraftView View(EtlDefinitionDraft draft) => new(draft.DefinitionId, draft.Revision, draft.LastPublishedVersion, draft.Pipeline, draft.UpdatedAt);
    private static EtlDraftView Draft(EtlDefinitionWriteResult result) => result.Status switch
    {
        EtlDefinitionWriteStatus.Created or EtlDefinitionWriteStatus.Updated when result.Draft is not null => View(result.Draft),
        EtlDefinitionWriteStatus.NotFound => throw NotFound(),
        _ => throw Conflict()
    };
}
