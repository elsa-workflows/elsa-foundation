using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Elsa.Activities.Design.Api;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Core.Models;
using Elsa.Api.Capabilities;
using Elsa.Api.Capabilities.Models;
using Elsa.Expressions;
using Elsa.Expressions.Api;
using Elsa.Expressions.Api.Models;
using Elsa.Expressions.Api.Requests;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Api;
using Elsa.Workflows.Design.Api.Commands;
using Elsa.Workflows.Design.Api.Handlers;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Requests;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Filters;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Services;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Primitives.Contracts;
using Elsa.Workflows.Publishing.Api;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Api;
using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Elsa.Architecture.Tests;

public sealed class DomainManagementApiCompositionTests
{
    private static readonly string[] ExpectedCapabilities =
    [
        "elsa.api.activity-design",
        "elsa.api.expressions",
        "elsa.api.publishing",
        "elsa.api.runtime",
        "elsa.api.workflow-design"
    ];

    [Fact]
    public async Task Custom_host_exposes_representative_domain_journeys_without_Elsa_Server()
    {
        await using var host = await CustomManagementHost.StartAsync(includeExpressions: true);

        Assert.DoesNotContain(
            typeof(DomainManagementApiCompositionTests).Assembly.GetReferencedAssemblies(),
            reference => string.Equals(reference.Name, "Elsa.Server", StringComparison.Ordinal));

        await host.AssertJourneyAsync(HttpMethod.Get, "/design/workflows/definitions");
        await host.AssertJourneyAsync(HttpMethod.Get, "/design/activities/catalog");
        await host.AssertJourneyAsync(HttpMethod.Get, "/expressions/descriptors");
        await host.AssertJourneyAsync(HttpMethod.Post, "/publishing/workflows/version-1/publish", new { });
        await host.AssertJourneyAsync(HttpMethod.Get, "/runtime/workflows/executables");

        var expressions = await host.Client.GetFromJsonAsync<ExpressionDescriptorsResponse>("/expressions/descriptors");
        Assert.NotNull(expressions);
        Assert.Equal(new[] { "Input", "Literal", "Object", "Variable" }, expressions.Items.Select(x => x.Type));
        Assert.Equal(
            new[]
            {
                ExpressionEditingModeView.Reference,
                ExpressionEditingModeView.Literal,
                ExpressionEditingModeView.Structured,
                ExpressionEditingModeView.Reference
            },
            expressions.Items.Select(x => x.EditingMode));

        var capabilities = await host.Client.GetFromJsonAsync<ApiCapabilitiesDocument>("/capabilities");
        Assert.NotNull(capabilities);
        Assert.Equal(ExpectedCapabilities, capabilities.Capabilities.Select(x => x.Id).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Omitted_domain_has_neither_routes_nor_capability_while_installed_domains_remain_available()
    {
        await using var host = await CustomManagementHost.StartAsync(includeExpressions: false);

        await host.AssertJourneyAsync(HttpMethod.Get, "/design/workflows/definitions");
        await host.AssertJourneyAsync(HttpMethod.Get, "/design/activities/catalog");
        await host.AssertJourneyAsync(HttpMethod.Post, "/publishing/workflows/version-1/publish", new { });
        await host.AssertJourneyAsync(HttpMethod.Get, "/runtime/workflows/executables");

        var omittedRoute = await host.Client.SendAsync(new HttpRequestMessage(HttpMethod.Options, "/expressions/descriptors"));
        Assert.Equal(HttpStatusCode.NotFound, omittedRoute.StatusCode);

        var capabilities = await host.Client.GetFromJsonAsync<ApiCapabilitiesDocument>("/capabilities");
        Assert.NotNull(capabilities);
        Assert.Equal(
            ExpectedCapabilities.Where(x => x != "elsa.api.expressions"),
            capabilities.Capabilities.Select(x => x.Id).Order(StringComparer.Ordinal));
        Assert.DoesNotContain(
            capabilities.Capabilities.SelectMany(capability => capability.Links),
            link => link.Rel == "workflow-definitions-page");
    }

    [Fact]
    public async Task Activity_availability_diagnostics_is_retrievable_without_request_input()
    {
        await using var host = await CustomManagementHost.StartAsync(includeExpressions: false);

        var diagnostics = await host.Client.GetFromJsonAsync<ActivityAvailabilityDiagnostics>(
            "/design/activities/availability/diagnostics");

        Assert.NotNull(diagnostics);
        Assert.Empty(diagnostics.Items);
        Assert.Empty(diagnostics.Sets);
    }

    [Fact]
    public async Task Workflow_definition_page_is_capability_discovered_and_preserves_the_public_http_contract()
    {
        await using var host = await CustomManagementHost.StartAsync(includeExpressions: false, includePaging: true);

        var capabilities = await host.Client.GetFromJsonAsync<ApiCapabilitiesDocument>("/capabilities");
        var relation = Assert.Single(
            capabilities!.Capabilities.SelectMany(capability => capability.Links),
            link => link.Rel == "workflow-definitions-page");
        Assert.Equal("design/workflows/definitions/page", relation.Href);

        var page = await host.Client.GetFromJsonAsync<WorkflowDefinitionPageView>(
            "/design/workflows/definitions/page?pageSize=1&search=definition-42&state=deleted");
        var item = Assert.Single(page!.Items);
        Assert.Equal("definition-42", item.Id);
        Assert.NotNull(item.DeletedAt);
        Assert.Equal("draft-42", item.DraftId);
        Assert.Equal("version-42", item.LatestVersionId);
        Assert.Equal("2.3.4", item.LatestVersion);
        Assert.Equal(7, item.VersionCount);
        Assert.Equal("next-http-token", page.NextContinuationToken);

        using var malformed = await host.Client.GetAsync(
            "/design/workflows/definitions/page?continuationToken=malformed");
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        Assert.Equal("application/problem+json", malformed.Content.Headers.ContentType?.MediaType);

        using var mismatched = await host.Client.GetAsync(
            "/design/workflows/definitions/page?search=other&continuationToken=context-token");
        Assert.Equal(HttpStatusCode.BadRequest, mismatched.StatusCode);
        Assert.Equal("application/problem+json", mismatched.Content.Headers.ContentType?.MediaType);

        using var legacy = await host.Client.GetAsync("/design/workflows/definitions?state=all");
        legacy.EnsureSuccessStatusCode();
        using var legacyJson = await JsonDocument.ParseAsync(await legacy.Content.ReadAsStreamAsync());
        Assert.Equal(["items"], legacyJson.RootElement.EnumerateObject().Select(property => property.Name));
    }

    [Fact]
    public async Task Workflow_folder_routes_are_capability_discovered_and_use_the_public_page_contract()
    {
        await using var host = await CustomManagementHost.StartAsync(includeExpressions: false, includePaging: true, includeFolders: true);
        var capabilities = await host.Client.GetFromJsonAsync<ApiCapabilitiesDocument>("/capabilities");
        Assert.Contains(capabilities!.Capabilities.SelectMany(x => x.Links), link => link.Rel == "workflow-folders");

        var page = await host.Client.GetFromJsonAsync<WorkflowFolderListView>("/design/workflows/folders?pageSize=1");
        Assert.Equal("folder-1", Assert.Single(page!.Items).Id);
        Assert.Equal("folder-token", page.NextContinuationToken);
        var detail = await host.Client.GetFromJsonAsync<WorkflowFolderDetailsView>("/design/workflows/folders/folder-1");
        Assert.Equal("folder-1", detail!.Folder.Id);

        using var created = await host.Client.PostAsJsonAsync("/design/workflows/folders", new { name = "Created" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using var malformed = await host.Client.GetAsync("/design/workflows/folders?continuationToken=malformed");
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);

        using var unknown = await host.Client.GetAsync("/design/workflows/folders/missing");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        using var duplicate = await host.Client.PostAsJsonAsync("/design/workflows/folders", new { name = "Duplicate" });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        using var missingParent = await host.Client.PostAsJsonAsync("/design/workflows/folders", new { name = "Child", parentId = "missing" });
        Assert.Equal(HttpStatusCode.NotFound, missingParent.StatusCode);

        var folderDefinitions = await host.Client.GetFromJsonAsync<WorkflowDefinitionPageView>(
            "/design/workflows/definitions/page?folderId=folder-1");
        Assert.Equal("folder-1", Assert.Single(folderDefinitions!.Items).FolderId);

        var unfiledDefinitions = await host.Client.GetFromJsonAsync<WorkflowDefinitionPageView>(
            "/design/workflows/definitions/page?unfiled=true");
        Assert.Null(Assert.Single(unfiledDefinitions!.Items).FolderId);

        using var mutuallyExclusiveSelectors = await host.Client.GetAsync(
            "/design/workflows/definitions/page?folderId=folder-1&unfiled=true");
        Assert.Equal(HttpStatusCode.BadRequest, mutuallyExclusiveSelectors.StatusCode);

        using var definitionCreated = await host.Client.PostAsJsonAsync(
            "/design/workflows/definitions",
            new { name = "Filed workflow", folderId = "folder-1" });
        definitionCreated.EnsureSuccessStatusCode();
        var createdDefinition = await definitionCreated.Content.ReadFromJsonAsync<WorkflowDefinitionDetailsView>();
        Assert.Equal("folder-1", createdDefinition!.Definition.FolderId);

        var persistedDefinition = await host.Client.GetFromJsonAsync<WorkflowDefinitionDetailsView>(
            $"/design/workflows/definitions/{createdDefinition.Definition.Id}");
        Assert.Equal("folder-1", persistedDefinition!.Definition.FolderId);

        using var unexpected = await host.Client.PostAsJsonAsync("/design/workflows/folders", new { name = "Unexpected" });
        Assert.Equal(HttpStatusCode.InternalServerError, unexpected.StatusCode);
        Assert.DoesNotContain("provider-secret", await unexpected.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Workflow_folder_routes_and_capability_are_omitted_when_the_folder_store_is_unavailable()
    {
        await using var host = await CustomManagementHost.StartAsync(includeExpressions: false, includePaging: true);

        var capabilities = await host.Client.GetFromJsonAsync<ApiCapabilitiesDocument>("/capabilities");
        Assert.DoesNotContain(capabilities!.Capabilities.SelectMany(x => x.Links), link => link.Rel == "workflow-folders");

        using var omitted = await host.Client.SendAsync(new HttpRequestMessage(HttpMethod.Options, "/design/workflows/folders"));
        Assert.Equal(HttpStatusCode.NotFound, omitted.StatusCode);
    }

    [Fact]
    public async Task Workflow_definition_move_is_capability_discovered_and_maps_the_public_success_and_failure_contract()
    {
        await using var host = await CustomManagementHost.StartAsync(includeExpressions: false, includePaging: true, includeFolders: true);
        var capabilities = await host.Client.GetFromJsonAsync<ApiCapabilitiesDocument>("/capabilities");
        Assert.Contains(capabilities!.Capabilities.SelectMany(x => x.Links), link =>
            link.Rel == "workflow-definition-folder-move" && link.Href == "design/workflows/definitions/move");

        using var success = await host.Client.PostAsJsonAsync(
            "/design/workflows/definitions/move", new { definitionIds = new[] { "one", "two" }, folderId = "folder-1" });
        Assert.Equal(HttpStatusCode.NoContent, success.StatusCode);

        using var invalid = await host.Client.PostAsJsonAsync(
            "/design/workflows/definitions/move", new { definitionIds = new[] { "one", "one" }, folderId = (string?)null });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        using var omittedDestination = await host.Client.PostAsJsonAsync(
            "/design/workflows/definitions/move", new { definitionIds = new[] { "one" } });
        Assert.Equal(HttpStatusCode.BadRequest, omittedDestination.StatusCode);

        using var missing = await host.Client.PostAsJsonAsync(
            "/design/workflows/definitions/move", new { definitionIds = new[] { "missing" }, folderId = (string?)null });
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        using var conflict = await host.Client.PostAsJsonAsync(
            "/design/workflows/definitions/move", new { definitionIds = new[] { "conflict" }, folderId = (string?)null });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
    }

    private sealed class CustomManagementHost(WebApplication app, HttpClient client) : IAsyncDisposable
    {
        private static readonly string[] CommonEndpointTypes =
        [
            "Elsa.Api.Capabilities.Endpoints.GetCapabilities",
            "Elsa.Activities.Design.Api.Endpoints.Catalog.List",
            "Elsa.Activities.Design.Api.Endpoints.Availability.ListDiagnostics",
            "Elsa.Workflows.Design.Api.Endpoints.Definitions.List",
            "Elsa.Workflows.Publishing.Api.Endpoints.PublishWorkflowEndpoint",
            "Elsa.Workflows.Runtime.Api.Endpoints.ListWorkflowExecutablesEndpoint"
        ];

        public HttpClient Client { get; } = client;

        public static async Task<CustomManagementHost> StartAsync(bool includeExpressions, bool includePaging = false, bool includeFolders = false)
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();

            new ApiCapabilitiesFeature().ConfigureServices(builder.Services);
            new ActivitiesDesignApiFeature().ConfigureServices(builder.Services);
            new WorkflowsDesignApiFeature().ConfigureServices(builder.Services);
            new WorkflowsPublishingApiFeature().ConfigureServices(builder.Services);
            new WorkflowsRuntimeApiFeature().ConfigureServices(builder.Services);

            var endpointTypes = CommonEndpointTypes.ToHashSet(StringComparer.Ordinal);
            var assemblies = new List<System.Reflection.Assembly>
            {
                typeof(ApiCapabilitiesFeature).Assembly,
                typeof(ActivitiesDesignApiFeature).Assembly,
                typeof(WorkflowsDesignApiFeature).Assembly,
                typeof(WorkflowsPublishingApiFeature).Assembly,
                typeof(WorkflowsRuntimeApiFeature).Assembly
            };

            if (includeExpressions)
            {
                new ExpressionsFeature().ConfigureServices(builder.Services);
                new ExpressionsApiFeature().ConfigureServices(builder.Services);
                assemblies.Add(typeof(ExpressionsApiFeature).Assembly);
                endpointTypes.Add("Elsa.Expressions.Api.Endpoints.ListExpressionDescriptors");
            }

            if (includePaging)
            {
                builder.Services.AddSingleton<IWorkflowDefinitionPageStore, HttpWorkflowDefinitionPageStore>();
                builder.Services.AddSingleton<IWorkflowDefinitionListProjectionStore, HttpWorkflowDefinitionProjectionStore>();
                endpointTypes.Add("Elsa.Workflows.Design.Api.Endpoints.Definitions.Page");
            }
            if (includeFolders)
            {
                builder.Services.AddSingleton<IWorkflowFolderStore, HttpWorkflowFolderStore>();
                builder.Services.AddSingleton<IIdentityGenerator, HttpIdentityGenerator>();
                builder.Services.AddSingleton<HttpWorkflowDefinitionRepository>();
                builder.Services.AddSingleton<IAddWorkflowDefinitionCommand>(services => services.GetRequiredService<HttpWorkflowDefinitionRepository>());
                builder.Services.AddSingleton<IWorkflowDefinitionStore>(services => services.GetRequiredService<HttpWorkflowDefinitionRepository>());
                builder.Services.AddSingleton<IWorkflowDefinitionDraftStore>(services => services.GetRequiredService<HttpWorkflowDefinitionRepository>());
                builder.Services.AddSingleton<IWorkflowDefinitionVersionStore>(services => services.GetRequiredService<HttpWorkflowDefinitionRepository>());
                builder.Services.AddSingleton<IMoveWorkflowDefinitionsCommand, HttpMoveWorkflowDefinitionsCommand>();
                builder.Services.AddSingleton<IWorkflowDefinitionFactory, WorkflowDefinitionFactory>();
                builder.Services.AddSingleton<IWorkflowDefinitionDraftFactory, WorkflowDefinitionDraftFactory>();
                endpointTypes.UnionWith([
                    "Elsa.Workflows.Design.Api.Endpoints.Definitions.Add",
                    "Elsa.Workflows.Design.Api.Endpoints.Definitions.Get",
                    "Elsa.Workflows.Design.Api.Endpoints.Definitions.Move",
                    "Elsa.Workflows.Design.Api.Endpoints.Folders.List",
                    "Elsa.Workflows.Design.Api.Endpoints.Folders.Get",
                    "Elsa.Workflows.Design.Api.Endpoints.Folders.Create"]);
            }

            builder.Services.AddSingleton<JourneyRequestSender>();
            builder.Services.AddSingleton<IRequestSender>(services => services.GetRequiredService<JourneyRequestSender>());
            builder.Services.AddSingleton<ICommandSender>(services => services.GetRequiredService<JourneyRequestSender>());
            builder.Services.AddFastEndpoints(options =>
            {
                options.Assemblies = assemblies.ToArray();
                options.Filter = type => type.FullName is not null && endpointTypes.Contains(type.FullName);
            });

            var app = builder.Build();
            app.UseFastEndpoints(options => options.Endpoints.Configurator = endpoint => endpoint.AllowAnonymous());
            await app.StartAsync();
            return new CustomManagementHost(app, app.GetTestClient());
        }

        public async Task AssertJourneyAsync(HttpMethod method, string path, object? body = null)
        {
            using var request = new HttpRequestMessage(method, path);
            if (body is not null)
                request.Content = JsonContent.Create(body);
            using var response = await Client.SendAsync(request);

            Assert.True(
                response.IsSuccessStatusCode,
                $"Representative {method} {path} journey returned {(int)response.StatusCode} {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.DisposeAsync();
        }
    }

    private sealed class JourneyRequestSender(IServiceProvider services) : IRequestSender, ICommandSender
    {
        public Task<T> Send<T>(IRequest<T> request, CancellationToken cancellationToken = default) where T : notnull
        {
            if (request is ListExpressionDescriptors expressionDescriptors)
                return HandleExpressionDescriptors<T>(expressionDescriptors, cancellationToken);
            if (request is ListWorkflowDefinitionPage page)
                return HandleWorkflowDefinitionPage<T>(page, cancellationToken);
            if (request is ListWorkflowFolders folders)
                return HandleWorkflowFolders<T>(folders, cancellationToken);
            if (request is GetWorkflowFolder folder)
                return HandleWorkflowFolder<T>(folder, cancellationToken);
            if (request is GetDefinition definition)
                return HandleWorkflowDefinition<T>(definition, cancellationToken);

            object response = typeof(T) switch
            {
                var type when type == typeof(WorkflowDefinitionListView) => new WorkflowDefinitionListView([]),
                var type when type == typeof(ActivityAuthoringCatalogView) => new ActivityAuthoringCatalogView([]),
                var type when type == typeof(ActivityAvailabilityDiagnostics) => new ActivityAvailabilityDiagnostics([], []),
                var type when type == typeof(PublishedWorkflowView) => new PublishedWorkflowView(
                    "publication-1", "definition-1", "version-1", "version-1", "artifact-1", "default",
                    PublicationStatusView.Active, "reference-1", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
                    null, "1.0.0", "hash-1", "root-1", 1, true),
                var type when type == typeof(Elsa.Workflows.Runtime.Api.Models.WorkflowExecutablesListView) =>
                    new Elsa.Workflows.Runtime.Api.Models.WorkflowExecutablesListView([]),
                _ => throw new InvalidOperationException($"Unexpected representative journey response type '{typeof(T)}'.")
            };

            return Task.FromResult((T)response);
        }

        public async Task<T> Send<T>(Elsa.Mediator.Core.Contracts.ICommand<T> command, CancellationToken cancellationToken = default) where T : notnull
        {
            if (command is CreateWorkflowFolder folder)
            {
                var handler = services.GetRequiredService<Elsa.Mediator.Core.Contracts.ICommandHandler<CreateWorkflowFolder, WorkflowFolderView>>();
                return (T)(object)await handler.Handle(folder, cancellationToken);
            }
            if (command is AddDefinition definition)
            {
                var handler = services.GetRequiredService<Elsa.Mediator.Core.Contracts.ICommandHandler<AddDefinition, WorkflowDefinitionDetailsView>>();
                return (T)(object)await handler.Handle(definition, cancellationToken);
            }
            throw new InvalidOperationException($"Unexpected command '{command.GetType().Name}'.");
        }

        public async Task Send(Elsa.Mediator.Core.Contracts.ICommand command, CancellationToken cancellationToken = default)
        {
            if (command is MoveWorkflowDefinitions move)
            {
                var handler = services.GetRequiredService<Elsa.Mediator.Core.Contracts.ICommandHandler<MoveWorkflowDefinitions>>();
                await handler.Handle(move, cancellationToken);
                return;
            }
            throw new InvalidOperationException($"Unexpected command '{command.GetType().Name}'.");
        }

        private async Task<T> HandleWorkflowDefinitionPage<T>(
            ListWorkflowDefinitionPage request,
            CancellationToken cancellationToken) where T : notnull
        {
            var handler = services.GetRequiredService<
                IRequestHandler<ListWorkflowDefinitionPage, WorkflowDefinitionPageView>>();
            return (T)(object)await handler.Handle(request, cancellationToken);
        }

        private async Task<T> HandleExpressionDescriptors<T>(
            ListExpressionDescriptors request,
            CancellationToken cancellationToken) where T : notnull
        {
            var handler = services.GetRequiredService<IRequestHandler<ListExpressionDescriptors, ExpressionDescriptorsResponse>>();
            return (T)(object)await handler.Handle(request, cancellationToken);
        }

        private async Task<T> HandleWorkflowFolders<T>(ListWorkflowFolders request, CancellationToken cancellationToken) where T : notnull
        {
            var handler = services.GetRequiredService<IRequestHandler<ListWorkflowFolders, WorkflowFolderListView>>();
            return (T)(object)await handler.Handle(request, cancellationToken);
        }

        private async Task<T> HandleWorkflowFolder<T>(GetWorkflowFolder request, CancellationToken cancellationToken) where T : notnull
        {
            var handler = services.GetRequiredService<IRequestHandler<GetWorkflowFolder, WorkflowFolderDetailsView>>();
            return (T)(object)await handler.Handle(request, cancellationToken);
        }

        private async Task<T> HandleWorkflowDefinition<T>(GetDefinition request, CancellationToken cancellationToken) where T : notnull
        {
            var handler = services.GetRequiredService<IRequestHandler<GetDefinition, WorkflowDefinitionDetailsView>>();
            return (T)(object)await handler.Handle(request, cancellationToken);
        }
    }

    private sealed class HttpWorkflowDefinitionPageStore : IWorkflowDefinitionPageStore
    {
        public bool IsAvailable => true;

        public Task<WorkflowDefinitionPage> QueryPageAsync(
            WorkflowDefinitionPageQuery query,
            CancellationToken cancellationToken = default)
        {
            if (query.ContinuationToken is "malformed" or "context-token")
                throw new ArgumentException("The continuation token is invalid for this query.", nameof(query.ContinuationToken));

            if (query.FolderId is not null || query.Unfiled == true)
            {
                var definition = new WorkflowDefinition
                {
                    Id = query.Unfiled == true ? "unfiled-definition" : "folder-definition",
                    Name = "Orders",
                    CreatedAt = DateTimeOffset.UnixEpoch,
                    LastModifiedAt = DateTimeOffset.UnixEpoch,
                    FolderId = query.Unfiled == true ? null : query.FolderId
                };
                return Task.FromResult(new WorkflowDefinitionPage([definition], null));
            }

            Assert.Equal(1, query.PageSize);
            Assert.Equal("definition-42", query.SearchTerm);
            Assert.Equal(WorkflowDefinitionPageState.Deleted, query.State);
            return Task.FromResult(new WorkflowDefinitionPage(
                [
                    new WorkflowDefinition
                    {
                        Id = "definition-42",
                        Name = "Orders",
                        Description = "Order workflow",
                        CreatedAt = DateTimeOffset.UnixEpoch,
                        LastModifiedAt = DateTimeOffset.UnixEpoch,
                        DeletedAt = DateTimeOffset.UnixEpoch
                    }
                ],
                "next-http-token"));
        }
    }

    private sealed class HttpWorkflowDefinitionProjectionStore : IWorkflowDefinitionListProjectionStore
    {
        public Task<IReadOnlyList<WorkflowDefinitionListProjection>> ListByDefinitionIdsAsync(
            IReadOnlyCollection<string> workflowDefinitionIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WorkflowDefinitionListProjection>>(
                [new("definition-42", "draft-42", "version-42", "2.3.4", 7)]);
    }

    private sealed class HttpWorkflowFolderStore : IWorkflowFolderStore
    {
        private static readonly WorkflowFolder Folder = new() { Id = "folder-1", Name = "Orders", NormalizedName = "ORDERS", ParentKey = WorkflowFolder.RootParentKey };
        public bool IsAvailable => true;
        public Task<WorkflowFolderPage> ListDirectChildrenAsync(WorkflowFolderPageRequest request, CancellationToken cancellationToken = default)
        {
            if (request.ContinuationToken == "malformed")
                throw new ArgumentException("The continuation token is invalid for this query.", nameof(request.ContinuationToken));
            return Task.FromResult(new WorkflowFolderPage([Folder], "folder-token"));
        }
        public Task<WorkflowFolderDetails?> FindWithAncestorsAsync(string folderId, CancellationToken cancellationToken = default) =>
            Task.FromResult<WorkflowFolderDetails?>(folderId == Folder.Id ? new WorkflowFolderDetails(Folder, []) : null);
        public Task<WorkflowFolder> CreateAsync(WorkflowFolder folder, CancellationToken cancellationToken = default)
        {
            if (folder.NormalizedName == "DUPLICATE")
                throw new Elsa.Workflows.Design.Persistence.Core.Exceptions.WorkflowFolderSiblingConflictException();
            if (folder.ParentFolderId == "missing")
                throw Elsa.Primitives.Exceptions.EntityNotFoundException.ForEntity(typeof(WorkflowFolder), folder.ParentFolderId);
            if (folder.NormalizedName == "UNEXPECTED")
                throw new InvalidOperationException("provider-secret");
            return Task.FromResult(folder);
        }
    }

    private sealed class HttpIdentityGenerator : IIdentityGenerator
    {
        private int _next;
        public string Generate() => $"http-id-{Interlocked.Increment(ref _next)}";
    }

    private sealed class HttpMoveWorkflowDefinitionsCommand : IMoveWorkflowDefinitionsCommand
    {
        public Task Execute(IReadOnlyCollection<string> definitionIds, string? folderId, CancellationToken cancellationToken = default)
        {
            if (definitionIds.Contains("missing", StringComparer.Ordinal))
                throw Elsa.Primitives.Exceptions.EntityNotFoundException.ForEntity(typeof(WorkflowDefinition), "missing");
            if (definitionIds.Contains("conflict", StringComparer.Ordinal))
                throw new Elsa.Workflows.Design.Persistence.Core.Exceptions.WorkflowDefinitionFolderMoveConflictException();
            return Task.CompletedTask;
        }
    }

    private sealed class HttpWorkflowDefinitionRepository :
        IAddWorkflowDefinitionCommand,
        IWorkflowDefinitionStore,
        IWorkflowDefinitionDraftStore,
        IWorkflowDefinitionVersionStore
    {
        private WorkflowDefinition? _definition;
        private WorkflowDefinitionDraft? _draft;
        private IReadOnlyCollection<DesignMetadataRecord> _layout = [];

        public Task Execute(WorkflowDefinition workflowDefinition, WorkflowDefinitionDraft draft, CancellationToken cancellation) =>
            Execute(workflowDefinition, draft, [], cancellation);

        public Task Execute(
            WorkflowDefinition workflowDefinition,
            WorkflowDefinitionDraft draft,
            IReadOnlyCollection<DesignMetadataRecord> layout,
            CancellationToken cancellation)
        {
            _definition = workflowDefinition;
            _draft = draft;
            _layout = layout;
            return Task.CompletedTask;
        }

        public Task<WorkflowDefinition> GetAsync(string id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_definition is not null && _definition.Id == id
                ? _definition
                : throw Elsa.Primitives.Exceptions.EntityNotFoundException.ForEntity(typeof(WorkflowDefinition), id));

        public async Task<WorkflowDefinition?> FindByIdAsync(string id, CancellationToken cancellationToken = default) =>
            await GetAsync(id, cancellationToken);

        public Task<IReadOnlyList<WorkflowDefinition>> ListAsync(WorkflowDefinitionFilter filter, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WorkflowDefinition>>(_definition is null ? [] : [_definition]);

        Task<WorkflowDefinitionDraft?> IWorkflowDefinitionDraftStore.FindByIdAsync(string draftId, CancellationToken cancellationToken) =>
            Task.FromResult(_draft?.Id == draftId ? _draft : null);

        public Task<WorkflowDefinitionDraft?> FindByWorkflowDefinitionIdAsync(string workflowDefinitionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_draft?.WorkflowDefinitionId == workflowDefinitionId ? _draft : null);

        public Task<IReadOnlyList<WorkflowDefinitionDraft>> ListByWorkflowDefinitionIdAsync(string workflowDefinitionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WorkflowDefinitionDraft>>(
                _draft?.WorkflowDefinitionId == workflowDefinitionId ? [_draft] : []);

        public Task<IReadOnlyCollection<DesignMetadataRecord>> FindLayoutByDraftIdAsync(string draftId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<DesignMetadataRecord>>(_draft?.Id == draftId ? _layout : []);

        public async Task<DraftWithLayout?> FindWithLayoutByIdAsync(string draftId, CancellationToken cancellationToken = default) =>
            _draft?.Id == draftId ? new DraftWithLayout(_draft, await FindLayoutByDraftIdAsync(draftId, cancellationToken)) : null;

        Task<WorkflowDefinitionVersion> IWorkflowDefinitionVersionStore.GetAsync(string versionId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        Task<WorkflowDefinitionVersion?> IWorkflowDefinitionVersionStore.FindByIdAsync(string versionId, CancellationToken cancellationToken) =>
            Task.FromResult<WorkflowDefinitionVersion?>(null);

        public Task<WorkflowDefinitionVersion> GetWithDefinitionAsync(string versionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<WorkflowDefinitionVersion?> FindLatestVersionAsync(string definitionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<WorkflowDefinitionVersion?>(null);

        public Task<IReadOnlyList<WorkflowDefinitionVersion>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WorkflowDefinitionVersion>>([]);

        public Task<bool> ExistsAsync(string definitionId, string semVerSortKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }
}
