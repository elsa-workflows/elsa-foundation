using Elsa.Workflows.Design.Api.Capabilities;
using Elsa.Workflows.Design.Api.Handlers;
using Elsa.Workflows.Design.Api.Requests;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Xunit;

namespace Elsa.Workflows.Design.Api.Tests.Unit;

public sealed class WorkflowDefinitionPagingCapabilityTests
{
    [Fact]
    public async Task Paged_workflow_definition_relation_is_advertised_only_when_a_bounded_store_is_active()
    {
        var absent = await new WorkflowDesignOperationalCapabilitySource().GetCapabilitiesAsync();
        var present = await new WorkflowDesignOperationalCapabilitySource(pageStore: new StubPagedStore()).GetCapabilitiesAsync();

        Assert.DoesNotContain(absent.SelectMany(declaration => declaration.Links), link => link.Rel == "workflow-definitions-page");
        var relation = Assert.Single(present.SelectMany(declaration => declaration.Links), link => link.Rel == "workflow-definitions-page");
        Assert.Equal("design/workflows/definitions/page", relation.Href);
    }

    [Fact]
    public async Task Paged_relation_is_not_advertised_for_a_registered_but_unadmitted_store()
    {
        var capabilities = await new WorkflowDesignOperationalCapabilitySource(pageStore: new StubPagedStore { IsAvailable = false })
            .GetCapabilitiesAsync();

        Assert.DoesNotContain(capabilities.SelectMany(declaration => declaration.Links), link => link.Rel == "workflow-definitions-page");
    }

    [Fact]
    public async Task Folder_relation_requires_both_the_folder_and_paged_definition_stores()
    {
        var folders = new StubFolderStore();
        var withoutPaging = await new WorkflowDesignOperationalCapabilitySource(folderStore: folders).GetCapabilitiesAsync();
        var withPaging = await new WorkflowDesignOperationalCapabilitySource(pageStore: new StubPagedStore(), folderStore: folders).GetCapabilitiesAsync();

        Assert.DoesNotContain(withoutPaging.SelectMany(x => x.Links), link => link.Rel == "workflow-folders");
        Assert.Contains(withPaging.SelectMany(x => x.Links), link => link.Rel == "workflow-folders");
    }

    [Fact]
    public async Task Definition_move_relation_requires_the_atomic_command_and_browse_dependencies()
    {
        var withoutCommand = await new WorkflowDesignOperationalCapabilitySource(
            pageStore: new StubPagedStore(), folderStore: new StubFolderStore()).GetCapabilitiesAsync();
        var withCommand = await new WorkflowDesignOperationalCapabilitySource(
            pageStore: new StubPagedStore(), folderStore: new StubFolderStore(), moveDefinitions: new StubMoveCommand()).GetCapabilitiesAsync();

        Assert.DoesNotContain(withoutCommand.SelectMany(x => x.Links), link => link.Rel == "workflow-definition-folder-move");
        var move = Assert.Single(withCommand.SelectMany(x => x.Links), link => link.Rel == "workflow-definition-folder-move");
        Assert.Equal("design/workflows/definitions/move", move.Href);
    }

