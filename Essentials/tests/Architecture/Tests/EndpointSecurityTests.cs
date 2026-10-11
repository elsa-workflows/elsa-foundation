using Elsa.Api.Capabilities.Authorization;
using Elsa.Activities.Bpmn.Interchange;
using Elsa.Activities.Design.Api;
using Elsa.Activities.Design.Api.Authorization;
using Elsa.Api.AspNetCore;
using Elsa.Api.Capabilities;
using Elsa.Api.Compatibility.Testing.Manifests;
using Elsa.Api.Compatibility.Testing.Security;
using Elsa.Expressions.Api;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Workflows.Design.Api;
using Elsa.Workflows.Publishing.Api;
using Elsa.Workflows.Publishing.Api.Authorization;
using Elsa.Workflows.Runtime.Api;
using Elsa3.Activities.Design.Import;
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using NativeEndpoints;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace Elsa.Architecture.Tests;

/// <summary>
/// Pins the stable management-client permission vocabulary and the security metadata the management domain
/// API mappers put on the endpoints they map.
/// </summary>
public sealed class EndpointSecurityTests
{
    // Anchored on a public type per owning assembly. AppDomain.GetAssemblies() only reports assemblies
    // already loaded, and a using directive alone does not load one, so the anchors force the load.
    private static readonly Assembly[] PermissionDeclaringAssemblies =
    [
        typeof(Elsa.Workflows.Design.Api.Authorization.WorkflowDesignPermissions).Assembly,
        typeof(Elsa.Activities.Design.Api.Authorization.ActivityDesignPermissions).Assembly,
        typeof(Elsa.Expressions.Api.Authorization.ExpressionsPermissions).Assembly,
        typeof(Elsa.Workflows.Publishing.Api.Authorization.WorkflowPublishingPermissions).Assembly,
        typeof(Elsa.Workflows.Runtime.Api.Authorization.WorkflowRuntimePermissions).Assembly,
        typeof(Elsa.Api.Capabilities.Authorization.ApiCapabilitiesPermissions).Assembly,
        typeof(Elsa3.Activities.Design.Import.Endpoints.ReusableActivityImportApi).Assembly,
        typeof(Elsa.Activities.Bpmn.Interchange.Endpoints.BpmnInterchangeApi).Assembly
    ];

    /// <summary>
    /// Reads a declared permission constant by its fully-qualified "Namespace.Type.Field" name.
    /// Reflection is used because two owners declare their permissions as <c>internal</c>; the guard
    /// should check the real declaration rather than force an accessibility change to suit a test.
    /// </summary>
    private static string ReadDeclaredPermission(string qualifiedField)
    {
        var separator = qualifiedField.LastIndexOf('.');
        var typeName = qualifiedField[..separator];
        var fieldName = qualifiedField[(separator + 1)..];
        var type = PermissionDeclaringAssemblies
            .Select(assembly => assembly.GetType(typeName, throwOnError: false))
            .FirstOrDefault(candidate => candidate is not null)
            ?? throw new InvalidOperationException($"Permission type '{typeName}' was not found. Is its assembly referenced?");
        var field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException($"Permission field '{fieldName}' was not found on '{typeName}'.");
        return Assert.IsType<string>(field.GetRawConstantValue());
    }

