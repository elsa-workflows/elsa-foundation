using Elsa.Agent.Specialists.Core.Contracts;
using Elsa.Agent.Specialists.Core.Models;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Authorization;
using MinimalEndpoints;

namespace Elsa.Agent.Specialists.Api.Endpoints;

internal sealed record AgentSourceCreate(string AgentId, string DraftId, string Name, AgentSourceContent Content);
internal sealed record AgentSourceSave(string AgentId, string DraftId, long ExpectedRevision, string Name, AgentSourceContent Content);
internal sealed record AgentSourceRead(string AgentId, string DraftId);
internal sealed record AgentSourceRevisionRead(string AgentId, string DraftId, string SnapshotId);
internal sealed record AgentSourceHistory(string AgentId, string DraftId, int Skip = 0, int Take = 100);
internal sealed record AgentSourceFork(string AgentId, string DraftId, string SnapshotId, string NewAgentId, string NewDraftId, string Name);
internal sealed record AgentSourceList(string? Search = null, int Skip = 0, int Take = 50);
internal sealed record AgentSourceCapture(string AgentId, string DraftId, long ExpectedRevision, string OperationId);
internal sealed record AgentSourceDiscard(string AgentId, string DraftId, long ExpectedRevision);

[Get("/agent/definitions")]
[RequirePermission(AgentSourcePermissions.Read)]
internal sealed class ListAgentSources(IAgentSourceReader sources) : ApiEndpoint<AgentSourceList, AgentSourcePage>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "AgentSourcesList";
    public override Task<AgentSourcePage> HandleAsync(AgentSourceList request, CancellationToken cancellationToken) => sources.ListAsync(request.Search, request.Skip, request.Take, cancellationToken);
}

[Get("/agent/definitions/{agentId}/drafts/{draftId}")]
[RequirePermission(AgentSourcePermissions.Read)]
internal sealed class GetAgentSource(IAgentSourceReader sources) : ApiEndpoint<AgentSourceRead, AgentSourceDraft>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "AgentSourcesGet";
    public override async Task<AgentSourceDraft> HandleAsync(AgentSourceRead request, CancellationToken cancellationToken) => await sources.FindAsync(request.AgentId, request.DraftId, cancellationToken) ?? throw new Elsa.Persistence.Core.SourceRevisionException("not-found");
}

[Post("/agent/definitions")]
[RequirePermission(AgentSourcePermissions.Manage)]
internal sealed class CreateAgentSource(IAgentSourceWriter sources) : ApiEndpoint<AgentSourceCreate, AgentSourceDraft>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "AgentSourcesCreate";
        options.Accepts = ["application/json"];
        options.BodyMode = EndpointBodyMode.RequiredWithContentType;
        options.StrictTypedParsing = true;
    }
    public override Task<AgentSourceDraft> HandleAsync(AgentSourceCreate request, CancellationToken cancellationToken) => sources.CreateAsync(request.AgentId, request.DraftId, request.Name, request.Content, cancellationToken);
}

[Put("/agent/definitions/{agentId}/drafts/{draftId}")]
[RequirePermission(AgentSourcePermissions.Manage)]
internal sealed class SaveAgentSource(IAgentSourceWriter sources) : ApiEndpoint<AgentSourceSave, AgentSourceDraft>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "AgentSourcesSave";
        options.Accepts = ["application/json"];
        options.BodyMode = EndpointBodyMode.RequiredWithContentType;
        options.StrictTypedParsing = true;
    }
    public override Task<AgentSourceDraft> HandleAsync(AgentSourceSave request, CancellationToken cancellationToken) =>
        sources.SaveAsync(request.AgentId, request.DraftId, request.ExpectedRevision, request.Name, request.Content, cancellationToken);

}

[Post("/agent/definitions/{agentId}/drafts/{draftId}/captures")]
[RequirePermission(AgentSourcePermissions.Manage)]
internal sealed class CaptureAgentSource(IAgentSourceWriter sources) : ApiEndpoint<AgentSourceCapture, AgentSourceRevision>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "AgentSourcesCapture";
        options.Accepts = ["application/json"];
        options.BodyMode = EndpointBodyMode.RequiredWithContentType;
        options.StrictTypedParsing = true;
    }
    public override Task<AgentSourceRevision> HandleAsync(AgentSourceCapture request, CancellationToken cancellationToken) => sources.CaptureAsync(request.AgentId, request.DraftId, request.ExpectedRevision, request.OperationId, cancellationToken);
}

[Get("/agent/definitions/{agentId}/drafts/{draftId}/revisions")]
[RequirePermission(AgentSourcePermissions.Read)]
internal sealed class ListAgentSourceHistory(IAgentSourceReader sources) : ApiEndpoint<AgentSourceHistory, IReadOnlyList<AgentSourceRevision>>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "AgentSourcesHistory";
    public override Task<IReadOnlyList<AgentSourceRevision>> HandleAsync(AgentSourceHistory request, CancellationToken cancellationToken) => sources.HistoryAsync(request.AgentId, request.DraftId, request.Skip, request.Take, cancellationToken);
}

[Post("/agent/definitions/{agentId}/drafts/{draftId}/discard")]
[RequirePermission(AgentSourcePermissions.Manage)]
internal sealed class DiscardAgentSource(IAgentSourceWriter sources) : ApiEndpoint<AgentSourceDiscard, bool>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "AgentSourcesDiscard";
        options.Accepts = ["application/json"];
        options.BodyMode = EndpointBodyMode.RequiredWithContentType;
        options.StrictTypedParsing = true;
    }
    public override async Task<bool> HandleAsync(AgentSourceDiscard request, CancellationToken cancellationToken)
    {
        await sources.DiscardAsync(request.AgentId, request.DraftId, request.ExpectedRevision, cancellationToken);
        return true;
    }
}

[Post("/agent/definitions/{agentId}/drafts/{draftId}/fork")]
[RequirePermission(AgentSourcePermissions.Manage)]
internal sealed class ForkAgentSource(IAgentSourceWriter sources) : ApiEndpoint<AgentSourceFork, AgentSourceDraft>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "AgentSourcesFork";
        options.Accepts = ["application/json"];
        options.BodyMode = EndpointBodyMode.RequiredWithContentType;
        options.StrictTypedParsing = true;
    }
    public override Task<AgentSourceDraft> HandleAsync(AgentSourceFork request, CancellationToken cancellationToken) =>
        sources.ForkAsync(request.AgentId, request.DraftId, request.SnapshotId, request.NewAgentId, request.NewDraftId, request.Name, cancellationToken);
}

[Get("/agent/definitions/{agentId}/drafts/{draftId}/revisions/{snapshotId}")]
[RequirePermission(AgentSourcePermissions.Read)]
internal sealed class GetAgentSourceRevision(IAgentSourceReader sources) : ApiEndpoint<AgentSourceRevisionRead, AgentSourceRevision>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "AgentSourcesRevisionGet";
    public override async Task<AgentSourceRevision> HandleAsync(AgentSourceRevisionRead request, CancellationToken cancellationToken) =>
        await sources.FindRevisionAsync(request.AgentId, request.DraftId, request.SnapshotId, cancellationToken) ?? throw new Elsa.Persistence.Core.SourceRevisionException("not-found");
}