    [Fact]
    public async Task Folder_restructure_relations_require_the_atomic_command_and_are_templated()
    {
        var capabilities = await new WorkflowDesignOperationalCapabilitySource(
            pageStore: new StubPagedStore(), folderStore: new StubFolderStore(), restructureFolders: new StubRestructureCommand()).GetCapabilitiesAsync();

        var links = capabilities.SelectMany(x => x.Links).Where(link => link.Rel.StartsWith("workflow-folder-", StringComparison.Ordinal)).ToArray();
        Assert.Equal(3, links.Length);
        Assert.All(links, link => Assert.True(link.Templated));
        Assert.Contains(links, link => link.Rel == "workflow-folder-rename" && link.Href == "design/workflows/folders/{folderId}/rename");
        Assert.Contains(links, link => link.Rel == "workflow-folder-move" && link.Href == "design/workflows/folders/{folderId}/move");
        Assert.Contains(links, link => link.Rel == "workflow-folder-delete-empty" && link.Href == "design/workflows/folders/{folderId}");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task Folder_read_handlers_reject_blank_identifiers_at_the_request_boundary(string folderId)
    {
        var store = new StubFolderStore();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new ListWorkflowFoldersRequestHandler(store).Handle(
                new ListWorkflowFolders(ParentId: folderId),
                CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            new GetWorkflowFolderRequestHandler(store).Handle(
                new GetWorkflowFolder(folderId),
                CancellationToken.None));
    }

    [Fact]
    public async Task Folder_read_handlers_reject_overlong_identifiers_at_the_request_boundary()
    {
        var store = new StubFolderStore();
        var folderId = new string('f', WorkflowFolderNames.MaximumIdentifierLength + 1);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            new ListWorkflowFoldersRequestHandler(store).Handle(
                new ListWorkflowFolders(ParentId: folderId),
                CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            new GetWorkflowFolderRequestHandler(store).Handle(
                new GetWorkflowFolder(folderId),
                CancellationToken.None));
    }

    [Fact]
    public async Task Paged_handler_composes_lifecycle_search_page_size_and_continuation()
    {
        var pageStore = new StubPagedStore
        {
            Result = new WorkflowDefinitionPage(
                [new WorkflowDefinition { Id = "definition-1", Name = "Orders" }],
                "next-token")
        };
        var handler = new ListWorkflowDefinitionPageRequestHandler(
            new PageStoreServiceProvider(pageStore),
            new EmptyProjectionStore());

        var response = await handler.Handle(
            new ListWorkflowDefinitionPage(999, "previous-token", "orders", "deleted"),
            CancellationToken.None);

        Assert.Equal(100, pageStore.Query!.PageSize);
        Assert.Equal("previous-token", pageStore.Query.ContinuationToken);
        Assert.Equal("orders", pageStore.Query.SearchTerm);
        Assert.Equal(WorkflowDefinitionPageState.Deleted, pageStore.Query.State);
        Assert.Equal("next-token", response.NextContinuationToken);
        Assert.Equal("definition-1", Assert.Single(response.Items).Id);
    }

    [Fact]
    public async Task Paged_handler_composes_mutually_exclusive_folder_selectors_for_the_store_to_validate()
    {
        var pageStore = new StubPagedStore();
        var handler = new ListWorkflowDefinitionPageRequestHandler(new PageStoreServiceProvider(pageStore), new EmptyProjectionStore());

        await Assert.ThrowsAsync<ArgumentException>(() => handler.Handle(
            new ListWorkflowDefinitionPage(FolderId: "folder-1", Unfiled: true), CancellationToken.None));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task Paged_handler_rejects_blank_folder_selectors(string folderId)
    {
        var pageStore = new StubPagedStore();
        var handler = new ListWorkflowDefinitionPageRequestHandler(new PageStoreServiceProvider(pageStore), new EmptyProjectionStore());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.Handle(new ListWorkflowDefinitionPage(FolderId: folderId), CancellationToken.None));

        Assert.Null(pageStore.Query);
    }

    [Fact]
    public async Task Paged_handler_rejects_an_overlong_folder_selector()
    {
        var pageStore = new StubPagedStore();
        var handler = new ListWorkflowDefinitionPageRequestHandler(new PageStoreServiceProvider(pageStore), new EmptyProjectionStore());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            handler.Handle(
                new ListWorkflowDefinitionPage(
                    FolderId: new string('f', WorkflowFolderNames.MaximumIdentifierLength + 1)),
                CancellationToken.None));

        Assert.Null(pageStore.Query);
    }

