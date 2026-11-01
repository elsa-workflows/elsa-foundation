using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Elsa.Activities.Bpmn.Core;
using Elsa.Foundation.Identity.Authorization;
using NativeEndpoints;

namespace Elsa.Activities.Bpmn.Api.Endpoints;

internal sealed record BpmnCatalogRequest(int Limit = 50, string? ContinuationToken = null, string? ArtifactId = null);

// Resource filtering may empty one bounded provider page while later rows remain.
internal sealed record BpmnCatalogPage<T>(IReadOnlyList<T> Items, string? NextContinuationToken);
internal sealed record BpmnPublicationView(string SourceReferenceId, string SourceId, string? SourceVersion,
    WorkflowExecutableIdentity Executable, string MaterialIncarnationId, string Profile, string? DisplayName,
    DateTimeOffset CreatedAt, DateTimeOffset? PublishedAt);

internal sealed class BpmnCatalog(IWorkflowExecutableSourceReferenceStore sources, IWorkflowExecutableStore materials,
    IRuntimeModelExecutionReader executions, IPersistenceAccessContextAccessor access, BpmnApiCaller caller,
    IRuntimeRecoveryContinuationCodec continuations, TimeProvider clock)
{
    private const string PublicationsPurpose = "bpmn-publication-catalog-v1";
    private const string ExecutionsPurpose = "bpmn-execution-catalog-v1";
    private static readonly RuntimeExecutionProtocol Protocol = new(BpmnModelProtocol.Key, BpmnModelProtocol.Version);

    public async ValueTask<BpmnCatalogPage<BpmnPublicationView>> PublicationsAsync(BpmnCatalogRequest request, CancellationToken cancellationToken)
    {
        var context = Context(request, PublicationsPurpose, cancellationToken);
        var query = new WorkflowExecutableSourceReferencePageQuery(WorkflowExecutableReferenceScope.Published, true,
            new DateTimeOffset(context.Cursor.LiveUtcTicks, TimeSpan.Zero), request.Limit, context.Cursor.Raw);
        var page = await sources.ListPageAsync(query, cancellationToken);
        var items = new List<BpmnPublicationView>();
        foreach (var source in page.Items)
        {
            if (source.SourceKind != "BpmnModel" || source.Scope != WorkflowExecutableReferenceScope.Published ||
                source.TenantId != context.Caller.TenantId || !source.IsLive(clock.GetUtcNow()) ||
                request.ArtifactId is not null && source.ArtifactId != request.ArtifactId ||
                !await caller.CanReadAsync(source, cancellationToken)) continue;
            var material = await materials.FindMaterialAsync(source.ArtifactId, cancellationToken);
            if (material?.Model is not { } model || model.Protocol != Protocol ||
                material.Identity.ArtifactId != source.ArtifactId || material.Identity.DefinitionId != source.DefinitionId ||
                material.Identity.DefinitionVersionId != source.DefinitionVersionId || material.Identity.ArtifactVersion != source.ArtifactVersion) continue;
            items.Add(new(source.SourceReferenceId, source.SourceId, source.SourceVersion, material.Identity, material.IncarnationId,
                model.Profile, source.ActivityPresentation?.FirstOrDefault()?.DisplayName, source.CreatedAt, source.PublishedAt));
        }
        return new(items, Next(PublicationsPurpose, context.Cursor, page.NextContinuationToken));
    }

    public async ValueTask<BpmnCatalogPage<BpmnExecutionView>> ExecutionsAsync(BpmnCatalogRequest request, CancellationToken cancellationToken)
    {
        var context = Context(request, ExecutionsPurpose, cancellationToken);
        var page = await executions.ListPageAsync(new(request.Limit, context.Cursor.Raw), cancellationToken);
        var items = new List<BpmnExecutionView>();
        foreach (var state in page.Items)
        {
            if (state.Scope != context.Scope || state.TenantId != context.Caller.TenantId || state.Protocol != Protocol ||
                request.ArtifactId is not null && state.Executable.ArtifactId != request.ArtifactId ||
                !await caller.CanReadAsync(state, cancellationToken)) continue;
            items.Add(new(state.ExecutionId, state.Revision, state.Lifecycle.ToString(), state.Protocol, state.Executable,
                state.MaterialIncarnationId, state.CreatedAt, state.UpdatedAt, state.TerminalResult));
        }
        return new(items, Next(ExecutionsPurpose, context.Cursor, page.NextContinuationToken));
    }

    private (BpmnModelCaller Caller, PersistenceScope Scope, Cursor Cursor) Context(BpmnCatalogRequest request, string purpose,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var identity = caller.Current();
        var scope = access.Current.RequireScope();
        access.Current.EnsureTenantScope(identity.TenantId);
        RuntimeStorePageRequest.ValidateLimit(request.Limit, nameof(request.Limit));
        RuntimeStorePageRequest.ValidateContinuationToken(request.ContinuationToken, nameof(request.ContinuationToken));
        if (request.ArtifactId is not null) ArgumentException.ThrowIfNullOrWhiteSpace(request.ArtifactId);
        var binding = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new[]
            { scope.Value, identity.ActorId, identity.TenantId, request.ArtifactId, request.Limit.ToString(CultureInfo.InvariantCulture) })));
        if (request.ContinuationToken is null) return (identity, scope, new(binding, null, clock.GetUtcNow().UtcTicks));
        try
        {
            var cursor = JsonSerializer.Deserialize<Cursor>(continuations.Decode(purpose, request.ContinuationToken));
            if (cursor is null || cursor.Binding != binding || cursor.Raw is null ||
                cursor.LiveUtcTicks <= DateTimeOffset.MinValue.UtcTicks || cursor.LiveUtcTicks >= DateTimeOffset.MaxValue.UtcTicks)
                throw new ArgumentException("Invalid catalog continuation.");
            RuntimeStorePageRequest.ValidateContinuationToken(cursor.Raw, nameof(request.ContinuationToken));
            return (identity, scope, cursor);
        }
        catch (Exception error) when (error is ArgumentException or FormatException or JsonException or InvalidDataException or InvalidOperationException)
        {
            throw new ArgumentException("The BPMN catalog continuation is invalid or belongs to another context.", nameof(request.ContinuationToken), error);
        }
    }

    private string? Next(string purpose, Cursor cursor, string? raw) => raw is null ? null :
        continuations.Encode(purpose, JsonSerializer.SerializeToUtf8Bytes(cursor with { Raw = raw }));
    private sealed record Cursor(string Binding, string? Raw, long LiveUtcTicks);
}

[Get("bpmn/publications")]
[RequirePermission(BpmnHostingApi.Read)]
internal sealed class ListPublicationsEndpoint(BpmnCatalog catalog) : ApiEndpoint<BpmnCatalogRequest, BpmnCatalogPage<BpmnPublicationView>>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "ListBpmnPublications";
    public override Task<BpmnCatalogPage<BpmnPublicationView>> HandleAsync(BpmnCatalogRequest request, CancellationToken cancellationToken) =>
        catalog.PublicationsAsync(request, cancellationToken).AsTask();
}

[Get("bpmn/executions")]
[RequirePermission(BpmnHostingApi.Read)]
internal sealed class ListExecutionsEndpoint(BpmnCatalog catalog) : ApiEndpoint<BpmnCatalogRequest, BpmnCatalogPage<BpmnExecutionView>>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "ListBpmnExecutions";
    public override Task<BpmnCatalogPage<BpmnExecutionView>> HandleAsync(BpmnCatalogRequest request, CancellationToken cancellationToken) =>
        catalog.ExecutionsAsync(request, cancellationToken).AsTask();
}
