using Elsa.Events.Core.Contracts;
using Elsa.Locking.Core;
using Elsa.Workflows.Design.Api.Endpoints.Definitions;
using Elsa.Workflows.Design.Api.Endpoints.Definitions.Add;
using Elsa.Workflows.Design.Api.Endpoints.Definitions.Submit;
using Elsa.Workflows.Design.Api.Endpoints.Definitions.Update;
using Elsa.Workflows.Design.Api.Endpoints.Drafts.Promote;
using Elsa.Workflows.Design.Api.Endpoints.Drafts.Replace;
using Elsa.Workflows.Design.Api.Endpoints.Versions;
using Elsa.Workflows.Design.Api.Endpoints.Versions.Add;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Projections;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using AddDefinitionEndpoint = Elsa.Workflows.Design.Api.Endpoints.Definitions.Add.Endpoint;
using AddVersionEndpoint = Elsa.Workflows.Design.Api.Endpoints.Versions.Add.Endpoint;
using PromoteDraftEndpoint = Elsa.Workflows.Design.Api.Endpoints.Drafts.Promote.Endpoint;
using ReplaceDraftEndpoint = Elsa.Workflows.Design.Api.Endpoints.Drafts.Replace.Endpoint;
using SubmitDefinitionEndpoint = Elsa.Workflows.Design.Api.Endpoints.Definitions.Submit.Endpoint;
using UpdateDefinitionHandler = Elsa.Workflows.Design.Api.Endpoints.Definitions.Update.Handler;

namespace Elsa.Workflows.Design.Tests.Infrastructure;

/// <summary>
/// The Design API callers that write workflow state, over the real EF design store on SQLite and the real
/// credential-literal admission, with the catalog of <see cref="CredentialLiteralTestSupport"/> (spec 188, T051, T052,
/// T113). A caller is built in a fresh request scope and called as the endpoint framework calls it.
/// </summary>
/// <remarks>
/// By default the host composes the real inline event publisher, so the promotion command's in-lock validation gate runs
/// every draft validator, the credential-literal validator included. <c>composeInlinePublisher: false</c> composes the
/// promotion command without it, so nothing but the caller's admission can refuse a promotion.
/// </remarks>
internal sealed class DesignAdmissionTestHost : IAsyncDisposable
{
    private readonly string _directory;
    private readonly ServiceProvider _services;

    private DesignAdmissionTestHost(string directory, ServiceProvider services)
    {
        _directory = directory;
        _services = services;
    }

    public static async Task<DesignAdmissionTestHost> CreateAsync(bool composeInlinePublisher = true, Action<IServiceCollection>? configure = null)
    {
        var directory = WorkflowsDesignTestHost.NewDirectory();
        var services = WorkflowsDesignTestHost.DesignStoreServices(directory);
        services.AddSingleton<IDistributedLockProvider>(new InMemoryDistributedLockProvider());
        // Lifecycle events published after a write are not under test; capture them instead of starting the worker.
        services.AddSingleton<IDeferredEventPublisher>(new CapturingEventPublisher());
        services.AddScoped<IActivityStructureService, DefaultActivityStructureService>();
        services.AddScoped<IWorkflowDefinitionDetailsReader, WorkflowDefinitionDetailsReader>();
        services.AddScoped<IWorkflowVersionDetailsReader, WorkflowVersionDetailsReader>();
        if (!composeInlinePublisher)
            services.RemoveAll<IInlineEventPublisher>();
        configure?.Invoke(services);

        return new DesignAdmissionTestHost(directory, await WorkflowsDesignTestHost.BuildAsync(services));
    }

    /// <summary>Definitions/Add with <paramref name="state"/> as the initial draft state.</summary>
    public Task<WorkflowDefinitionDetailsView> AddDefinitionAsync(WorkflowDefinitionState state) =>
        CallAsync<AddDefinitionEndpoint, WorkflowDefinitionDetailsView>(endpoint =>
            endpoint.HandleAsync(new AddDefinition(null, "Definition", null, state.ToStateView()), CancellationToken.None));

    /// <summary>Drafts/Replace of draft <paramref name="draftId"/>.</summary>
    public Task<WorkflowDraftView> ReplaceDraftAsync(string draftId, WorkflowDefinitionState state) =>
        CallAsync<ReplaceDraftEndpoint, WorkflowDraftView>(endpoint =>
            endpoint.HandleAsync(new ReplaceDraft(null, draftId, state.ToStateView()), CancellationToken.None));