    [Fact]
    public async Task Paged_handler_rejects_an_unknown_folder_before_querying_definitions()
    {
        var pageStore = new StubPagedStore();
        var handler = new ListWorkflowDefinitionPageRequestHandler(
            new PageStoreServiceProvider(pageStore, new StubFolderStore()),
            new EmptyProjectionStore());

        await Assert.ThrowsAsync<EntityNotFoundException>(() => handler.Handle(
            new ListWorkflowDefinitionPage(FolderId: "missing"), CancellationToken.None));

        Assert.Null(pageStore.Query);
    }

    [Fact]
    public async Task Global_page_projects_root_to_folder_breadcrumbs_with_one_batched_folder_read()
    {
        var pageStore = new StubPagedStore
        {
            Result = new WorkflowDefinitionPage(
                [
                    new WorkflowDefinition { Id = "definition-1", Name = "One", FolderId = "child" },
                    new WorkflowDefinition { Id = "definition-2", Name = "Two", FolderId = "child" },
                    new WorkflowDefinition { Id = "definition-3", Name = "Three" }
                ],
                null)
        };
        var root = new WorkflowFolder { Id = "root", Name = "Root" };
        var child = new WorkflowFolder { Id = "child", Name = "Child", ParentFolderId = root.Id };
        var folderStore = new StubFolderStore(
            new Dictionary<string, WorkflowFolderDetails>(StringComparer.Ordinal)
            {
                [child.Id] = new(child, [root])
            });
        var handler = new ListWorkflowDefinitionPageRequestHandler(
            new PageStoreServiceProvider(pageStore, folderStore),
            new EmptyProjectionStore());

        var response = await handler.Handle(new ListWorkflowDefinitionPage(), CancellationToken.None);

        Assert.Equal(1, folderStore.BatchReadCount);
        Assert.All(
            response.Items.Where(item => item.FolderId == child.Id),
            item => Assert.Equal(
                [("root", "Root"), ("child", "Child")],
                item.FolderBreadcrumb!.Select(folder => (folder.Id, folder.Name))));
        Assert.Empty(Assert.Single(response.Items, item => item.FolderId is null).FolderBreadcrumb!);
    }

    [Fact]
    public async Task Direct_folder_page_composes_every_public_browse_input()
    {
        var pageStore = new StubPagedStore();
        var folder = new WorkflowFolder { Id = "folder-1", Name = "Folder" };
        var folderStore = new StubFolderStore(
            new Dictionary<string, WorkflowFolderDetails>(StringComparer.Ordinal)
            {
                [folder.Id] = new(folder, [])
            });
        var handler = new ListWorkflowDefinitionPageRequestHandler(
            new PageStoreServiceProvider(pageStore, folderStore),
            new EmptyProjectionStore());

        await handler.Handle(
            new ListWorkflowDefinitionPage(7, "token", "orders", "all", folder.Id, false),
            CancellationToken.None);

        Assert.Equal(
            new WorkflowDefinitionPageQuery(
                7,
                "orders",
                WorkflowDefinitionPageState.All,
                "token",
                folder.Id,
                false),
            pageStore.Query);
        Assert.Equal(1, folderStore.BatchReadCount);
    }

