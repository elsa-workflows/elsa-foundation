using Elsa.Api.AspNetCore;
using Elsa.Api.Compatibility.Testing.Endpoints;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Core.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Foundation.Identity.Abstractions.Authentication;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using Elsa.Mediator.Core.Contracts;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Design.Api.Authorization;
using Elsa.Workflows.Design.Api.Commands;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Requests;
using Elsa.Workflows.Design.Api.Services;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Exceptions;
using FastEndpoints;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Xunit;
using MediatorCommand = Elsa.Mediator.Core.Contracts.ICommand;

namespace Elsa.Workflows.Design.Api.Tests;

public sealed class WorkflowsDesignApiContractTests
{
    [Fact]
    public void Mapper_registers_exactly_the_27_owned_operations_with_stable_metadata()
    {
        using var services = new ServiceCollection().AddRouting().BuildServiceProvider();
        var routes = new TestEndpointRouteBuilder(services);

        Elsa.Workflows.Design.Api.WorkflowsDesignApi.MapWorkflowsDesignApi(routes);

        var endpoints = routes.DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>().ToArray();
        Assert.Equal(27, endpoints.Length);
        Assert.Equal(27, endpoints.Select(endpoint => endpoint.Metadata.GetMetadata<EndpointOwnershipMetadata>()?.OwnerId).Distinct().Count() == 1 ? endpoints.Length : 0);

        foreach (var endpoint in endpoints)
        {
            Assert.Equal("Elsa.Workflows.Design.Api", endpoint.Metadata.GetMetadata<EndpointOwnershipMetadata>()?.OwnerId);
            Assert.Equal(EndpointAuthoringModels.MinimalApi, endpoint.Metadata.GetMetadata<EndpointAuthoringMetadata>()?.Model);
            Assert.NotNull(endpoint.Metadata.GetMetadata<EndpointSecurityDispositionMetadata>());
            Assert.NotNull(endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>());
            Assert.False(string.IsNullOrWhiteSpace(endpoint.Metadata.GetMetadata<EndpointNameMetadata>()?.EndpointName));
        }
    }

    [Fact]
    public void Permission_metadata_contains_only_the_catalog_action()
    {
        using var services = new ServiceCollection().AddRouting().BuildServiceProvider();
        var routes = new TestEndpointRouteBuilder(services);
        Elsa.Workflows.Design.Api.WorkflowsDesignApi.MapWorkflowsDesignApi(routes);

        var endpoint = routes.DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>()
            .Single(candidate => candidate.RoutePattern.RawText == "design/workflows/definitions/{definitionId}" &&
                                 candidate.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains("GET") == true);
        var disposition = Assert.IsType<EndpointSecurityDispositionMetadata>(endpoint.Metadata.GetMetadata<EndpointSecurityDispositionMetadata>());
        var policy = Assert.IsType<PermissionPolicyParseResult>(new PermissionPolicyCodec().Parse(disposition.Value!));
        Assert.Contains(PermissionKey.Normalize(WorkflowDesignPermissions.Read), policy.Descriptor!.Permissions);
        Assert.DoesNotContain(PermissionKey.Normalize(PermissionKey.Wildcard), policy.Descriptor.Permissions);
        Assert.Single(policy.Descriptor.Permissions);
        Assert.Contains("*/*", Assert.IsAssignableFrom<IAcceptsMetadata>(endpoint.Metadata.GetMetadata<IAcceptsMetadata>()).ContentTypes);
    }

