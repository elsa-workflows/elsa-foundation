using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Elsa.Api.Capabilities;
using Elsa.Api.FastEndpoints.Constants;
using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Architecture.Tests;

/// <summary>
/// Pins the stable management-client permission vocabulary and guards every endpoint source file in the
/// management domain API slices that exist today. Endpoint-specific tests still inspect the configured
/// FastEndpoints definitions; this architecture sweep prevents a newly added endpoint file from escaping
/// those narrower inline inventories before the endpoint-specific tests are updated.
/// </summary>
public sealed partial class EndpointSecurityTests
{
    private static readonly (string Area, string RelativePath, string[] Permissions)[] CurrentManagementEndpointRoots =
    [
        ("Workflow Design", "src/Elsa/Workflows/Design/Api/Endpoints", [nameof(PermissionNames.WorkflowDesignRead), nameof(PermissionNames.WorkflowDesignManage)]),
        ("Activity Design", "src/Elsa/Activities/Design/Api/Endpoints", [nameof(PermissionNames.ActivityDesignRead), nameof(PermissionNames.ActivityDesignManage)]),
        ("Expressions", "src/Elsa/Expressions/Api/Endpoints", [nameof(PermissionNames.ExpressionsRead)]),
        ("Publishing", "src/Elsa/Workflows/Publishing/Api/Endpoints", [nameof(PermissionNames.WorkflowPublishingRead), nameof(PermissionNames.WorkflowPublishingManage)]),
        ("Runtime", "src/Elsa/Workflows/Runtime/Api/Endpoints", [nameof(PermissionNames.WorkflowRuntimeRead), nameof(PermissionNames.WorkflowRuntimeExecute), nameof(PermissionNames.WorkflowRuntimeManage)]),
        ("API Capabilities", "src/Elsa/Api/Capabilities/Endpoints", [nameof(PermissionNames.ApiCapabilitiesRead)])
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
            [nameof(PermissionNames.ApiCapabilitiesRead)] = "api-capabilities.read"
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
    public void Every_current_management_domain_endpoint_configures_permissions_and_is_not_anonymous()
    {
        var endpointFiles = CurrentManagementEndpointRoots
            .SelectMany(root => EnumerateEndpointFiles(root.Area, root.RelativePath)
                .Select(file => (file.DisplayPath, file.FullPath, root.Permissions)))
            .ToList();

        Assert.NotEmpty(endpointFiles);

        var violations = endpointFiles
            .Select(file => (file.DisplayPath, Source: File.ReadAllText(file.FullPath), file.Permissions))
            .SelectMany(file =>
            {
                var errors = new List<string>();
                if (!ConfigurePermissionsCall().IsMatch(file.Source))
                    errors.Add($"{file.DisplayPath}: missing ConfigurePermissions(...) in Configure()");
                if (!file.Permissions.Any(permission => file.Source.Contains($"PermissionNames.{permission}", StringComparison.Ordinal)))
                    errors.Add($"{file.DisplayPath}: missing a canonical action-scoped permission for its owning domain");
                if (AllowAnonymousCall().IsMatch(file.Source))
                    errors.Add($"{file.DisplayPath}: management endpoints must not call AllowAnonymous(...)");
                return errors;
            })
            .ToList();

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Capability_endpoint_rejects_unauthenticated_calls_by_default()
    {
        var endpointType = typeof(ApiCapabilitiesFeature).Assembly.GetType(
            "Elsa.Api.Capabilities.Endpoints.GetCapabilities",
            throwOnError: true)!;
        var dependencies = endpointType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Single().GetParameters()
            .Select(parameter => parameter.ParameterType.IsInterface
                ? DispatchProxy.Create(parameter.ParameterType, typeof(NoopProxy))
                : RuntimeHelpers.GetUninitializedObject(parameter.ParameterType))
            .ToArray();
        var create = typeof(Factory).GetMethods()
            .Single(method => method.Name == nameof(Factory.Create) && method.IsGenericMethodDefinition &&
                              method.GetParameters() is [var first, var rest] &&
                              first.ParameterType == typeof(DefaultHttpContext) &&
                              rest.ParameterType == typeof(object[]))
            .MakeGenericMethod(endpointType);

        // Factory.Create otherwise falls back to FastEndpoints' process-global service resolver. The
        // representative management host in this assembly can replace and dispose that resolver concurrently.
        // Supplying request services keeps this configuration-only assertion isolated from the host lifecycle.
        using var serviceProvider = new ServiceCollection().AddServicesForUnitTesting().BuildServiceProvider();
        var httpContext = new DefaultHttpContext { RequestServices = serviceProvider };
        var endpoint = (BaseEndpoint)create.Invoke(null, [httpContext, dependencies])!;
        endpoint.Configure();

        Assert.Contains(PermissionNames.ApiCapabilitiesRead, endpoint.Definition.AllowedPermissions!);
        Assert.Contains(PermissionNames.All, endpoint.Definition.AllowedPermissions!);
        Assert.Null(endpoint.Definition.AnonymousVerbs);
    }

    private static IEnumerable<(string DisplayPath, string FullPath)> EnumerateEndpointFiles(string area, string relativePath)
    {
        var fullPath = Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(Directory.Exists(fullPath), $"Missing {area} endpoint directory: {relativePath}");

        var files = Directory.EnumerateFiles(fullPath, "*.cs", SearchOption.AllDirectories)
            .Where(file => File.ReadAllText(file).Contains("override void Configure", StringComparison.Ordinal))
            .ToList();
        Assert.True(files.Count > 0, $"No {area} endpoint sources found under {relativePath}");
        return files.Select(file => ($"{area}: {Path.GetRelativePath(RepoRoot, file).Replace(Path.DirectorySeparatorChar, '/')}", file));
    }

    private static string RepoRoot { get; } = FindRepoRoot();

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Elsa.Server.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate the repository root.");
    }

    [GeneratedRegex(@"^\s*ConfigurePermissions\s*\(\s*PermissionNames\.[A-Za-z]+", RegexOptions.Multiline)]
    private static partial Regex ConfigurePermissionsCall();

    [GeneratedRegex(@"^\s*AllowAnonymous\s*\(", RegexOptions.Multiline)]
    private static partial Regex AllowAnonymousCall();

    private class NoopProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new InvalidOperationException("A configuration-only endpoint test must not invoke dependencies.");
    }
}
