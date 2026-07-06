using Elsa.Persistence.EFCore.Contracts;
using Elsa.Persistence.EFCore.Extensions;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText.Services;
using Elsa.Workflows.Design.Persistence.EFCore.DbContext;
using Elsa.Workflows.Design.Persistence.EFCore.EntityHandlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using DraftEntity = Elsa.Workflows.Design.Persistence.Core.Entities.WorkflowDefinitionDraft;
using VersionEntity = Elsa.Workflows.Design.Persistence.Core.Entities.WorkflowDefinitionVersion;

namespace Elsa.Workflows.Design.Tests.Unit;

/// <summary>
/// Pins that the production assembly-scan registration (<see cref="ServiceCollectionExtensions.AddEntityLoadingHandlersFrom"/>
/// / <c>AddEntitySavingHandlersFrom</c>) still resolves the concrete state-source handlers for both
/// the Draft and Version entities after issue #417 item 5 folded them onto shared abstract bases
/// (<see cref="StateSourceLoadingHandlerBase{TEntity}"/> / <see cref="StateSourceSavingHandlerBase{TEntity}"/>).
/// The scan has no <c>IsAbstract</c>/open-generic guard, so this test also guards that the abstract
/// generic bases do not poison the container.
/// </summary>
public sealed class StateSourceHandlerRegistrationTests
{
    private static ServiceProvider BuildScannedContainer()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IPayloadSerializer>(new JsonPayloadSerializer(new JsonPayloadConverterRegistry()));
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        var assembly = typeof(WorkflowDefinitionDraftLoadingHandler).Assembly;
        services.AddEntityLoadingHandlersFrom(assembly);
        services.AddEntitySavingHandlersFrom(assembly);

        return services.BuildServiceProvider(validateScopes: true);
    }

    [Fact]
    public void Loading_handlers_resolve_for_both_draft_and_version()
    {
        using var provider = BuildScannedContainer();
        using var scope = provider.CreateScope();
        var sp = scope.ServiceProvider;

        var draftLoading = sp.GetService<IEntityLoadingHandler<WorkflowsDesignDbContext, DraftEntity>>();
        var versionLoading = sp.GetService<IEntityLoadingHandler<WorkflowsDesignDbContext, VersionEntity>>();

        Assert.IsType<WorkflowDefinitionDraftLoadingHandler>(draftLoading);
        Assert.IsType<WorkflowDefinitionVersionLoadingHandler>(versionLoading);
    }

    [Fact]
    public void Saving_handlers_resolve_for_both_draft_and_version()
    {
        using var provider = BuildScannedContainer();
        using var scope = provider.CreateScope();
        var sp = scope.ServiceProvider;

        var draftSaving = sp.GetService<IEntitySavingHandler<WorkflowsDesignDbContext, DraftEntity>>();
        var versionSaving = sp.GetService<IEntitySavingHandler<WorkflowsDesignDbContext, VersionEntity>>();

        Assert.IsType<WorkflowDefinitionDraftSavingHandler>(draftSaving);
        Assert.IsType<WorkflowDefinitionVersionSavingHandler>(versionSaving);
    }
}
