using System.Reflection;
using System.Runtime.CompilerServices;
using Elsa.Activities.Bpmn.Interchange;
using Elsa.Activities.Design.Api;
using Elsa.Api.AspNetCore;
using Elsa.Api.Capabilities;
using Elsa.Api.Compatibility.Testing.Manifests;
using Elsa.Api.Compatibility.Testing.Security;
using Elsa.Api.FastEndpoints.Constants;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Expressions.Api;
using Elsa.Workflows.Design.Api;
using Elsa.Workflows.Publishing.Api;
using Elsa.Workflows.Runtime.Api;
using Elsa3.Activities.Design.Import;
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Elsa.Architecture.Tests;

/// <summary>
/// Pins the stable management-client permission vocabulary and guards every endpoint source file in the
/// management domain API slices that exist today. Endpoint-specific tests still inspect the configured
/// FastEndpoints definitions; this architecture sweep prevents a newly added endpoint file from escaping
/// those narrower inline inventories before the endpoint-specific tests are updated.
/// </summary>
public sealed class EndpointSecurityTests
{
    private static readonly (string Area, string RelativePath)[] CurrentManagementEndpointRoots =
    [
        ("Workflow Design", "src/Elsa/Workflows/Design/Api/Endpoints"),
        ("Activity Design", "src/Elsa/Activities/Design/Api/Endpoints"),
        ("Publishing", "src/Elsa/Workflows/Publishing/Api/Endpoints"),
        ("Runtime", "src/Elsa/Workflows/Runtime/Api/Endpoints"),
        ("Elsa 3 Import", "src/Elsa3/Activities/Design/Import/Endpoints"),
        ("BPMN Interchange", "src/Elsa/Activities/Bpmn/Interchange/Endpoints")
    ];

