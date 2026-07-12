using System.Reflection;
using System.Text.RegularExpressions;
using Elsa.Api.FastEndpoints.Constants;
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
    private static readonly (string Area, string RelativePath)[] CurrentManagementEndpointRoots =
    [
        ("Workflow Design", "src/Elsa/Workflows/Design/Api/Endpoints"),
        ("Activity Design", "src/Elsa/Activities/Design/Api/Endpoints"),
        ("Publishing", "src/Elsa/Workflows/Publishing/Api/Endpoints"),
        ("Runtime", "src/Elsa/Workflows/Runtime/Api/Endpoints")
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
            .SelectMany(root => EnumerateEndpointFiles(root.Area, root.RelativePath))
            .ToList();

        Assert.NotEmpty(endpointFiles);

        var violations = endpointFiles
            .Select(file => (file.DisplayPath, Source: File.ReadAllText(file.FullPath)))
            .SelectMany(file =>
            {
                var errors = new List<string>();
                if (!ConfigurePermissionsCall().IsMatch(file.Source))
                    errors.Add($"{file.DisplayPath}: missing ConfigurePermissions(...) in Configure()");
                if (AllowAnonymousCall().IsMatch(file.Source))
                    errors.Add($"{file.DisplayPath}: management endpoints must not call AllowAnonymous(...)");
                return errors;
            })
            .ToList();

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    private static IEnumerable<(string DisplayPath, string FullPath)> EnumerateEndpointFiles(string area, string relativePath)
    {
        var fullPath = Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(Directory.Exists(fullPath), $"Missing {area} endpoint directory: {relativePath}");

        var files = Directory.EnumerateFiles(fullPath, "*.cs", SearchOption.AllDirectories).ToList();
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

    [GeneratedRegex(@"^\s*ConfigurePermissions\s*\(", RegexOptions.Multiline)]
    private static partial Regex ConfigurePermissionsCall();

    [GeneratedRegex(@"^\s*AllowAnonymous\s*\(", RegexOptions.Multiline)]
    private static partial Regex AllowAnonymousCall();
}