    [Fact]
    public void Management_permission_names_are_stable_unique_and_action_scoped()
    {
        // Each management permission name is declared by the domain that enforces it. There is no
        // shared catalog to drift from: the wildcard is the only name Foundation Identity owns. This
        // reads the owning types directly, including the two internal ones, so the guard checks the
        // real sources rather than a copy of them.
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Elsa.Workflows.Design.Api.Authorization.WorkflowDesignPermissions.Read"] = "workflow-design.read",
            ["Elsa.Workflows.Design.Api.Authorization.WorkflowDesignPermissions.Manage"] = "workflow-design.manage",
            ["Elsa.Activities.Design.Api.Authorization.ActivityDesignPermissions.Read"] = "activity-design.read",
            ["Elsa.Activities.Design.Api.Authorization.ActivityDesignPermissions.Manage"] = "activity-design.manage",
            ["Elsa.Expressions.Api.Authorization.ExpressionsPermissions.Read"] = "expressions.read",
            ["Elsa.Workflows.Publishing.Api.Authorization.WorkflowPublishingPermissions.Read"] = "workflow-publishing.read",
            ["Elsa.Workflows.Publishing.Api.Authorization.WorkflowPublishingPermissions.Manage"] = "workflow-publishing.manage",
            ["Elsa.Workflows.Runtime.Api.Authorization.WorkflowRuntimePermissions.WorkflowRuntimeRead"] = "workflow-runtime.read",
            ["Elsa.Workflows.Runtime.Api.Authorization.WorkflowRuntimePermissions.WorkflowRuntimeExecute"] = "workflow-runtime.execute",
            ["Elsa.Workflows.Runtime.Api.Authorization.WorkflowRuntimePermissions.WorkflowRuntimeManage"] = "workflow-runtime.manage",
            ["Elsa.Api.Capabilities.Authorization.ApiCapabilitiesPermissions.Read"] = "api-capabilities.read",
            ["Elsa3.Activities.Design.Import.Endpoints.Elsa3ImportPermissions.Read"] = "elsa3-import.read",
            ["Elsa3.Activities.Design.Import.Endpoints.Elsa3ImportPermissions.Manage"] = "elsa3-import.manage",
            ["Elsa.Activities.Bpmn.Interchange.Endpoints.BpmnInterchangePermissions.Read"] = "bpmn-interchange.read",
            ["Elsa.Activities.Bpmn.Interchange.Endpoints.BpmnInterchangePermissions.Manage"] = "bpmn-interchange.manage"
        };
        var actual = expected.Keys.ToDictionary(key => key, ReadDeclaredPermission, StringComparer.Ordinal);