    /// <summary>Definitions/Update of definition <paramref name="definitionId"/>'s draft.</summary>
    public Task<WorkflowDefinitionDetailsView> UpdateDefinitionAsync(string definitionId, WorkflowDefinitionState state) =>
        CallAsync<UpdateDefinitionHandler, WorkflowDefinitionDetailsView>(handler =>
            handler.Handle(new UpdateDefinition(null, definitionId, state.ToStateView()), CancellationToken.None));

    /// <summary>Versions/Add to definition <paramref name="definitionId"/>.</summary>
    public Task<WorkflowDefinitionVersionDetailsView> AddVersionAsync(string definitionId, WorkflowDefinitionState state) =>
        CallAsync<AddVersionEndpoint, WorkflowDefinitionVersionDetailsView>(endpoint =>
            endpoint.HandleAsync(new AddVersion(null, definitionId, state.ToStateView()), CancellationToken.None));

    /// <summary>Definitions/Submit of a new definition.</summary>
    public Task<SubmittedWorkflowDefinitionView> SubmitAsync(WorkflowDefinitionState state) =>
        CallAsync<SubmitDefinitionEndpoint, SubmittedWorkflowDefinitionView>(endpoint =>
            endpoint.HandleAsync(new SubmitDefinition(null, "Submitted", null, state.ToStateView()), CancellationToken.None));

    /// <summary>Drafts/Promote of draft <paramref name="draftId"/>.</summary>
    public Task<WorkflowDefinitionVersionDetailsView> PromoteAsync(string draftId, string? operationKey = null) =>
        CallAsync<PromoteDraftEndpoint, WorkflowDefinitionVersionDetailsView>(endpoint =>
            endpoint.HandleAsync(new PromoteDraft(operationKey, draftId), CancellationToken.None));

    /// <summary>
    /// Stores <paramref name="state"/> into a draft through the update command itself, around every admission, as a draft
    /// saved before the rule existed, or before its activity was installed, would hold it.
    /// </summary>
    public Task StoreDraftDirectlyAsync(string draftId, WorkflowDefinitionState state) => InScopeAsync(async services =>
    {
        await services.GetRequiredService<IUpdateDraftCommand>().Execute(
            DesignOperationKey.CreateOrGenerate(null),
            new UpdateDraftRequest(draftId, state, []));
        return true;
    });

    /// <summary>Builds <typeparamref name="TCaller"/> in a fresh request scope and runs <paramref name="call"/> on it.</summary>
    public async Task<TResult> CallAsync<TCaller, TResult>(Func<TCaller, Task<TResult>> call) where TCaller : class
    {
        await using var scope = _services.CreateAsyncScope();
        return await call(ActivatorUtilities.CreateInstance<TCaller>(scope.ServiceProvider));
    }

    /// <summary>Runs <paramref name="action"/> against the services of a fresh scope.</summary>
    public async Task<TResult> InScopeAsync<TResult>(Func<IServiceProvider, Task<TResult>> action)
    {
        await using var scope = _services.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    /// <summary>The number of definition, draft and version rows.</summary>
    public Task<(int Definitions, int Drafts, int Versions)> CountRowsAsync() => InScopeAsync(async services =>
    {
        var db = services.GetRequiredService<WorkflowsDesignDbContext>();
        return (await db.Definitions.CountAsync(), await db.Drafts.CountAsync(), await db.Versions.CountAsync());
    });

    /// <summary>Whether any stored draft or version state source contains <paramref name="text"/>.</summary>
    public Task<bool> AnyStoredStateContainsAsync(string text) => InScopeAsync(async services =>
    {
        var db = services.GetRequiredService<WorkflowsDesignDbContext>();
        var sources = (await db.Drafts.AsNoTracking().Select(draft => draft.StateSource).ToListAsync())
            .Concat(await db.Versions.AsNoTracking().Select(version => version.StateSource).ToListAsync());
        return sources.Any(source => source?.Contains(text, StringComparison.Ordinal) == true);
    });

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        WorkflowsDesignTestHost.DeleteDirectory(_directory);
    }
}
