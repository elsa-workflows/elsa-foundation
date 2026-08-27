using Elsa.Agent.Api;
using Elsa.Agent.Api.Constants;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Agent.Tests;

public sealed class AgentApiMappingTests
{
    [Fact]
    public void Maps_exactly_eleven_named_operations()
    {
        using var services = new ServiceCollection().AddRouting().AddElsaEndpoints().BuildServiceProvider();
        var app = new TestEndpointRouteBuilder(services);

        AgentApi.MapAgentApi(app);

        var names = app.DataSources
            .SelectMany(x => x.Endpoints)
            .Select(x => x.Metadata.GetMetadata<EndpointNameMetadata>()?.EndpointName)
            .Where(x => x is not null)
            .OrderBy(x => x)
            .ToArray();

        Assert.Equal(11, names.Length);
        Assert.All(names, name => Assert.StartsWith("ElsaAgentApiEndpoints", name, StringComparison.Ordinal));
    }

    [Fact]
    public void Every_route_declares_owner_minimal_authoring_and_exactly_one_permission_disposition()
    {
        using var services = new ServiceCollection().AddRouting().AddElsaEndpoints().BuildServiceProvider();
        var app = new TestEndpointRouteBuilder(services);
        AgentApi.MapAgentApi(app);

        var permissions = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ElsaAgentApiEndpointsBootstrap"] = AgentPermissionKeys.Use,
            ["ElsaAgentApiEndpointsCreateSession"] = AgentPermissionKeys.Use,
            ["ElsaAgentApiEndpointsGetSession"] = AgentPermissionKeys.Use,
            ["ElsaAgentApiEndpointsPostMessage"] = AgentPermissionKeys.Use,
            ["ElsaAgentApiEndpointsCancelTurn"] = AgentPermissionKeys.Use,
            ["ElsaAgentApiEndpointsStreamSession"] = AgentPermissionKeys.Use,
            ["ElsaAgentApiEndpointsFeedback"] = AgentPermissionKeys.Use,
            ["ElsaAgentApiEndpointsApproveProposal"] = AgentPermissionKeys.Proposals,
            ["ElsaAgentApiEndpointsDenyProposal"] = AgentPermissionKeys.Proposals,
            ["ElsaAgentApiEndpointsExecuteProposal"] = AgentPermissionKeys.Proposals,
            ["ElsaAgentApiEndpointsAudit"] = AgentPermissionKeys.Audit
        };
        var codec = new PermissionPolicyCodec();

        var endpoints = app.DataSources.SelectMany(x => x.Endpoints).Cast<RouteEndpoint>().ToArray();
        Assert.Equal(permissions.Count, endpoints.Length);
        foreach (var endpoint in endpoints)
        {
            var operationId = endpoint.Metadata.GetMetadata<EndpointNameMetadata>()?.EndpointName;
            Assert.NotNull(operationId);
            Assert.Equal("Elsa.Agent.Api", endpoint.Metadata.GetMetadata<EndpointOwnershipMetadata>()?.OwnerId);
            Assert.Equal(EndpointAuthoringModels.MinimalApi, endpoint.Metadata.GetMetadata<EndpointAuthoringMetadata>()?.Model);

            var dispositions = endpoint.Metadata.GetOrderedMetadata<EndpointSecurityDispositionMetadata>();
            Assert.Single(dispositions);
            Assert.Equal(EndpointSecurityDispositionKind.Permission, dispositions[0].Kind);
            Assert.Equal(codec.Format(PermissionPolicyDescriptor.Single(permissions[operationId!])), dispositions[0].Value);

            var authorization = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();
            Assert.Single(authorization);
            Assert.Equal(dispositions[0].Value, authorization[0].Policy);
            Assert.DoesNotContain(endpoint.Metadata, item => item is IAllowAnonymous);
        }
    }

    private sealed class TestEndpointRouteBuilder(IServiceProvider serviceProvider) : IEndpointRouteBuilder
    {
        public IServiceProvider ServiceProvider { get; } = serviceProvider;
        public ICollection<EndpointDataSource> DataSources { get; } = [];
        public IApplicationBuilder CreateApplicationBuilder() => new ApplicationBuilder(ServiceProvider);
    }
}