        Assert.Equal(expected.OrderBy(x => x.Key), actual.OrderBy(x => x.Key));
        Assert.Equal(actual.Count, actual.Values.Distinct(StringComparer.Ordinal).Count());
        Assert.All(actual.Values, value => Assert.Matches("^[a-z][a-z0-9-]*\\.(read|manage|execute)$", value));
        Assert.NotEqual(
            actual["Elsa.Api.Capabilities.Authorization.ApiCapabilitiesPermissions.Read"],
            actual["Elsa.Workflows.Design.Api.Authorization.WorkflowDesignPermissions.Read"]);
        // The shared convention owns the wildcard and nothing else.
        Assert.Equal(
            [nameof(PermissionNames.All)],
            typeof(PermissionNames).GetFields(BindingFlags.Public | BindingFlags.Static).Select(field => field.Name));
    }

    [Fact]
    public void Metadata_gate_rejects_missing_and_ambiguous_security_dispositions()
    {
        var missing = Endpoint("/missing", dispositions: []);
        var ambiguous = Endpoint("/ambiguous",
        [
            EndpointSecurityDispositionMetadata.Public("test", "Exercises ambiguous security metadata."),
            EndpointSecurityDispositionMetadata.NamedPolicy("owned-policy", "Elsa.Tests")
        ]);

        var missingException = Assert.Throws<EndpointManifestValidationException>(() =>
            new EndpointManifestBuilder([new FixedEndpointDataSource([missing])]).Build());
        var ambiguousException = Assert.Throws<EndpointManifestValidationException>(() =>
            new EndpointManifestBuilder([new FixedEndpointDataSource([ambiguous])]).Build());

        Assert.Contains("missing security disposition", missingException.Message, StringComparison.Ordinal);
        Assert.Contains("ambiguous security dispositions", ambiguousException.Message, StringComparison.Ordinal);
        Assert.Contains("GET /missing", missingException.Message, StringComparison.Ordinal);
        Assert.Contains("GET /ambiguous", ambiguousException.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_management_permission_has_one_active_feature_owned_catalog_contributor()
    {
        var services = new ServiceCollection();
        new ActivitiesDesignApiFeature().ConfigureServices(services);
        new ActivitiesBpmnInterchangeFeature().ConfigureServices(services);
        new ApiCapabilitiesFeature().ConfigureServices(services);
        new ExpressionsApiFeature().ConfigureServices(services);
        new WorkflowsDesignApiFeature().ConfigureServices(services);
        new WorkflowsPublishingApiFeature().ConfigureServices(services);
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);
        new Elsa3ImportActivitiesFeature().ConfigureServices(services);
        using var provider = services.AddElsaEndpoints().BuildServiceProvider();
        var contributors = provider.GetServices<IPermissionContributor>().ToArray();
        var permissions = typeof(PermissionNames).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.Name != nameof(PermissionNames.All))
            .Select(field => Assert.IsType<string>(field.GetRawConstantValue()))
            .Select(permission => new PermissionConsumption(new EndpointIdentity("/catalog-validation", "GET"), "Elsa.Tests", permission));

        var result = PermissionOwnershipValidator.Validate(contributors, permissions);

        Assert.Equal(8, contributors.Select(contributor => contributor.OwnerId).Distinct(StringComparer.Ordinal).Count());
        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Issues.Select(issue =>
            $"{issue.Code}: {issue.Permission}: {issue.Message}")));
    }

    [Fact]
    public void Activities_design_minimal_api_declares_38_owned_secure_routes_with_one_catalog_action_each()
    {
        using var serviceProvider = new ServiceCollection().AddRouting().AddElsaEndpoints().BuildServiceProvider();
        var routes = new TestEndpointRouteBuilder(serviceProvider);
        ActivitiesDesignApi.MapActivitiesDesignApi(routes);
        var endpoints = routes.DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>().ToArray();

        Assert.Equal(38, endpoints.Length);
        foreach (var endpoint in endpoints)
        {
            var authorization = Assert.Single(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>());
            var policy = new PermissionPolicyCodec().Parse(authorization.Policy!);
            var descriptor = Assert.IsType<PermissionPolicyDescriptor>(policy.Descriptor);
            var permission = Assert.Single(descriptor.Permissions);

            Assert.Equal(PermissionPolicyParseStatus.Valid, policy.Status);
            Assert.Equal(PermissionRequirementMode.Single, descriptor.Mode);
            Assert.Contains(permission, new[]
            {
                PermissionKey.Normalize(ActivityDesignPermissions.Read),
                PermissionKey.Normalize(ActivityDesignPermissions.Manage)
            });
            Assert.NotEqual(PermissionKey.Wildcard, permission);
            Assert.Null(endpoint.Metadata.GetMetadata<IAllowAnonymous>());
            Assert.Equal(
                "Elsa.Activities.Design.Api",
                Assert.IsType<EndpointOwnershipMetadata>(endpoint.Metadata.GetMetadata<EndpointOwnershipMetadata>()).OwnerId);
            Assert.Equal(
                EndpointAuthoringModels.MinimalApi,
                Assert.IsType<EndpointAuthoringMetadata>(endpoint.Metadata.GetMetadata<EndpointAuthoringMetadata>()).Model);

            var disposition = Assert.Single(endpoint.Metadata.GetOrderedMetadata<EndpointSecurityDispositionMetadata>());
            Assert.Equal(EndpointSecurityDispositionKind.Permission, disposition.Kind);
            var dispositionPolicy = new PermissionPolicyCodec().Parse(disposition.Value!);
            Assert.Equal(PermissionPolicyParseStatus.Valid, dispositionPolicy.Status);
            Assert.Equal([permission], dispositionPolicy.Descriptor!.Permissions);
        }
    }

    [Fact]
    public void Publishing_minimal_api_declares_23_owned_secure_routes_with_one_catalog_action_each()
    {
        using var serviceProvider = new ServiceCollection().AddRouting().AddElsaEndpoints().BuildServiceProvider();
        var routes = new TestEndpointRouteBuilder(serviceProvider);
        WorkflowsPublishingApi.MapWorkflowsPublishingApi(routes);
        var endpoints = routes.DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>().ToArray();

        Assert.Equal(23, endpoints.Length);
        foreach (var endpoint in endpoints)
        {
            var authorization = Assert.Single(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>());
            var policy = new PermissionPolicyCodec().Parse(authorization.Policy!);
            var descriptor = Assert.IsType<PermissionPolicyDescriptor>(policy.Descriptor);
            var permission = Assert.Single(descriptor.Permissions);

            Assert.Equal(PermissionPolicyParseStatus.Valid, policy.Status);
            Assert.Equal(PermissionRequirementMode.Single, descriptor.Mode);
            Assert.Contains(permission, new[]
            {
                PermissionKey.Normalize(WorkflowPublishingPermissions.Read),
                PermissionKey.Normalize(WorkflowPublishingPermissions.Manage)
            });
            Assert.NotEqual(PermissionKey.Wildcard, permission);
            Assert.Null(endpoint.Metadata.GetMetadata<IAllowAnonymous>());
            Assert.Equal(
                "Elsa.Workflows.Publishing.Api",
                Assert.IsType<EndpointOwnershipMetadata>(endpoint.Metadata.GetMetadata<EndpointOwnershipMetadata>()).OwnerId);
            Assert.Equal(
                EndpointAuthoringModels.MinimalApi,
                Assert.IsType<EndpointAuthoringMetadata>(endpoint.Metadata.GetMetadata<EndpointAuthoringMetadata>()).Model);

            var disposition = Assert.Single(endpoint.Metadata.GetOrderedMetadata<EndpointSecurityDispositionMetadata>());
            Assert.Equal(EndpointSecurityDispositionKind.Permission, disposition.Kind);
            var dispositionPolicy = new PermissionPolicyCodec().Parse(disposition.Value!);
            Assert.Equal(PermissionPolicyParseStatus.Valid, dispositionPolicy.Status);
            Assert.Equal([permission], dispositionPolicy.Descriptor!.Permissions);
        }
    }

    [Fact]
    public void Capability_endpoint_rejects_unauthenticated_calls_by_default()
    {
        using var serviceProvider = new ServiceCollection().AddRouting().AddElsaEndpoints().BuildServiceProvider();
        var routes = new TestEndpointRouteBuilder(serviceProvider);
        ApiCapabilitiesApi.MapApiCapabilitiesApi(routes);
        var endpoint = Assert.Single(routes.DataSources.SelectMany(source => source.Endpoints));
        var security = endpoint.Metadata.GetMetadata<EndpointSecurityDispositionMetadata>();
        Assert.NotNull(security);
        var policy = new PermissionPolicyCodec().Parse(security!.Value!);
        Assert.Equal(PermissionPolicyParseStatus.Valid, policy.Status);
        Assert.Equal(PermissionRequirementMode.Single, policy.Descriptor!.Mode);
        Assert.Equal([PermissionKey.Normalize(ApiCapabilitiesPermissions.Read)], policy.Descriptor.Permissions);
        Assert.Null(endpoint.Metadata.GetMetadata<IAllowAnonymous>());
    }

    private static RouteEndpoint Endpoint(string route, IReadOnlyList<EndpointSecurityDispositionMetadata> dispositions)
    {
        var metadata = new List<object>
        {
            new EndpointOwnershipMetadata("Elsa.Tests"),
            new EndpointAuthoringMetadata(EndpointAuthoringModels.MinimalApi),
            new HttpMethodMetadata(["GET"])
        };
        metadata.AddRange(dispositions);
        return new RouteEndpoint(
            _ => Task.CompletedTask,
            RoutePatternFactory.Parse(route),
            0,
            new EndpointMetadataCollection(metadata),
            $"Elsa.Tests:{route}");
    }

    private sealed class FixedEndpointDataSource(IReadOnlyList<Endpoint> endpoints) : EndpointDataSource
    {
        public override IReadOnlyList<Endpoint> Endpoints => endpoints;
        public override IChangeToken GetChangeToken() => new CancellationChangeToken(CancellationToken.None);
    }

    private sealed class TestEndpointRouteBuilder(IServiceProvider serviceProvider) : IEndpointRouteBuilder
    {
        public IServiceProvider ServiceProvider { get; } = serviceProvider;
        public ICollection<EndpointDataSource> DataSources { get; } = [];
        public IApplicationBuilder CreateApplicationBuilder() => new ApplicationBuilder(ServiceProvider);
    }

    private class NoopProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new InvalidOperationException("A configuration-only endpoint test must not invoke dependencies.");
    }
}
