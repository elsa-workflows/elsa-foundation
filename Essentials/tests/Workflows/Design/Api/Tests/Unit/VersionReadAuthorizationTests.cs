using System.Security.Claims;
using System.Text.Json;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Extensions;
using Elsa.Primitives.Exceptions;
using Elsa.Testing;
using Elsa.Workflows.Design.Api.Authorization;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Exceptions;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Workflows.Design.Api.Tests.Unit;

/// <summary>
/// The version GET through the real endpoint pipeline (#3723): the owning definition authorizer decides on the
/// version's definition before any details leave, with the definition GET's not-found and forbidden responses.
/// In-process composition, no listener or database.
/// </summary>
public sealed class VersionReadAuthorizationTests
{
    private const string DefinitionId = "wf-definition-1";
    private const string VersionId = "wf-version-1";
    private const string Scheme = "DesignVersionReadAuthorization";

    [Fact]
    public async Task An_allowing_owner_is_asked_for_the_version_s_definition_and_the_version_is_returned()
    {
        using var host = new Host(allow: true);

        var (status, _, body) = await host.GetAsync();

        Assert.Equal(StatusCodes.Status200OK, status);
        using var document = JsonDocument.Parse(body);
        Assert.Equal(VersionId, document.RootElement.GetProperty("id").GetString());
        Assert.Equal(DefinitionId, document.RootElement.GetProperty("definition").GetProperty("id").GetString());
        Assert.Equal([DefinitionId], host.Authorizer.ReadDecisions);
    }

    [Fact]
    public async Task A_denying_owner_answers_the_definition_GET_s_403_body_and_no_version_details()
    {
        using var host = new Host(allow: false);

        var (status, _, body) = await host.GetAsync();

        Assert.Equal(StatusCodes.Status403Forbidden, status);
        // Byte for byte the definition GET's denial (DefinitionReadAuthorizationTests), so a caller cannot tell the two apart.
        Assert.Equal("""{"errors":{"generalErrors":["The requested workflow definition is not readable in the current application context."]},"message":"One or more errors occurred!","statusCode":403}""", body);
        Assert.DoesNotContain(VersionId, body, StringComparison.Ordinal);
        Assert.Equal([DefinitionId], host.Authorizer.ReadDecisions);
    }

    [Fact]
    public async Task A_missing_version_answers_404_before_the_owner_is_asked()
    {
        using var host = new Host(allow: false);
        host.Versions.Missing = true;

        var (status, _, body) = await host.GetAsync();

        Assert.Equal(StatusCodes.Status404NotFound, status);
        Assert.Equal(404, JsonDocument.Parse(body).RootElement.GetProperty("statusCode").GetInt32());
        Assert.Empty(host.Authorizer.ReadDecisions);
    }

