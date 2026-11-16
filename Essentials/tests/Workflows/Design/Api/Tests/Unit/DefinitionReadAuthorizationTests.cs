using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Extensions;
using Elsa.Primitives.Exceptions;
using Elsa.Testing;
using Elsa.Workflows.Design.Api.Endpoints.Definitions;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Exceptions;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Filters;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Workflows.Design.Api.Tests.Unit;

/// <summary>
/// The definition GET through the real endpoint pipeline (spec 1193, story 5): an owner that cannot decide answers
/// unavailable, distinct from denied and without its cause, and a built-in read does each permission, store and
/// organization check once. In-process composition, no listener or database.
/// </summary>
public sealed class DefinitionReadAuthorizationTests
{
    private const string Cause = "Private infrastructure detail 5f1c";

    [Fact]
    public async Task An_owner_that_cannot_decide_answers_503_with_the_unavailable_code_and_no_cause()
    {
        using var host = new Host(configure: services => services.Replace(
            ServiceDescriptor.Scoped<IWorkflowDefinitionAuthorizer>(_ => new ThrowingAuthorizer())));

        var (status, contentType, body) = await host.GetAsync();

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, status);
        Assert.Equal("application/problem+json; charset=utf-8", contentType);
        using var document = JsonDocument.Parse(body);
        var problem = document.RootElement;
        Assert.Equal(WorkflowDefinitionAuthorizationException.UnavailableCode, problem.GetProperty("errorCode").GetString());
        Assert.Equal("workflow.authorization.unavailable", problem.GetProperty("errorCode").GetString());
        Assert.Equal(503, problem.GetProperty("statusCode").GetInt32());
        Assert.Equal("One or more errors occurred!", problem.GetProperty("message").GetString());
        Assert.Equal("Workflow definition authorization could not be completed.",
            Assert.Single(problem.GetProperty("errors").GetProperty("generalErrors").EnumerateArray()).GetString());
        Assert.DoesNotContain(Cause, body, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(IOException), body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_built_in_owner_s_own_infrastructure_failure_answers_unavailable_too()
    {
        // Without a library store the reader makes no permission decision, so the built-in authorizer makes its own.
        using var host = new Host(library: false);
        host.Counters.FailOwnerPermission = true;

        var (status, _, body) = await host.GetAsync();

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, status);
        Assert.Equal(WorkflowDefinitionAuthorizationException.UnavailableCode, JsonDocument.Parse(body).RootElement.GetProperty("errorCode").GetString());
        Assert.DoesNotContain(Cause, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_denying_owner_keeps_the_existing_403_body_byte_for_byte()
    {
        using var host = new Host(configure: services => services.Replace(
            ServiceDescriptor.Scoped<IWorkflowDefinitionAuthorizer>(_ => new DenyingAuthorizer())));

        var (status, _, body) = await host.GetAsync();

        Assert.Equal(StatusCodes.Status403Forbidden, status);
        Assert.Equal("""{"errors":{"generalErrors":["The requested workflow definition is not readable in the current application context."]},"message":"One or more errors occurred!","statusCode":403}""", body);
    }

    [Fact]
    public async Task A_missing_definition_keeps_the_existing_404_body_without_an_error_code()
    {
        using var host = new Host();
        host.Definitions.Missing = true;

        var (status, _, body) = await host.GetAsync();

        Assert.Equal(StatusCodes.Status404NotFound, status);
        Assert.Equal("""{"errors":{"generalErrors":["The workflow definition was not found."]},"message":"One or more errors occurred!","statusCode":404}""", body);
    }

    /// <summary>
    /// Before slice A a built-in GET made six Read decisions on the definition (endpoint, reader, reader organization
    /// check, authorizer, authorizer's reader preflight and its organization check), four folder decisions, two
    /// definition store reads and two organization lookups.
    /// </summary>
    [Fact]
    public async Task A_built_in_GET_checks_permission_store_and_organization_once_each()
    {
        using var host = new Host();

        var (status, _, _) = await host.GetAsync();

        Assert.Equal(StatusCodes.Status200OK, status);
        // Endpoint and reader permission checks, plus the one organization check's definition decision.
        Assert.Equal(3, host.Counters.DefinitionDecisions(WorkflowDesignPermissionsRead));
        // The organization check reads each folder of the breadcrumb once.
        Assert.Equal(["finance", "invoices"], host.Counters.FolderDecisions());
        Assert.Equal(0, host.Counters.DefinitionDecisions(WorkflowDesignPermissionsManage));
        Assert.Equal(1, host.Definitions.Gets);
        Assert.Equal(0, host.Definitions.Finds);
        Assert.Equal(1, host.Counters.OrganizationLookups);
    }

    [Fact]
    public async Task Without_a_library_store_the_owner_still_decides_Read_and_only_the_repeated_store_read_is_skipped()
    {
        using var host = new Host(library: false);

        var (status, _, _) = await host.GetAsync();

        Assert.Equal(StatusCodes.Status200OK, status);
        // The endpoint's check and the owner's own check: the reader made none, so its read stands for existence only.
        Assert.Equal(2, host.Counters.DefinitionDecisions(WorkflowDesignPermissionsRead));
        Assert.Equal(1, host.Counters.Decisions.Count(item => item.ExplicitTenant));
        Assert.Equal(1, host.Definitions.Gets);
        Assert.Equal(0, host.Definitions.Finds);
    }

    [Fact]
    public async Task A_definition_from_another_tenant_is_still_refused_after_a_successful_read()
    {
        using var host = new Host();
        host.Definitions.Tenant = "another-tenant";

        var (status, _, _) = await host.GetAsync();

        Assert.Equal(StatusCodes.Status403Forbidden, status);
        Assert.Equal(1, host.Definitions.Finds);
    }

    [Fact]
    public async Task A_replacement_reader_that_delegates_to_the_built_in_reader_cannot_stand_in_for_the_owner()
    {
        var replacement = new DelegatingReader();
        using var host = new Host(configure: services => services.Replace(ServiceDescriptor.Scoped<IWorkflowDefinitionDetailsReader>(provider =>
            replacement.Wrap(ActivatorUtilities.CreateInstance<WorkflowDefinitionDetailsReader>(provider)))));

        var (status, _, _) = await host.GetAsync();

        Assert.Equal(StatusCodes.Status200OK, status);
        // The built-in reader recorded its read, but the registered reader is the replacement, so the owner does
        // its complete decision, including the replacement's own preflight.
        Assert.Equal(1, replacement.Reads);
        Assert.Equal(1, replacement.Preflights);
        Assert.Equal(1, host.Definitions.Finds);
        Assert.Equal(1, host.Counters.Decisions.Count(item => item.ExplicitTenant));
    }

    [Fact]
    public async Task A_stricter_replacement_preflight_is_honoured_even_though_its_read_succeeded()
    {
        var replacement = new DelegatingReader { RefusePreflight = true };
        using var host = new Host(configure: services => services.Replace(ServiceDescriptor.Scoped<IWorkflowDefinitionDetailsReader>(provider =>
            replacement.Wrap(ActivatorUtilities.CreateInstance<WorkflowDefinitionDetailsReader>(provider)))));

        var (status, _, _) = await host.GetAsync();

        Assert.Equal(StatusCodes.Status403Forbidden, status);
        Assert.Equal(1, replacement.Preflights);
    }

    [Fact]
    public async Task Evidence_for_one_definition_does_not_satisfy_another_and_is_spent_by_any_decision()
    {
        using var host = new Host();
        using var request = host.Begin();

        await request.Reader.ReadAsync("wf-1", CancellationToken.None);
        Assert.True(await request.Owner.CanReadAsync("wf-2"));
        Assert.Equal(1, host.Definitions.Finds);

        // The read's evidence was spent on the wf-2 decision, so wf-1 now gets the complete decision too.
        Assert.True(await request.Owner.CanReadAsync("wf-1"));
        Assert.Equal(2, host.Definitions.Finds);
    }

    [Fact]
    public async Task Evidence_does_not_survive_a_failed_read()
    {
        using var host = new Host();
        using var request = host.Begin();
        await request.Reader.ReadAsync("wf-1", CancellationToken.None);
        host.Definitions.Missing = true;
        await Assert.ThrowsAsync<EntityNotFoundException>(() => request.Reader.ReadAsync("wf-1", CancellationToken.None));
        host.Definitions.Missing = false;

        Assert.True(await request.Owner.CanReadAsync("wf-1"));

        Assert.Equal(1, host.Definitions.Finds);
    }

    [Fact]
    public async Task Evidence_does_not_cross_requests_or_principals()
    {
        using var host = new Host();
        using (var first = host.Begin())
            await first.Reader.ReadAsync("wf-1", CancellationToken.None);
        using (var second = host.Begin())
            Assert.True(await second.Owner.CanReadAsync("wf-1"));
        Assert.Equal(1, host.Definitions.Finds);

        using var third = host.Begin();
        await third.Reader.ReadAsync("wf-1", CancellationToken.None);
        third.Context.User = Principal("another-tenant", WorkflowDesignPermissionsRead);
        Assert.False(await third.Owner.CanReadAsync("wf-1"));
        Assert.Equal(2, host.Definitions.Finds);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_manage_decision_after_a_read_still_checks_the_Manage_permission(bool canManage)
    {
        using var host = new Host();
        using var request = host.Begin(canManage
            ? Principal("tenant", WorkflowDesignPermissionsRead, WorkflowDesignPermissionsManage)
            : Principal("tenant", WorkflowDesignPermissionsRead));
        await request.Reader.ReadAsync("wf-1", CancellationToken.None);

        Assert.Equal(canManage, await request.Owner.CanManageAsync("wf-1"));

        Assert.Equal(1, host.Counters.DefinitionDecisions(WorkflowDesignPermissionsManage));
        Assert.Equal(0, host.Definitions.Finds);
    }

    [Fact]
    public async Task A_same_tenant_principal_without_Read_cannot_use_another_principal_s_read()
    {
        using var host = new Host();
        using var request = host.Begin();
        await request.Reader.ReadAsync("wf-1", CancellationToken.None);
        // Same tenant, same request, but a different principal that holds no workflow design permission.
        request.Context.User = Principal("tenant");

        Assert.False(await request.Owner.CanReadAsync("wf-1"));
        Assert.Equal(1, host.Counters.Decisions.Count(item => item.ExplicitTenant));
    }

    [Fact]
    public async Task A_read_against_another_definition_store_does_not_confirm_existence_in_the_owner_s_store()
    {
        var foreign = new CountingDefinitions();
        using var host = new Host(configure: services => services.Replace(ServiceDescriptor.Scoped<IWorkflowDefinitionDetailsReader>(provider =>
            new WorkflowDefinitionDetailsReader(provider.GetRequiredService<IWorkflowDefinitionVersionStore>(), foreign,
                provider.GetRequiredService<IWorkflowDefinitionDraftStore>(), provider.GetService<IWorkflowDefinitionPublicationStateLookup>(),
                provider.GetService<IWorkflowLibraryStore>(), provider.GetRequiredService<IHttpContextAccessor>()))));
        using var request = host.Begin();
        await request.Reader.ReadAsync("wf-1", CancellationToken.None);
        Assert.Equal(1, foreign.Gets);

        Assert.True(await request.Owner.CanReadAsync("wf-1"));

        // The registered reader is the built-in instance, but its store is not the owner's: the owner reads its own.
        Assert.Equal(1, host.Definitions.Finds);
    }

    [Fact]
    public async Task A_read_decided_through_another_scope_s_permission_service_does_not_stand_for_the_owner_s_Read_check()
    {
        using var host = new Host();
        using var request = host.BeginWithForeignRequestServices();
        await request.Reader.ReadAsync("wf-1", CancellationToken.None);

        Assert.True(await request.Owner.CanReadAsync("wf-1"));

        // Existence still stands (same reader, store and principal), but the owner makes its own Read decision.
        Assert.Equal(1, host.Counters.Decisions.Count(item => item.ExplicitTenant && item.Permission == WorkflowDesignPermissionsRead));
        Assert.Equal(0, host.Definitions.Finds);
    }

    private const string WorkflowDesignPermissionsRead = "workflows/design:read";
    private const string WorkflowDesignPermissionsManage = "workflows/design:manage";
    private const string Scheme = "DesignReadAuthorization";

    // A host whose workflow persistence has one scope stores every definition under that scope's tenant, whatever tenant
    // created it (the conformance probe's packaged consumer stores "default"). The owner then refuses a principal of
    // another tenant: the stored tenant must equal the principal's. This comparison predates the read evidence (it is the
    // authorizer's tenant check on main), and the evidence confirms only the same tenant, so the outcome is unchanged with
    // or without a library store. Manage satisfies Read in the permission evaluator.
    [Theory]
    [InlineData(true, "tenant-b", "default", StatusCodes.Status403Forbidden)]
    [InlineData(false, "tenant-b", "default", StatusCodes.Status403Forbidden)]
    [InlineData(true, "tenant-b", "tenant-b", StatusCodes.Status200OK)]
    [InlineData(false, "tenant-b", "tenant-b", StatusCodes.Status200OK)]
    public async Task A_definition_stored_under_another_tenant_is_refused_and_manage_alone_reads_its_own(bool library, string principalTenant,
        string storedTenant, int expected)
    {
        using var host = new Host(library);
        host.Definitions.Tenant = storedTenant;

        var (status, _, body) = await host.GetAsync(Principal(principalTenant, WorkflowDesignPermissionsManage));

        Assert.Equal(expected, status);
        if (expected == StatusCodes.Status403Forbidden)
            Assert.Contains("not readable in the current application context", body, StringComparison.Ordinal);
    }

    private static ClaimsPrincipal Principal(string tenant, params string[] permissions) => new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "actor"), new Claim(IdentityClaimTypes.Normalized, "v1"),
            new Claim(IdentityClaimTypes.TenantId, tenant), .. permissions.Select(permission => new Claim(IdentityClaimTypes.Permission, permission))
        ], Scheme));

    private sealed class Host : IDisposable
    {
        private readonly ServiceProvider root;
        private readonly RouteEndpoint get;
        internal Counters Counters { get; } = new();
        internal CountingDefinitions Definitions { get; } = new();

        internal Host(bool library = true, Action<IServiceCollection>? configure = null)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddRouting().AddElsaEndpoints();
            services.AddFoundationIdentityAbstractions(options => options.NormalizedAuthenticationTypes = new HashSet<string>([Scheme], StringComparer.Ordinal));
            new WorkflowsDesignApiFeature().ConfigureServices(services);
            services.AddSingleton(Counters);
            services.ReplacePermissionAuthorizationService<CountingPermissions>();
            services.AddSingleton<IWorkflowDefinitionStore>(Definitions);
            services.AddSingleton(Store<IWorkflowDefinitionVersionStore>.Create(Counters));
            services.AddSingleton(Store<IWorkflowDefinitionDraftStore>.Create(Counters));
            if (library)
                services.AddSingleton(Store<IWorkflowLibraryStore>.Create(Counters));
            configure?.Invoke(services);
            root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            var routes = new TestEndpointRouteBuilder(root);
            WorkflowsDesignApi.MapWorkflowsDesignApi(routes);
            get = routes.DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>().Single(endpoint =>
                endpoint.RoutePattern.RawText?.EndsWith("/definitions/{definitionId}", StringComparison.Ordinal) == true
                && endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains("GET") == true);
        }

        internal async Task<(int Status, string? ContentType, string Body)> GetAsync(ClaimsPrincipal? user = null)
        {
            using var request = Begin(user);
            var context = request.Context;
            context.SetEndpoint(get);
            context.Request.Method = "GET";
            context.Request.Path = "/design/workflows/definitions/wf-1";
            context.Request.RouteValues["definitionId"] = "wf-1";
            context.Response.Body = new MemoryStream();
            await get.RequestDelegate!(context);
            context.Response.Body.Position = 0;
            using var reader = new StreamReader(context.Response.Body);
            return (context.Response.StatusCode, context.Response.ContentType, await reader.ReadToEndAsync());
        }

        /// <summary>One request: its own scope, HTTP context and principal.</summary>
        internal Request Begin(ClaimsPrincipal? user = null) =>
            new(root.CreateScope(), user ?? Principal("tenant", WorkflowDesignPermissionsRead, WorkflowDesignPermissionsManage));

        /// <summary>A request whose HTTP context resolves its services from a second, foreign scope.</summary>
        internal Request BeginWithForeignRequestServices() =>
            new(root.CreateScope(), Principal("tenant", WorkflowDesignPermissionsRead, WorkflowDesignPermissionsManage), root.CreateScope());

        public void Dispose() => root.Dispose();
    }

    private sealed class Request : IDisposable
    {
        private readonly IServiceScope scope;
        private readonly IServiceScope? requestScope;
        private readonly IHttpContextAccessor accessor;
        internal HttpContext Context { get; }
        internal IWorkflowDefinitionDetailsReader Reader => scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionDetailsReader>();
        internal IWorkflowDefinitionAuthorizer Owner => scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionAuthorizer>();

        internal Request(IServiceScope scope, ClaimsPrincipal user, IServiceScope? requestScope = null)
        {
            this.scope = scope;
            this.requestScope = requestScope;
            accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
            Context = new DefaultHttpContext { RequestServices = (requestScope ?? scope).ServiceProvider, User = user };
            accessor.HttpContext = Context;
        }

        public void Dispose()
        {
            accessor.HttpContext = null;
            requestScope?.Dispose();
            scope.Dispose();
        }
    }

    internal sealed class Counters
    {
        internal List<(string Permission, string? DefinitionId, string? FolderId, bool ExplicitTenant)> Decisions { get; } = [];
        internal int OrganizationLookups;
        internal bool FailOwnerPermission;
        internal int DefinitionDecisions(string permission) => Decisions.Count(item => item.Permission == permission && item.DefinitionId is not null);
        internal string[] FolderDecisions() => Decisions.Where(item => item.FolderId is not null).Select(item => item.FolderId!).ToArray();
    }

    /// <summary>Counts every permission decision; the built-in owner is the only caller that passes an explicit tenant.</summary>
    private sealed class CountingPermissions(IPermissionEvaluator evaluator, IEnumerable<IPermissionResourceHandler> handlers,
        NormalizedPrincipalValidator validator, IHttpContextAccessor accessor, Counters counters) : IPermissionAuthorizationService
    {
        private readonly PermissionAuthorizationService inner = new(evaluator, handlers, validator, accessor);

        public ValueTask<PermissionEvaluationResult> AuthorizeAsync(PermissionEvaluationContext context, CancellationToken cancellationToken = default)
        {
            var resource = context.Resource as WorkflowLibraryResource;
            counters.Decisions.Add((context.Permission, resource?.DefinitionId, resource?.FolderId, context.TenantId is not null));
            if (counters.FailOwnerPermission && context.TenantId is not null)
                throw new IOException(Cause);
            return inner.AuthorizeAsync(context, cancellationToken);
        }
    }

    internal sealed class CountingDefinitions : IWorkflowDefinitionStore
    {
        internal bool Missing;
        internal string? Tenant = "tenant";
        internal int Gets, Finds;

        public Task<WorkflowDefinition> GetAsync(string id, CancellationToken cancellationToken = default)
        {
            Gets++;
            return Missing ? throw new EntityNotFoundException("The workflow definition was not found.") : Task.FromResult(Metadata(id));
        }

        public Task<WorkflowDefinition?> FindByIdAsync(string id, CancellationToken cancellationToken = default)
        {
            Finds++;
            return Task.FromResult<WorkflowDefinition?>(Missing ? null : Metadata(id));
        }

        public Task<IReadOnlyList<WorkflowDefinition>> ListAsync(WorkflowDefinitionFilter filter, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("One definition read needs no inventory.");

        private WorkflowDefinition Metadata(string id) => new() { Id = id, Name = "Owned definition", TenantId = Tenant };
    }

    /// <summary>The version, draft and library ports a details read touches; the library port counts its organization lookups.</summary>
    public class Store<T> : DispatchProxy where T : class
    {
        private Counters counters = null!;

        internal static T Create(Counters counters)
        {
            var store = DispatchProxy.Create<T, Store<T>>();
            ((Store<T>)(object)store).counters = counters;
            return store;
        }

        protected override object? Invoke(MethodInfo? method, object?[]? arguments)
        {
            switch (method?.Name)
            {
                case nameof(IWorkflowLibraryStore.GetOrganizationAsync):
                    counters.OrganizationLookups++;
                    return Task.FromResult<WorkflowOrganization?>(new((string)arguments![0]!, "invoices",
                        [new("finance", null, "Finance", "finance", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, "folder-1")], [], "organization-1"));
                case nameof(IWorkflowDefinitionVersionStore.ListByDefinitionAsync):
                    return Task.FromResult<IReadOnlyList<WorkflowDefinitionVersion>>([]);
                case nameof(IWorkflowDefinitionDraftStore.FindByWorkflowDefinitionIdAsync):
                    return Task.FromResult<WorkflowDefinitionDraft?>(null);
                default:
                    throw new NotSupportedException("A definition read does not use " + method?.Name);
            }
        }
    }

    /// <summary>A replacement reader that reuses the built-in read but owns its own preflight.</summary>
    private sealed class DelegatingReader : IWorkflowDefinitionDetailsReader
    {
        private IWorkflowDefinitionDetailsReader inner = null!;
        internal bool RefusePreflight;
        internal int Reads, Preflights;

        internal IWorkflowDefinitionDetailsReader Wrap(IWorkflowDefinitionDetailsReader builtIn)
        {
            inner = builtIn;
            return this;
        }

        public Task<WorkflowDefinitionDetailsView> ReadAsync(string definitionId, CancellationToken cancellationToken)
        {
            Reads++;
            return inner.ReadAsync(definitionId, cancellationToken);
        }

        public Task AuthorizeReadAsync(string definitionId, CancellationToken cancellationToken)
        {
            Preflights++;
            return RefusePreflight
                ? throw new UnauthorizedAccessException("The replacement refuses this definition.")
                : inner.AuthorizeReadAsync(definitionId, cancellationToken);
        }
    }

    private sealed class ThrowingAuthorizer : IWorkflowDefinitionAuthorizer
    {
        public ValueTask<bool> CanReadAsync(string definitionId, CancellationToken cancellationToken = default)
            => throw new WorkflowDefinitionAuthorizationException(definitionId, "read", new IOException(Cause));
        public ValueTask<bool> CanManageAsync(string definitionId, CancellationToken cancellationToken = default)
            => throw new WorkflowDefinitionAuthorizationException(definitionId, "manage", new IOException(Cause));
    }

    private sealed class DenyingAuthorizer : IWorkflowDefinitionAuthorizer
    {
        public ValueTask<bool> CanReadAsync(string definitionId, CancellationToken cancellationToken = default) => ValueTask.FromResult(false);
        public ValueTask<bool> CanManageAsync(string definitionId, CancellationToken cancellationToken = default) => ValueTask.FromResult(false);
    }
}