    [Fact]
    public void Every_owned_route_declares_exactly_one_catalog_permission()
    {
        using var services = new ServiceCollection().AddRouting().BuildServiceProvider();
        var routes = new TestEndpointRouteBuilder(services);
        Elsa.Workflows.Design.Api.WorkflowsDesignApi.MapWorkflowsDesignApi(routes);

        var manageRoutes = new HashSet<string>(StringComparer.Ordinal)
        {
            "POST design/workflows/definitions",
            "PATCH design/workflows/definitions/{definitionId}",
            "DELETE design/workflows/definitions/{definitionId}",
            "DELETE design/workflows/definitions/{definitionId}/permanent",
            "POST design/workflows/definitions/{definitionId}/restore",
            "POST design/workflows/definitions/submit",
            "POST design/workflows/drafts/{draftId}/promote",
            "POST design/workflows/drafts/{draftId}/promotion-preflight",
            "PUT design/workflows/drafts/{draftId}",
            "DELETE design/workflows/drafts/{draftId}",
            "POST design/workflows/versions/ingest"
        };
        var endpoints = routes.DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>().ToArray();
        Assert.Equal(27, endpoints.Length);
        foreach (var endpoint in endpoints)
        {
            var method = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Single();
            var key = $"{method} {endpoint.RoutePattern.RawText}";
            var disposition = Assert.IsType<EndpointSecurityDispositionMetadata>(endpoint.Metadata.GetMetadata<EndpointSecurityDispositionMetadata>());
            var policy = Assert.IsType<PermissionPolicyParseResult>(new PermissionPolicyCodec().Parse(disposition.Value!));
            var permissions = policy.Descriptor!.Permissions;
            Assert.Single(permissions);
            Assert.DoesNotContain(PermissionKey.Normalize(PermissionKey.Wildcard), permissions);
            Assert.Equal(PermissionKey.Normalize(manageRoutes.Contains(key) ? WorkflowDesignPermissions.Manage : WorkflowDesignPermissions.Read), permissions.Single());
        }
    }

    [Fact]
    public void Endpoint_metadata_uses_stable_owner_local_names_tags_and_consumed_security()
    {
        using var services = new ServiceCollection().AddRouting().BuildServiceProvider();
        var routes = new TestEndpointRouteBuilder(services);
        Elsa.Workflows.Design.Api.WorkflowsDesignApi.MapWorkflowsDesignApi(routes);

        var endpoints = routes.DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>().ToArray();
        Assert.All(endpoints, endpoint =>
        {
            Assert.Equal("Elsa.Workflows.Design.Api", endpoint.Metadata.GetMetadata<ITagsMetadata>()?.Tags.Single());
            Assert.StartsWith("ElsaWorkflowsDesignApiEndpoints", endpoint.Metadata.GetMetadata<EndpointNameMetadata>()?.EndpointName, StringComparison.Ordinal);
            Assert.NotEmpty(endpoint.Metadata.GetOrderedMetadata<ProducesResponseTypeMetadata>());
            Assert.NotNull(endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>());
        });
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("trusted-denied", HttpStatusCode.Forbidden)]
    [InlineData("trusted-read", HttpStatusCode.OK)]
    [InlineData("trusted-manage", HttpStatusCode.OK)]
    [InlineData("trusted", HttpStatusCode.OK)]
    [InlineData("external-read", HttpStatusCode.Unauthorized)]
    [InlineData("tenant-allowed", HttpStatusCode.OK)]
    [InlineData("tenant-denied", HttpStatusCode.Forbidden)]
    [InlineData("resource-denied", HttpStatusCode.Forbidden)]
    [InlineData("trusted-no-tenant", HttpStatusCode.Forbidden)]
    [InlineData("wildcard-tenant-denied", HttpStatusCode.Forbidden)]
    [InlineData("wildcard-no-tenant", HttpStatusCode.Forbidden)]
    [InlineData("manage-tenant-denied", HttpStatusCode.Forbidden)]
    [InlineData("manage-no-tenant", HttpStatusCode.Forbidden)]
    [InlineData("manage-resource-denied", HttpStatusCode.Forbidden)]
    [InlineData("wildcard-resource-denied", HttpStatusCode.Forbidden)]
    public async Task Requests_use_the_shared_normalized_permission_evaluator(string? identity, HttpStatusCode expected)
    {
        await using var host = await AuthorizationHost.StartAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/design/workflows/definitions");
        if (identity is not null)
            request.Headers.TryAddWithoutValidation(AuthorizationHost.IdentityHeader, identity);

        using var response = await host.Client.SendAsync(request);
        Assert.Equal(expected, response.StatusCode);

        using var retainedFastEndpointsRequest = new HttpRequestMessage(HttpMethod.Get, DesignRetainedFastEndpointsCanary.Route);
        if (identity is not null)
            retainedFastEndpointsRequest.Headers.TryAddWithoutValidation(AuthorizationHost.IdentityHeader, identity);
        using var retainedFastEndpointsResponse = await host.Client.SendAsync(retainedFastEndpointsRequest);
        Assert.Equal(expected, retainedFastEndpointsResponse.StatusCode);

        if (identity == "external-read")
        {
            Assert.Equal(0, host.AuthorizationProbe.EvaluatorCalls);
            Assert.Equal(0, host.AuthorizationProbe.ResourceCalls);
        }
    }