    [Fact]
    public async Task Paged_handler_rejects_a_filed_definition_whose_folder_cannot_be_resolved()
    {
        var pageStore = new StubPagedStore
        {
            Result = new WorkflowDefinitionPage(
                [new WorkflowDefinition { Id = "definition-1", Name = "Orders", FolderId = "missing" }],
                null)
        };
        var handler = new ListWorkflowDefinitionPageRequestHandler(
            new PageStoreServiceProvider(pageStore, new StubFolderStore()),
            new EmptyProjectionStore());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new ListWorkflowDefinitionPage(), CancellationToken.None));

        Assert.Contains("definition-1", exception.Message, StringComparison.Ordinal);
        Assert.Contains("missing", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, WorkflowDefinitionPageState.Active)]
    [InlineData("unexpected", WorkflowDefinitionPageState.Active)]
    [InlineData("deleted", WorkflowDefinitionPageState.Deleted)]
    [InlineData("all", WorkflowDefinitionPageState.All)]
    public async Task Paged_handler_parses_lifecycle_state(string? state, WorkflowDefinitionPageState expected)
    {
        var pageStore = new StubPagedStore();
        var handler = new ListWorkflowDefinitionPageRequestHandler(new PageStoreServiceProvider(pageStore), new EmptyProjectionStore());

        await handler.Handle(new ListWorkflowDefinitionPage(null, null, null, state), CancellationToken.None);

        Assert.Equal(expected, pageStore.Query!.State);
    }

    [Fact]
    public async Task Paged_handler_rejects_an_unavailable_runtime_before_processing_the_request()
    {
        var handler = new ListWorkflowDefinitionPageRequestHandler(new EmptyServiceProvider(), new EmptyProjectionStore());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new ListWorkflowDefinitionPage(null, null, null, null), CancellationToken.None));
    }

    private sealed class StubPagedStore : IWorkflowDefinitionPageStore
    {
        public bool IsAvailable { get; init; } = true;
        public WorkflowDefinitionPage? Result { get; init; }
        public WorkflowDefinitionPageQuery? Query { get; private set; }

        public Task<WorkflowDefinitionPage> QueryPageAsync(
            WorkflowDefinitionPageQuery query,
            CancellationToken cancellationToken = default)
        {
            Query = query;
            return Task.FromResult(Result ?? new WorkflowDefinitionPage([], null));
        }
    }

    private sealed class PageStoreServiceProvider(
        IWorkflowDefinitionPageStore pageStore,
        IWorkflowFolderStore? folderStore = null) : IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            serviceType == typeof(IWorkflowDefinitionPageStore)
                ? pageStore
                : serviceType == typeof(IWorkflowFolderStore)
                    ? folderStore
                    : null;
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private sealed class EmptyProjectionStore : IWorkflowDefinitionListProjectionStore
    {
        public Task<IReadOnlyList<WorkflowDefinitionListProjection>> ListByDefinitionIdsAsync(
            IReadOnlyCollection<string> workflowDefinitionIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WorkflowDefinitionListProjection>>([]);
    }

    private sealed class StubFolderStore(
        IReadOnlyDictionary<string, WorkflowFolderDetails>? detailsById = null) : IWorkflowFolderStore
    {
        private readonly IReadOnlyDictionary<string, WorkflowFolderDetails> _detailsById =
            detailsById ?? new Dictionary<string, WorkflowFolderDetails>(StringComparer.Ordinal);

        public bool IsAvailable => true;
        public int BatchReadCount { get; private set; }
        public Task<WorkflowFolderPage> ListDirectChildrenAsync(WorkflowFolderPageRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkflowFolderDetails?> FindWithAncestorsAsync(string folderId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_detailsById.GetValueOrDefault(folderId));
        public Task<IReadOnlyDictionary<string, WorkflowFolderDetails>> FindManyWithAncestorsAsync(
            IReadOnlyCollection<string> folderIds,
            CancellationToken cancellationToken = default)
        {
            BatchReadCount++;
            IReadOnlyDictionary<string, WorkflowFolderDetails> result = folderIds
                .Distinct(StringComparer.Ordinal)
                .Where(_detailsById.ContainsKey)
                .ToDictionary(id => id, id => _detailsById[id], StringComparer.Ordinal);
            return Task.FromResult(result);
        }
        public Task<WorkflowFolder> CreateAsync(WorkflowFolder folder, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class StubMoveCommand : IMoveWorkflowDefinitionsCommand
    {
        public Task Execute(IReadOnlyCollection<string> definitionIds, string? folderId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class StubRestructureCommand : IRestructureWorkflowFoldersCommand
    {
        public Task<WorkflowFolder> RenameAsync(string folderId, string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkflowFolder> MoveAsync(string folderId, string? parentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteEmptyAsync(string folderId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