    [Fact]
    public async Task Without_exactly_one_owner_the_version_is_withheld()
    {
        using var host = new Host(allow: true, configure: services => services.RemoveAll<IWorkflowDefinitionAuthorizer>());

        var (status, _, body) = await host.GetAsync();

        Assert.Equal(StatusCodes.Status403Forbidden, status);
        Assert.DoesNotContain(VersionId, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_actor_without_Read_on_the_version_s_definition_is_refused_before_the_owner_is_asked()
    {
        using var host = new Host(allow: true);

        var (status, _, body) = await host.GetAsync(Principal("tenant"));

        Assert.Equal(StatusCodes.Status403Forbidden, status);
        // The definition GET's resource-scoped permission refusal, even though the replacement owner would allow.
        Assert.Equal("""{"errors":{"generalErrors":["The requested workflow library operation is not permitted."]},"message":"One or more errors occurred!","statusCode":403}""", body);
        Assert.Empty(host.Authorizer.ReadDecisions);
    }

    [Fact]
    public async Task An_owner_that_cannot_decide_answers_503_with_the_unavailable_code_and_no_version_details()
    {
        using var host = new Host(allow: true, configure: services => services.Replace(
            ServiceDescriptor.Scoped<IWorkflowDefinitionAuthorizer>(_ => new ThrowingAuthorizer())));

        var (status, _, body) = await host.GetAsync();

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, status);
        Assert.Equal(WorkflowDefinitionAuthorizationException.UnavailableCode, JsonDocument.Parse(body).RootElement.GetProperty("errorCode").GetString());
        Assert.DoesNotContain(VersionId, body, StringComparison.Ordinal);
        Assert.DoesNotContain(Cause, body, StringComparison.Ordinal);
    }

    // The built-in owner takes its complete decision here: the version reader records no read evidence, so the owner
    // checks the Read permission, the tenant-scoped definition store and the reader preflight itself.
    [Theory]
    [InlineData("tenant", StatusCodes.Status200OK)]
    [InlineData("another-tenant", StatusCodes.Status403Forbidden)]
    public async Task The_built_in_owner_reads_its_own_tenant_s_version_and_refuses_one_stored_under_another_tenant(string storedTenant, int expected)
    {
        using var host = new Host(allow: true, builtInOwner: true);
        host.Definitions.Tenant = storedTenant;

        var (status, _, body) = await host.GetAsync();

        Assert.Equal(expected, status);
        Assert.Equal(1, host.Definitions.Finds);
        if (expected == StatusCodes.Status403Forbidden)
            Assert.DoesNotContain(VersionId, body, StringComparison.Ordinal);
    }

    private const string Cause = "Private infrastructure detail 3723";

    private static ClaimsPrincipal Principal(string tenant, params string[] permissions) => new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "actor"), new Claim(IdentityClaimTypes.Normalized, "v1"),
            new Claim(IdentityClaimTypes.TenantId, tenant), .. permissions.Select(permission => new Claim(IdentityClaimTypes.Permission, permission))
        ], Scheme));

    private sealed class Host : IDisposable
    {
        private readonly ServiceProvider root;
        private readonly RouteEndpoint get;
        internal RecordingAuthorizer Authorizer { get; }
        internal StubVersions Versions { get; } = new();
        internal DefinitionReadAuthorizationTests.CountingDefinitions Definitions { get; } = new();

        /// <param name="builtInOwner">Keep the built-in <c>HttpWorkflowDefinitionAuthorizer</c>, without a library store,
        /// instead of the recording replacement.</param>
        internal Host(bool allow, Action<IServiceCollection>? configure = null, bool builtInOwner = false)
        {
            Authorizer = new RecordingAuthorizer(allow);
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddRouting().AddElsaEndpoints();
            services.AddFoundationIdentityAbstractions(options => options.NormalizedAuthenticationTypes = new HashSet<string>([Scheme], StringComparer.Ordinal));
            new WorkflowsDesignApiFeature().ConfigureServices(services);
            services.AddSingleton<IWorkflowDefinitionVersionStore>(Versions);
            services.AddSingleton<IWorkflowDefinitionVersionLayoutStore>(new NoLayouts());
            // The built-in owner's reader preflight composes the definition and draft stores; the version read uses neither.
            services.AddSingleton<IWorkflowDefinitionStore>(Definitions);
            services.AddSingleton(DefinitionReadAuthorizationTests.Store<IWorkflowDefinitionDraftStore>.Create(new DefinitionReadAuthorizationTests.Counters()));
            if (!builtInOwner)
                services.Replace(ServiceDescriptor.Scoped<IWorkflowDefinitionAuthorizer>(_ => Authorizer));
            configure?.Invoke(services);
            root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            var routes = new TestEndpointRouteBuilder(root);
            WorkflowsDesignApi.MapWorkflowsDesignApi(routes);
            get = routes.DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>().Single(endpoint =>
                endpoint.RoutePattern.RawText?.EndsWith("/versions/{versionId}", StringComparison.Ordinal) == true
                && endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains("GET") == true);
        }

        internal async Task<(int Status, string? ContentType, string Body)> GetAsync(ClaimsPrincipal? user = null)
        {
            using var scope = root.CreateScope();
            var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
            var context = new DefaultHttpContext
            {
                RequestServices = scope.ServiceProvider,
                User = user ?? Principal("tenant", WorkflowDesignPermissions.Read)
            };
            accessor.HttpContext = context;
            try
            {
                context.SetEndpoint(get);
                context.Request.Method = "GET";
                context.Request.Path = "/design/workflows/versions/" + VersionId;
                context.Request.RouteValues["versionId"] = VersionId;
                context.Response.Body = new MemoryStream();
                await get.RequestDelegate!(context);
                context.Response.Body.Position = 0;
                using var reader = new StreamReader(context.Response.Body);
                return (context.Response.StatusCode, context.Response.ContentType, await reader.ReadToEndAsync());
            }
            finally
            {
                accessor.HttpContext = null;
            }
        }

        public void Dispose() => root.Dispose();
    }

    /// <summary>A replacement owner that records the definitions it was asked about.</summary>
    internal sealed class RecordingAuthorizer(bool allow) : IWorkflowDefinitionAuthorizer
    {
        internal List<string> ReadDecisions { get; } = [];

        public ValueTask<bool> CanReadAsync(string definitionId, CancellationToken cancellationToken = default)
        {
            ReadDecisions.Add(definitionId);
            return ValueTask.FromResult(allow);
        }

        public ValueTask<bool> CanManageAsync(string definitionId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("A version read never asks for management authority.");
    }

    private sealed class ThrowingAuthorizer : IWorkflowDefinitionAuthorizer
    {
        public ValueTask<bool> CanReadAsync(string definitionId, CancellationToken cancellationToken = default)
            => throw new WorkflowDefinitionAuthorizationException(definitionId, "read", new IOException(Cause));
        public ValueTask<bool> CanManageAsync(string definitionId, CancellationToken cancellationToken = default)
            => throw new WorkflowDefinitionAuthorizationException(definitionId, "manage", new IOException(Cause));
    }

    internal sealed class StubVersions : IWorkflowDefinitionVersionStore
    {
        internal bool Missing;

        public Task<WorkflowDefinitionVersion> GetAsync(string versionId, CancellationToken cancellationToken = default) => Get();

        public Task<WorkflowDefinitionVersion?> FindByIdAsync(string versionId, CancellationToken cancellationToken = default)
            => Task.FromResult<WorkflowDefinitionVersion?>(Missing ? null : Version());

        public Task<WorkflowDefinitionVersion> GetWithDefinitionAsync(string versionId, CancellationToken cancellationToken = default) => Get();

        public Task<WorkflowDefinitionVersion?> FindLatestVersionAsync(string definitionId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("One version read needs no latest-version lookup.");

        public Task<IReadOnlyList<WorkflowDefinitionVersion>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("One version read needs no inventory.");

        public Task<bool> ExistsAsync(string definitionId, string semVerSortKey, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("One version read needs no existence check.");

        private Task<WorkflowDefinitionVersion> Get() =>
            Missing ? throw new EntityNotFoundException("The workflow definition version was not found.") : Task.FromResult(Version());

        private static WorkflowDefinitionVersion Version() => new(DefinitionId, "1.0.0")
        {
            Id = VersionId,
            Definition = new WorkflowDefinition { Id = DefinitionId, Name = "Owned definition", TenantId = "tenant" },
            State = new WorkflowDefinitionState([], null, [], [], null)
        };
    }

    private sealed class NoLayouts : IWorkflowDefinitionVersionLayoutStore
    {
        public Task<WorkflowDefinitionVersionLayout?> FindByVersionIdAsync(string workflowDefinitionVersionId, CancellationToken cancellationToken = default)
            => Task.FromResult<WorkflowDefinitionVersionLayout?>(null);
    }
}