    [Fact]
    public async Task Promotion_preflight_deserializes_and_invokes_the_mapped_request_handler()
    {
        await using var host = await AuthorizationHost.StartAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/design/workflows/drafts/route-draft/promotion-preflight")
        {
            Content = new StringContent("{\"draftId\":\"body-draft\",\"requestedVersion\":\"1.2.0\"}", System.Text.Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation(AuthorizationHost.IdentityHeader, "trusted-manage");

        using var response = await host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("route-draft", host.Sender.LastRequest!.DraftId);
        Assert.Equal("1.2.0", host.Sender.LastRequest.RequestedVersion);
        Assert.Contains("\"isReady\":true", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.BadRequest)]
    [InlineData("{", HttpStatusCode.BadRequest)]
    public async Task Promotion_preflight_rejects_missing_or_malformed_json_before_handler(string? body, HttpStatusCode expected)
    {
        await using var host = await AuthorizationHost.StartAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/design/workflows/drafts/route-draft/promotion-preflight")
        {
            Content = body is null ? null : new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation(AuthorizationHost.IdentityHeader, "trusted-manage");

        using var response = await host.Client.SendAsync(request);
        Assert.Equal(expected, response.StatusCode);
        Assert.Null(host.Sender.LastRequest);
    }

    [Fact]
    public async Task Promotion_preflight_rejects_non_json_content_type_exactly()
    {
        await using var host = await AuthorizationHost.StartAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/design/workflows/drafts/route-draft/promotion-preflight")
        {
            Content = new StringContent("{}", System.Text.Encoding.UTF8, "text/plain")
        };
        request.Headers.TryAddWithoutValidation(AuthorizationHost.IdentityHeader, "trusted-manage");

        using var response = await host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Null(host.Sender.LastRequest);
    }

    [Fact]
    public async Task Soft_delete_binds_operation_key_reason_and_route_id()
    {
        await using var host = await AuthorizationHost.StartAsync();
        using var request = LifecycleRequest(HttpMethod.Delete, "/design/workflows/definitions/route-definition",
            "{\"operationKey\":\"soft-op\",\"definitionId\":\"body-definition\",\"reason\":\"cleanup\"}");

        var response = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var command = Assert.IsType<SoftDeleteDefinition>(host.CommandSender.LastCommand);
        Assert.Equal("soft-op", command.OperationKey);
        Assert.Equal("route-definition", command.DefinitionId);
        Assert.Equal("cleanup", command.Reason);
    }

    [Fact]
    public async Task Restore_binds_operation_key_and_route_id()
    {
        await using var host = await AuthorizationHost.StartAsync();
        using var request = LifecycleRequest(HttpMethod.Post, "/design/workflows/definitions/route-definition/restore",
            "{\"operationKey\":\"restore-op\",\"definitionId\":\"body-definition\"}");

        var response = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var command = Assert.IsType<RestoreDefinition>(host.CommandSender.LastCommand);
        Assert.Equal("restore-op", command.OperationKey);
        Assert.Equal("route-definition", command.DefinitionId);
    }

    [Fact]
    public async Task Permanent_delete_binds_operation_key_and_route_id()
    {
        await using var host = await AuthorizationHost.StartAsync();
        using var request = LifecycleRequest(HttpMethod.Delete, "/design/workflows/definitions/route-definition/permanent",
            "{\"operationKey\":\"permanent-op\",\"definitionId\":\"body-definition\"}");

        var response = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var command = Assert.IsType<DeleteDefinitionPermanently>(host.CommandSender.LastCommand);
        Assert.Equal("permanent-op", command.OperationKey);
        Assert.Equal("route-definition", command.DefinitionId);
    }

    [Fact]
    public async Task Permanent_delete_reports_conflict_when_definition_is_not_soft_deleted()
    {
        await using var host = await AuthorizationHost.StartAsync();
        using var request = LifecycleRequest(HttpMethod.Delete, "/design/workflows/definitions/route-definition/permanent",
            "{\"operationKey\":\"permanent-op\",\"definitionId\":\"body-definition\"}");
        request.Headers.Remove(AuthorizationHost.IdentityHeader);
        request.Headers.TryAddWithoutValidation(AuthorizationHost.IdentityHeader, "trusted-delete-409");

        using var response = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("must be soft-deleted", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.IsType<DeleteDefinitionPermanently>(host.CommandSender.LastCommand);
    }

    [Fact]
    public async Task Discard_binds_operation_key_and_route_id()
    {
        await using var host = await AuthorizationHost.StartAsync();
        using var request = LifecycleRequest(HttpMethod.Delete, "/design/workflows/drafts/route-draft",
            "{\"operationKey\":\"discard-op\",\"draftId\":\"body-draft\"}");

        var response = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var command = Assert.IsType<DiscardDraft>(host.CommandSender.LastCommand);
        Assert.Equal("discard-op", command.OperationKey);
        Assert.Equal("route-draft", command.DraftId);
    }

    [Fact]
    public async Task Update_binds_operation_key_and_route_id_before_command_dispatch()
    {
        await using var host = await AuthorizationHost.StartAsync();
        using var request = LifecycleRequest(HttpMethod.Patch, "/design/workflows/definitions/route-definition",
            "{\"operationKey\":\"update-op\",\"definitionId\":\"body-definition\",\"name\":\"Updated\"}");

        var response = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var command = Assert.IsType<UpdateDefinitionMetadata>(host.CommandSender.LastCommand);
        Assert.Equal("update-op", command.OperationKey);
        Assert.Equal("route-definition", command.DefinitionId);
    }

    [Fact]
    public async Task Replace_binds_operation_key_and_route_id_before_command_dispatch()
    {
        await using var host = await AuthorizationHost.StartAsync();
        using var request = LifecycleRequest(HttpMethod.Put, "/design/workflows/drafts/route-draft",
            "{\"operationKey\":\"replace-op\",\"draftId\":\"body-draft\",\"state\":{\"activities\":[],\"connections\":[]}}");

        var response = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var command = Assert.IsType<ReplaceDraft>(host.CommandSender.LastCommand);
        Assert.Equal("replace-op", command.OperationKey);
        Assert.Equal("route-draft", command.DraftId);
    }

    [Fact]
    public async Task Promote_binds_operation_key_and_route_id_before_command_dispatch()
    {
        await using var host = await AuthorizationHost.StartAsync();
        using var request = LifecycleRequest(HttpMethod.Post, "/design/workflows/drafts/route-draft/promote",
            "{\"operationKey\":\"promote-op\",\"draftId\":\"body-draft\",\"requestedVersion\":\"1.2.0\"}");

        var response = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var command = Assert.IsType<PromoteDraft>(host.CommandSender.LastCommand);
        Assert.Equal("promote-op", command.OperationKey);
        Assert.Equal("route-draft", command.DraftId);
    }

    [Fact]
    public async Task Activity_options_binds_route_activity_and_input_keys_before_provider_invocation()
    {
        await using var host = await AuthorizationHost.StartAsync();
        using var request = LifecycleRequest(HttpMethod.Post, "/design/workflows/activities/route-activity/inputs/route-input/options",
            "{\"activityVersionId\":\"body-activity\",\"inputName\":\"body-input\",\"nodeId\":\"node\",\"workflowState\":{\"rootActivity\":{\"nodeId\":\"node\",\"activityVersionId\":\"route-activity\",\"children\":[]}}}");

        var response = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("route-activity", host.ActivityStore.LastVersionId);
        Assert.Equal("route-input", host.ActivityProvider.Context!.Input.Name);
        Assert.Equal("route-activity", host.ActivityProvider.Context.Activity.ActivityVersionId);
    }

    [Theory]
    [InlineData("DELETE", "/design/workflows/definitions/route-definition")]
    [InlineData("POST", "/design/workflows/definitions/route-definition/restore")]
    [InlineData("DELETE", "/design/workflows/definitions/route-definition/permanent")]
    [InlineData("DELETE", "/design/workflows/drafts/route-draft")]
    public async Task Lifecycle_commands_preserve_missing_body_dispositions_and_dispatch(string method, string route)
    {
        await using var host = await AuthorizationHost.StartAsync();
        using var request = new HttpRequestMessage(new HttpMethod(method), route);
        request.Headers.TryAddWithoutValidation(AuthorizationHost.IdentityHeader, "trusted-manage");

        var response = await host.Client.SendAsync(request);

        var expectedStatus = method == "DELETE"
            ? HttpStatusCode.NoContent
            : route.Contains("/restore", StringComparison.Ordinal) ? HttpStatusCode.UnsupportedMediaType : HttpStatusCode.BadRequest;
        Assert.Equal(expectedStatus, response.StatusCode);
        if (method == "DELETE")
        {
            Assert.NotNull(host.CommandSender.LastCommand);
            var command = host.CommandSender.LastCommand!;
            if (route.Contains("permanent", StringComparison.Ordinal))
                Assert.Equal("route-definition", Assert.IsType<DeleteDefinitionPermanently>(command).DefinitionId);
            else if (route.Contains("drafts", StringComparison.Ordinal))
                Assert.Equal("route-draft", Assert.IsType<DiscardDraft>(command).DraftId);
            else
                Assert.Equal("route-definition", Assert.IsType<SoftDeleteDefinition>(command).DefinitionId);
        }
        else
            Assert.Null(host.CommandSender.LastCommand);
    }

    [Fact]
    public async Task Lifecycle_commands_reject_malformed_json_but_dispatch_delete_non_json()
    {
        await using var host = await AuthorizationHost.StartAsync();
        using var malformed = LifecycleRequest(HttpMethod.Delete, "/design/workflows/definitions/route-definition", "{");
        using var malformedResponse = await host.Client.SendAsync(malformed);
        Assert.Equal(HttpStatusCode.BadRequest, malformedResponse.StatusCode);
        Assert.Null(host.CommandSender.LastCommand);

        using var nonJson = LifecycleRequest(HttpMethod.Delete, "/design/workflows/drafts/route-draft", "{}", "text/plain");
        using var nonJsonResponse = await host.Client.SendAsync(nonJson);
        Assert.Equal(HttpStatusCode.NoContent, nonJsonResponse.StatusCode);
        var command = Assert.IsType<DiscardDraft>(host.CommandSender.LastCommand);
        Assert.Equal("route-draft", command.DraftId);
    }

    private static HttpRequestMessage LifecycleRequest(HttpMethod method, string route, string body, string contentType = "application/json")
    {
        var request = new HttpRequestMessage(method, route)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, contentType)
        };
        request.Headers.TryAddWithoutValidation(AuthorizationHost.IdentityHeader, "trusted-manage");
        return request;
    }

    private sealed class TestEndpointRouteBuilder(IServiceProvider serviceProvider) : IEndpointRouteBuilder
    {
        public IServiceProvider ServiceProvider { get; } = serviceProvider;
        public ICollection<EndpointDataSource> DataSources { get; } = [];
        public IApplicationBuilder CreateApplicationBuilder() => new ApplicationBuilder(ServiceProvider);
    }

    internal sealed class AuthorizationHost(IHost host) : IAsyncDisposable
    {
        public const string IdentityHeader = "X-Workflow-Design-Identity";
        public HttpClient Client { get; } = host.GetTestClient();
        public DefinitionsRequestSender Sender { get; } = host.Services.GetRequiredService<DefinitionsRequestSender>();
        public DefinitionsCommandSender CommandSender { get; } = host.Services.GetRequiredService<DefinitionsCommandSender>();
        public ContractActivityVersionStore ActivityStore { get; } = host.Services.GetRequiredService<ContractActivityVersionStore>();
        public ContractActivityOptionsProvider ActivityProvider { get; } = host.Services.GetRequiredService<ContractActivityOptionsProvider>();
        public AuthorizationProbe AuthorizationProbe { get; } = host.Services.GetRequiredService<AuthorizationProbe>();

        public static async Task<AuthorizationHost> StartAsync()
        {
            var host = new HostBuilder()
                .ConfigureWebHost(webHost =>
                {
                    webHost.UseTestServer();
                    webHost.ConfigureServices(services =>
                    {
                        services.AddRouting();
                        services.AddHttpContextAccessor();
                        services.AddAuthentication(AuthenticationHandler.SchemeName)
                            .AddScheme<AuthenticationSchemeOptions, AuthenticationHandler>(AuthenticationHandler.SchemeName, _ => { });
                        services.AddAuthorization();
                        services.AddSingleton<AuthorizationProbe>();
                        services.AddScoped<IPermissionResourceHandler, DesignResourcePermissionHandler>();
                        services.AddSingleton<ContractActivityVersionStore>();
                        services.AddSingleton<IActivityDefinitionVersionStore>(services => services.GetRequiredService<ContractActivityVersionStore>());
                        services.AddSingleton<IActivityStructureService, ContractActivityStructureService>();
                        services.AddSingleton<ContractActivityOptionsProvider>();
                        services.AddSingleton<IActivityInputOptionsProvider>(services => services.GetRequiredService<ContractActivityOptionsProvider>());
                        services.AddOpenApi();
                        services.AddFoundationIdentityAbstractions(options =>
                            options.NormalizedAuthenticationTypes = new HashSet<string>([AuthenticationHandler.SchemeName], StringComparer.Ordinal));
                        new WorkflowsDesignApiFeature().ConfigureServices(services);
                        services.ReplacePermissionEvaluator<RecordingPermissionEvaluator>();
                        services.AddSingleton<DefinitionsRequestSender>();
                        services.AddSingleton<IRequestSender>(services => services.GetRequiredService<DefinitionsRequestSender>());
                        services.AddSingleton<DefinitionsCommandSender>();
                        services.AddSingleton<ICommandSender>(services => services.GetRequiredService<DefinitionsCommandSender>());
                        services.AddFastEndpoints(options => options.Assemblies = [typeof(DesignRetainedFastEndpointsCanary).Assembly]);
                    });
                    webHost.Configure(app =>
                    {
                        app.UseRouting();
                        app.UseAuthentication();
                        app.UseAuthorization();
                        app.UseEndpoints(endpoints =>
                        {
                            endpoints.MapFastEndpoints(config => config.Security.PermissionsClaimType = IdentityClaimTypes.Permission);
                            WorkflowsDesignApi.MapWorkflowsDesignApi(endpoints);
                        });
                    });
                })
                .Build();
            await host.StartAsync();
            return new AuthorizationHost(host);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await host.StopAsync();
            host.Dispose();
        }
    }

    private sealed class AuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : Microsoft.AspNetCore.Authentication.AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "WorkflowDesignContract";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = Request.Headers[AuthorizationHost.IdentityHeader].ToString();
            if (string.IsNullOrWhiteSpace(identity))
                return Task.FromResult(AuthenticateResult.NoResult());

            var claims = new List<Claim>();
            if (identity == "external-read")
            {
                claims.Add(new Claim(IdentityClaimTypes.Normalized, "v1"));
                claims.Add(new Claim(IdentityClaimTypes.Provider, "external"));
                claims.Add(new Claim(IdentityClaimTypes.Permission, "external.workflow-design.read"));
            }
            else if (identity == "trusted-denied")
            {
                claims.Add(new Claim(IdentityClaimTypes.Normalized, "v1"));
                claims.Add(new Claim(IdentityClaimTypes.Permission, "workflow-design.other"));
            }
            else if (identity is "trusted" or "trusted-read" or "trusted-manage" or "trusted-delete-409" or "tenant-allowed" or "tenant-denied" or "resource-denied" or "trusted-no-tenant" or "wildcard-tenant-denied" or "wildcard-no-tenant" or "manage-tenant-denied" or "manage-no-tenant" or "manage-resource-denied" or "wildcard-resource-denied")
            {
                claims.Add(new Claim(IdentityClaimTypes.Normalized, "v1"));
                claims.Add(new Claim(IdentityClaimTypes.Permission,
                    identity is "trusted" or "wildcard-tenant-denied" or "wildcard-no-tenant" or "wildcard-resource-denied" ? PermissionKey.Wildcard : identity is "trusted-manage" or "trusted-delete-409" or "manage-tenant-denied" or "manage-no-tenant" or "manage-resource-denied" ? WorkflowDesignPermissions.Manage : WorkflowDesignPermissions.Read));
                if (identity is not ("trusted-no-tenant" or "wildcard-no-tenant" or "manage-no-tenant"))
                    claims.Add(new Claim(IdentityClaimTypes.TenantId, identity is "tenant-denied" or "wildcard-tenant-denied" or "manage-tenant-denied" ? "tenant-other" : "tenant-design"));
                if (identity is "resource-denied" or "manage-resource-denied" or "wildcard-resource-denied")
                    claims.Add(new Claim("elsa.design.resource", "deny"));
            }
            else
                claims.Add(new Claim(IdentityClaimTypes.Normalized, "v1"));

            var authenticationType = identity == "external-read" ? "ExternalUntrusted" : SchemeName;
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(
                new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType)), SchemeName)));
        }
    }

    internal sealed class DefinitionsRequestSender(IHttpContextAccessor contextAccessor) : IRequestSender
    {
        public PreflightDraftPromotion? LastRequest { get; private set; }

        public Task<T> Send<T>(IRequest<T> request, CancellationToken cancellationToken = default) where T : notnull
        {
            if (request is ListDefinitions)
                return Task.FromResult((T)(object)new WorkflowDefinitionListView([]));

            if (request is PreflightDraftPromotion preflight)
            {
                LastRequest = preflight;
                return Task.FromResult((T)(object)new PromotionPreflightAssessmentView(true, "exact", preflight.RequestedVersion, preflight.RequestedVersion, "1.0.0", []));
            }

            if (request is GetDefinition && Scenario == "trusted-not-found")
                throw new EntityNotFoundException("definition sample was not found");

            throw new InvalidOperationException($"Unexpected request '{request.GetType().FullName}'.");
        }

        private string Scenario => contextAccessor.HttpContext?.Request.Headers[AuthorizationHost.IdentityHeader].ToString() ?? "";
    }

    internal sealed class DefinitionsCommandSender(IHttpContextAccessor contextAccessor) : ICommandSender
    {
        public object? LastCommand { get; private set; }
        private string Scenario => contextAccessor.HttpContext?.Request.Headers[AuthorizationHost.IdentityHeader].ToString() ?? "";

        public Task<T> Send<T>(Elsa.Mediator.Core.Contracts.ICommand<T> command, CancellationToken cancellationToken = default) where T : notnull
        {
            LastCommand = command;
            return Scenario switch
            {
                "trusted-promote-404" => throw new EntityNotFoundException("draft sample was not found"),
                "trusted-promote-409" => throw new WorkflowDefinitionVersionConflictException("definition sample", "1.0.0"),
                "trusted-promote-500" => throw new InvalidOperationException("deterministic command failure"),
                _ => Task.FromResult(default(T)!)
            };
        }

        public Task Send(MediatorCommand command, CancellationToken cancellationToken = default)
        {
            LastCommand = command;
            return Scenario switch
            {
                "trusted-delete-404" => throw new EntityNotFoundException("definition sample was not found"),
                "trusted-delete-501" => throw new PermanentDeletionUnavailableException("sample"),
                "trusted-delete-409" => throw new WorkflowDefinitionNotSoftDeletedException("sample"),
                "trusted-delete-500" => throw new InvalidOperationException("deterministic command failure"),
                _ => Task.CompletedTask
            };
        }
    }

}