    [Fact]
    public void Management_permission_names_are_stable_unique_and_action_scoped()
    {
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [nameof(PermissionNames.WorkflowDesignRead)] = "workflow-design.read",
            [nameof(PermissionNames.WorkflowDesignManage)] = "workflow-design.manage",
            [nameof(PermissionNames.ActivityDesignRead)] = "activity-design.read",
            [nameof(PermissionNames.ActivityDesignManage)] = "activity-design.manage",
            [nameof(PermissionNames.ExpressionsRead)] = "expressions.read",
            [nameof(PermissionNames.WorkflowPublishingRead)] = "workflow-publishing.read",
            [nameof(PermissionNames.WorkflowPublishingManage)] = "workflow-publishing.manage",
            [nameof(PermissionNames.WorkflowRuntimeRead)] = "workflow-runtime.read",
            [nameof(PermissionNames.WorkflowRuntimeExecute)] = "workflow-runtime.execute",
            [nameof(PermissionNames.WorkflowRuntimeManage)] = "workflow-runtime.manage",
            [nameof(PermissionNames.ApiCapabilitiesRead)] = "api-capabilities.read",
            [nameof(PermissionNames.Elsa3ImportRead)] = "elsa3-import.read",
            [nameof(PermissionNames.Elsa3ImportManage)] = "elsa3-import.manage",
            [nameof(PermissionNames.BpmnInterchangeRead)] = "bpmn-interchange.read",
            [nameof(PermissionNames.BpmnInterchangeManage)] = "bpmn-interchange.manage"
        };
        var actual = typeof(PermissionNames)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.Name != nameof(PermissionNames.All))
            .ToDictionary(field => field.Name, field => Assert.IsType<string>(field.GetRawConstantValue()), StringComparer.Ordinal);

        Assert.Equal(expected.OrderBy(x => x.Key), actual.OrderBy(x => x.Key));
        Assert.Equal(actual.Count, actual.Values.Distinct(StringComparer.Ordinal).Count());
        Assert.All(actual.Values, value => Assert.Matches("^[a-z][a-z0-9-]*\\.(read|manage|execute)$", value));
        Assert.NotEqual(PermissionNames.ApiCapabilitiesRead, PermissionNames.WorkflowDesignRead);
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
    public void Every_current_management_FastEndpoints_type_declares_an_owned_permission_and_not_anonymous()
    {
        var canonicalPermissions = typeof(PermissionNames)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.Name != nameof(PermissionNames.All))
            .Select(field => field.Name)
            .ToHashSet(StringComparer.Ordinal);
        var violations = new List<string>();
        var endpointCount = 0;
        foreach (var root in CurrentManagementEndpointRoots)
        {
            var relativePath = root.RelativePath.Replace('/', Path.DirectorySeparatorChar);
            Assert.False(Path.IsPathRooted(relativePath), $"Endpoint root must be relative: {root.RelativePath}");
            var directory = Path.Join(RepoRoot, relativePath);
            Assert.True(Directory.Exists(directory), $"Missing {root.Area} endpoint directory: {root.RelativePath}");
            foreach (var path in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                var syntax = CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path).GetCompilationUnitRoot();
                foreach (var declaration in syntax.DescendantNodes().OfType<ClassDeclarationSyntax>())
                {
                    var configure = declaration.Members.OfType<MethodDeclarationSyntax>()
                        .SingleOrDefault(method => method.Identifier.ValueText == "Configure");
                    if (configure is null)
                        continue;

                    endpointCount++;
                    var calls = configure.DescendantNodes().OfType<InvocationExpressionSyntax>().ToArray();
                    var permissionCalls = calls.Where(call => InvocationName(call) == "ConfigurePermissions").ToArray();
                    var display = $"{root.Area}: {Path.GetRelativePath(RepoRoot, path).Replace(Path.DirectorySeparatorChar, '/')}:{declaration.Identifier.ValueText}";
                    if (permissionCalls.Length != 1)
                    {
                        violations.Add($"{display}: expected exactly one ConfigurePermissions(...) call, found {permissionCalls.Length}");
                        continue;
                    }

                    var declaredPermissions = permissionCalls[0].ArgumentList.Arguments
                        .Select(argument => argument.Expression)
                        .OfType<MemberAccessExpressionSyntax>()
                        .Where(member => member.Expression.ToString() == nameof(PermissionNames))
                        .Select(member => member.Name.Identifier.ValueText)
                        .ToHashSet(StringComparer.Ordinal);
                    if (declaredPermissions.Count == 0 || !declaredPermissions.IsSubsetOf(canonicalPermissions))
                        violations.Add($"{display}: missing a canonical action-scoped permission from the active catalog");
                    if (calls.Any(call => InvocationName(call) == "AllowAnonymous"))
                        violations.Add($"{display}: management endpoints must not call AllowAnonymous(...)");
                }
            }
        }

        Assert.True(endpointCount > 0, "No management FastEndpoints Configure methods were discovered.");
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
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
        using var provider = services.BuildServiceProvider();
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
    public void Secrets_minimal_api_declares_one_owned_secure_route_per_operation_and_catalog_owner()
    {
        var apiRoot = Path.Join(RepoRoot, "src", "Elsa", "Secrets", "Api");
        var mapperPath = Path.Join(apiRoot, "SecretsApi.cs");
        var contributorPath = Path.Join(apiRoot, "Authorization", "SecretsPermissionContributor.cs");
        Assert.True(File.Exists(mapperPath), "The migrated Secrets API must expose its module-owned mapper.");
        Assert.True(File.Exists(contributorPath), "The migrated Secrets API must expose its permission contributor.");

        var mapper = File.ReadAllText(mapperPath);
        var contributor = File.ReadAllText(contributorPath);
        var syntax = CSharpSyntaxTree.ParseText(mapper, path: mapperPath).GetCompilationUnitRoot();
        var calls = syntax.DescendantNodes().OfType<InvocationExpressionSyntax>().ToArray();
        var routeMappings = calls.Count(call =>
            call.Expression is MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax endpoint } &&
            endpoint.Identifier.ValueText == "endpoints" &&
            InvocationName(call) is "MapGet" or "MapPost" or "MapPut" or "MapDelete");
        var permissionPolicies = calls.Count(call => InvocationName(call) == "RequireAnyPermission");

        Assert.Equal(10, routeMappings);
        Assert.Equal(10, permissionPolicies);
        Assert.Contains("Elsa.Secrets.Api", mapper, StringComparison.Ordinal);
        Assert.Contains("EndpointAuthoringModels.MinimalApi", mapper, StringComparison.Ordinal);
        Assert.Contains("WithOwner", mapper, StringComparison.Ordinal);

        foreach (var permission in new[] { "Read", "Write", "UpdateValue", "Delete", "Test", "Use", "Import", "Export" })
            Assert.Contains($"SecretsPermissions.{permission}", contributor, StringComparison.Ordinal);

        Assert.Contains("new HashSet<string>(StringComparer.Ordinal) { SecretsPermissions.Read }", contributor, StringComparison.Ordinal);
        Assert.DoesNotContain("PermissionKey.Wildcard", contributor, StringComparison.Ordinal);
    }

    [Fact]
    public void Structured_logs_minimal_api_declares_three_owned_secure_routes_and_one_catalog_permission()
    {
        var apiRoot = Path.Join(RepoRoot, "src", "Elsa", "Diagnostics", "StructuredLogs");
        var mapperPath = Path.Join(apiRoot, "Endpoints", "StructuredLogsApi.cs");
        var contributorPath = Path.Join(apiRoot, "Authorization", "StructuredLogsPermissionContributor.cs");
        var permissionsPath = Path.Join(apiRoot, "Authorization", "StructuredLogsPermissions.cs");
        Assert.True(File.Exists(mapperPath), "Structured Logs must expose its module-owned mapper.");
        Assert.True(File.Exists(contributorPath), "Structured Logs must expose its permission contributor.");
        Assert.True(File.Exists(permissionsPath), "Structured Logs must expose its stable permission vocabulary.");

        var mapper = File.ReadAllText(mapperPath);
        var contributor = File.ReadAllText(contributorPath);
        var permissions = File.ReadAllText(permissionsPath);
        var syntax = CSharpSyntaxTree.ParseText(mapper, path: mapperPath).GetCompilationUnitRoot();
        var calls = syntax.DescendantNodes().OfType<InvocationExpressionSyntax>().ToArray();
        var routeMappings = calls.Count(call =>
            call.Expression is MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax endpoint } &&
            endpoint.Identifier.ValueText == "endpoints" &&
            InvocationName(call) == "MapGet");

        Assert.Equal(3, routeMappings);
        Assert.Equal(3, calls.Count(call => InvocationName(call) == "RequireAnyPermission"));
        Assert.Equal(3, calls.Count(call => InvocationName(call) == "WithOwner"));
        Assert.Equal(3, calls.Count(call => InvocationName(call) == "WithAuthoringModel"));
        Assert.Contains("EndpointAuthoringModels.MinimalApi", mapper, StringComparison.Ordinal);
        Assert.Contains("PermissionKey.Wildcard", mapper, StringComparison.Ordinal);
        Assert.Contains("Diagnostics:StructuredLogs", permissions, StringComparison.Ordinal);
        Assert.Contains("StructuredLogsPermissions.Read", contributor, StringComparison.Ordinal);
        Assert.DoesNotContain("PermissionKey.Wildcard", contributor, StringComparison.Ordinal);
    }

    [Fact]
    public void Capability_endpoint_rejects_unauthenticated_calls_by_default()
    {
        using var serviceProvider = new ServiceCollection().AddRouting().BuildServiceProvider();
        var routes = new TestEndpointRouteBuilder(serviceProvider);
        ApiCapabilitiesApi.MapApiCapabilitiesApi(routes);
        var endpoint = Assert.Single(routes.DataSources.SelectMany(source => source.Endpoints));
        var security = endpoint.Metadata.GetMetadata<EndpointSecurityDispositionMetadata>();
        Assert.NotNull(security);
        var policy = new PermissionPolicyCodec().Parse(security!.Value!);
        Assert.Equal(PermissionPolicyParseStatus.Valid, policy.Status);
        Assert.Equal(PermissionRequirementMode.Single, policy.Descriptor!.Mode);
        Assert.Equal([PermissionKey.Normalize(PermissionNames.ApiCapabilitiesRead)], policy.Descriptor.Permissions);
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

    private static string InvocationName(InvocationExpressionSyntax invocation) => invocation.Expression switch
    {
        IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
        MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
        _ => string.Empty
    };

    private static string RepoRoot { get; } = FindRepoRoot();

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Elsa.Server.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate the repository root.");
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