internal sealed class AuthorizationProbe
{
    public int EvaluatorCalls { get; set; }
    public int ResourceCalls { get; set; }
}

internal sealed class RecordingPermissionEvaluator(IPermissionCatalog catalog, AuthorizationProbe probe) : IPermissionEvaluator
{
    private readonly ClaimsPermissionEvaluator inner = new(catalog);

    public ValueTask<PermissionEvaluationResult> EvaluateAsync(
        PermissionEvaluationContext context,
        CancellationToken cancellationToken = default)
    {
        probe.EvaluatorCalls++;
        return inner.EvaluateAsync(context, cancellationToken);
    }
}

internal sealed class ContractActivityVersionStore : IActivityDefinitionVersionStore
{
    public string? LastVersionId { get; private set; }

    public Task<ActivityDefinitionVersion> GetAsync(string versionId, CancellationToken cancellationToken = default)
    {
        LastVersionId = versionId;
        var input = new InputDefinition("route-input", "route-input", new("String"), null, "Route input", null, false,
            UISpecifications: JsonSerializer.SerializeToElement(new { optionsProvider = new { key = "contract-options" } }));
        return Task.FromResult(new ActivityDefinitionVersion("1.0.0", "contract-definition") { Id = versionId, Inputs = [input] });
    }

    public Task<ActivityDefinitionVersion> GetWithDefinitionAsync(string versionId, CancellationToken cancellationToken = default) => GetAsync(versionId, cancellationToken);
    public Task<ActivityDefinitionVersion?> FindByDefinitionAndSortKeyAsync(string definitionId, string semVerSortKey, CancellationToken cancellationToken = default) => Task.FromResult<ActivityDefinitionVersion?>(null);
    public Task<IReadOnlyList<ActivityDefinitionVersion>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ActivityDefinitionVersion>>([]);
    public Task<IReadOnlyList<ActivityDefinitionVersion>> ListByDefinitionIdsAsync(IEnumerable<string> definitionIds, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ActivityDefinitionVersion>>([]);
    public Task<IReadOnlyList<ActivityDefinitionVersion>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ActivityDefinitionVersion>>([]);
}

internal sealed class ContractActivityStructureService : IActivityStructureService
{
    public IReadOnlyCollection<ActivityChildProjection> ProjectChildren(ActivityNode activity) => [];
    public ActivityNode ReplaceChildren(ActivityNode activity, IReadOnlyCollection<ActivityChildProjection> childProjections) => activity;
    public ActivityNodeStructure? CompileExecutableStructure(ActivityNode activity) => null;
    public IReadOnlyCollection<VariableDefinition> ProjectScopedVariables(ActivityNode activity) => [];
    public bool SupportsScopedVariables(ActivityNode activity) => false;
}

internal sealed class ContractActivityOptionsProvider : IActivityInputOptionsProvider
{
    public string Key => "contract-options";
    public ActivityInputOptionsContext? Context { get; private set; }

    public ValueTask<IReadOnlyList<ActivityInputOption>> GetOptionsAsync(ActivityInputOptionsContext context, CancellationToken cancellationToken = default)
    {
        Context = context;
        return ValueTask.FromResult<IReadOnlyList<ActivityInputOption>>([new("Route option", "route")]);
    }
}

internal sealed class DesignResourcePermissionHandler : IPermissionResourceHandler
{
    public DesignResourcePermissionHandler(AuthorizationProbe probe) => this.probe = probe;

    private readonly AuthorizationProbe probe;

    public ValueTask<PermissionEvaluationResult?> EvaluateAsync(
        PermissionEvaluationContext context,
        CancellationToken cancellationToken = default)
    {
        probe.ResourceCalls++;
        var tenant = context.TenantId;
        var resource = context.Principal.FindFirst("elsa.design.resource")?.Value;
        return ValueTask.FromResult<PermissionEvaluationResult?>(tenant != "tenant-design" || resource == "deny"
            ? PermissionEvaluationResult.Denied("The design resource or tenant denied the request.")
            : null);
    }
}

public sealed class DesignRetainedFastEndpointsCanary : ElsaEndpointWithoutRequest<string>
{
    public const string Route = "/design/retained-fe-canary";

    public override void Configure()
    {
        Get(Route);
        ConfigurePermissions(WorkflowDesignPermissions.Read);
    }

    public override Task HandleAsync(CancellationToken cancellationToken) =>
        Send.OkAsync("retained-fe", cancellationToken);
}
