using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Elsa.Events.Core.Contracts;
using Xunit;

namespace Elsa.Persistence.Groundwork.DesignConformance.Tests;

public class DesignContractSuiteShapeTests
{
    private static readonly IReadOnlySet<string> AllowedAssemblyNames = new HashSet<string>(
        [
            "Elsa.Activities.Design.Core",
            "Elsa.Activities.Design.Persistence.Core",
            "Elsa.Activities.Design.Reconciliation.Core",
            "Elsa.Events.Core",
            "Elsa.Expressions.Core",
            "Elsa.Pipelines.Core",
            "Elsa.Persistence.Core",
            "Elsa.Persistence.Groundwork.DesignConformance.Tests",
            "Elsa.Primitives",
            "Elsa.Primitives.Hosting",
            "Elsa.Serialization.Core",
            "Elsa.Workflows.Design.Core",
            "Elsa.Workflows.Design.Persistence.Core",
            "Elsa.Workflows.Design.Validations.Core",
            "Microsoft.Extensions.DependencyInjection",
            "Microsoft.Extensions.DependencyInjection.Abstractions",
            "Microsoft.VisualStudio.TestPlatform.ObjectModel",
            "System.Collections",
            "System.ComponentModel",
            "System.Diagnostics.Process",
            "System.Linq",
            "System.Memory",
            "System.Private.CoreLib",
            "System.Runtime",
            "System.Security.Cryptography",
            "System.Text.Json",
            "xunit.assert",
            "xunit.core"
        ],
        StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> AllowedPackageReferences = new HashSet<string>(
        [
            "Microsoft.Extensions.DependencyInjection",
            "Microsoft.NET.Test.Sdk",
            "coverlet.collector",
            "xunit",
            "xunit.runner.visualstudio"
        ],
        StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlySet<string> AllowedResolvedPackageReferences = new HashSet<string>(
        [
            "CShells.Abstractions",
            "Elsa.Activities.Design.Core",
            "Elsa.Events.Core",
            "Elsa.Expressions.Core",
            "Elsa.Pipelines.Core",
            "Elsa.Workflows.Design.Core",
            "Elsa.Workflows.Design.Validations.Core",
            "JetBrains.Annotations",
            "Microsoft.CodeCoverage",
            "Microsoft.Extensions.Configuration",
            "Microsoft.Extensions.Configuration.Abstractions",
            "Microsoft.Extensions.DependencyInjection",
            "Microsoft.Extensions.DependencyInjection.Abstractions",
            "Microsoft.Extensions.Options",
            "Microsoft.Extensions.Primitives",
            "Microsoft.NETCore.App.Ref",
            "Microsoft.TestPlatform.ObjectModel",
            "Microsoft.TestPlatform.TestHost",
            "Newtonsoft.Json",
            "xunit.abstractions",
            "xunit.assert",
            "xunit.extensibility.core",
            "xunit.extensibility.execution"
        ],
        StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlySet<string> AllowedProjectReferences = new HashSet<string>(
        [
            "src/Elsa/Activities/Design/Core/Elsa.Activities.Design.Core.csproj",
            "src/Elsa/Activities/Design/Persistence/Core/Elsa.Activities.Design.Persistence.Core.csproj",
            "src/Elsa/Activities/Design/Reconciliation/Core/Elsa.Activities.Design.Reconciliation.Core.csproj",
            "src/Elsa/Events/Core/Elsa.Events.Core.csproj",
            "src/Elsa/Expressions/Core/Elsa.Expressions.Core.csproj",
            "src/Elsa/Persistence/Core/Elsa.Persistence.Core.csproj",
            "src/Elsa/Pipelines/Core/Elsa.Pipelines.Core.csproj",
            "src/Elsa/Primitives/Hosting/Elsa.Primitives.Hosting.csproj",
            "src/Elsa/Primitives/Primitives/Elsa.Primitives.csproj",
            "src/Elsa/Serialization/Core/Elsa.Serialization.Core.csproj",
            "src/Elsa/Workflows/Design/Core/Elsa.Workflows.Design.Core.csproj",
            "src/Elsa/Workflows/Design/Persistence/Core/Elsa.Workflows.Design.Persistence.Core.csproj",
            "src/Elsa/Workflows/Design/Validations/Core/Elsa.Workflows.Design.Validations.Core.csproj"
        ],
        StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void Shared_assembly_and_fixture_contracts_are_provider_neutral()
    {
        Assert.True(typeof(WorkflowDesignContractSuite).IsAbstract);
        Assert.True(typeof(ActivityDesignContractSuite).IsAbstract);

        var referencedAssemblies = Assembly.GetExecutingAssembly()
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name!)
            .ToArray();
        AssertAllowedReferences("assembly", referencedAssemblies, AllowedAssemblyNames);

        var fixtureTypes = new[]
        {
            typeof(IDesignPersistenceContractFixture),
            typeof(IDesignPersistenceContractFixtureFactory)
        };
        var signatureTypes = fixtureTypes
            .SelectMany(type => type.GetMethods())
            .SelectMany(method => method.GetParameters().Select(parameter => parameter.ParameterType).Append(method.ReturnType))
            .Concat(fixtureTypes.SelectMany(type => type.GetProperties()).Select(property => property.PropertyType))
            .SelectMany(ExpandType)
            .Distinct()
            .ToArray();

        AssertAllowedReferences(
            "fixture signature assembly",
            signatureTypes.Select(type => type.Assembly.GetName().Name!),
            AllowedAssemblyNames);
    }

    [Fact]
    public void Shared_project_allows_only_declared_core_projects_and_test_packages()
    {
        var unexpected = UnexpectedEvaluatedReferences(EvaluateReferenceGraph(ProjectFilePath()));

        Assert.True(
            unexpected.Length == 0,
            $"Shared design conformance MSBuild references must be provider-neutral. Unexpected: {string.Join(", ", unexpected)}.");
    }

    [Fact]
    public void Imported_provider_references_are_visible_to_the_boundary_check()
    {
        var graph = EvaluateReferenceGraph(InjectedReferenceFixturePath());
        var unexpected = UnexpectedEvaluatedReferences(graph);

        Assert.Contains("package: Groundwork.Documents", unexpected);
        Assert.Contains("project: src/Elsa/Persistence/Groundwork/Elsa.Persistence.Groundwork.csproj", unexpected);
        Assert.Contains("assembly reference: Groundwork.Documents", unexpected);
        Assert.Contains("resolved package: Groundwork.Documents", unexpected);
    }

    [Theory]
    [InlineData("Elsa.Persistence.Groundwork")]
    [InlineData("Groundwork.Documents")]
    [InlineData("Microsoft.EntityFrameworkCore")]
    [InlineData("Microsoft.Data.Sqlite")]
    public void Provider_assemblies_are_outside_the_allowlist(string assemblyName)
    {
        Assert.Contains(assemblyName, UnexpectedReferences([assemblyName], AllowedAssemblyNames));
    }

    [Theory]
    [InlineData("Groundwork.Documents")]
    [InlineData("Microsoft.EntityFrameworkCore.Sqlite")]
    public void Unused_provider_packages_are_outside_the_allowlist(string packageName)
    {
        Assert.Contains(packageName, UnexpectedReferences([packageName], AllowedPackageReferences));
    }

    [Theory]
    [InlineData("src/Elsa/Persistence/Groundwork/Elsa.Persistence.Groundwork.csproj")]
    [InlineData("src/Elsa/Persistence/EntityFrameworkCore/Elsa.Persistence.EntityFrameworkCore.csproj")]
    public void Provider_projects_are_outside_the_allowlist(string projectReference)
    {
        Assert.Contains(projectReference, UnexpectedReferences([projectReference], AllowedProjectReferences));
    }

    [Fact]
    public void Fixture_exposes_staging_and_actual_event_observation_but_no_reconciliation_shortcut()
    {
        var fixtureType = typeof(IDesignPersistenceContractFixture);

        Assert.NotNull(fixtureType.GetMethod(nameof(IDesignPersistenceContractFixture.StageActivityReconciliationCandidatesAsync)));
        Assert.Null(fixtureType.GetMethod("ReconcileActivityVersionsAsync"));

        var observation = fixtureType.GetMethod(nameof(IDesignPersistenceContractFixture.ReadObservedEventsAsync));
        Assert.NotNull(observation);
        Assert.Equal(typeof(Task<IReadOnlyList<IEvent>>), observation!.ReturnType);
    }

    private static IEnumerable<Type> ExpandType(Type type)
    {
        yield return type;

        if (type.HasElementType && type.GetElementType() is { } elementType)
        {
            foreach (var nested in ExpandType(elementType))
                yield return nested;
        }

        foreach (var argument in type.GetGenericArguments())
        {
            foreach (var nested in ExpandType(argument))
                yield return nested;
        }
    }

    private static void AssertAllowedReferences(
        string kind,
        IEnumerable<string> actual,
        IReadOnlySet<string> allowed)
    {
        var unexpected = UnexpectedReferences(actual, allowed);
        Assert.True(
            unexpected.Length == 0,
            $"Shared design conformance {kind} references must be provider-neutral. Unexpected: {string.Join(", ", unexpected)}.");
    }

    private static string[] UnexpectedReferences(
        IEnumerable<string> actual,
        IReadOnlySet<string> allowed) =>
        actual
            .Where(reference => !allowed.Contains(reference))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string[] UnexpectedEvaluatedReferences(EvaluatedReferenceGraph graph) =>
        UnexpectedReferences(
                graph.PackageReferences.Select(reference => reference.Identity),
                AllowedPackageReferences)
            .Select(reference => $"package: {reference}")
            .Concat(UnexpectedReferences(
                    graph.ProjectReferences.Select(NormalizeProjectReference),
                    AllowedProjectReferences)
                .Select(reference => $"project: {reference}"))
            .Concat(UnexpectedReferences(
                    graph.DeclaredAssemblyReferences.Select(AssemblyName),
                    AllowedAssemblyNames)
                .Select(reference => $"assembly reference: {reference}"))
            .Concat(UnexpectedReferences(
                    graph.ResolvedReferences
                        .Where(reference => !string.IsNullOrWhiteSpace(reference.NuGetPackageId))
                        .Select(reference => reference.NuGetPackageId!),
                    AllowedResolvedPackageReferences)
                .Select(reference => $"resolved package: {reference}"))
            .Concat(UnexpectedReferences(
                    graph.ResolvedReferences
                        .Where(reference => string.IsNullOrWhiteSpace(reference.NuGetPackageId))
                        .Select(AssemblyName),
                    AllowedAssemblyNames)
                .Select(reference => $"resolved assembly: {reference}"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static EvaluatedReferenceGraph EvaluateReferenceGraph(string projectFilePath)
    {
        var declared = EvaluateMsBuildItems(
            projectFilePath,
            target: null,
            "PackageReference",
            "ProjectReference",
            "Reference");
        var resolved = EvaluateMsBuildItems(
            projectFilePath,
            "ResolveReferences",
            "PackageReference",
            "ProjectReference",
            "ReferencePath");

        return new EvaluatedReferenceGraph(
            declared.Items("PackageReference").Concat(resolved.Items("PackageReference")).ToArray(),
            declared.Items("ProjectReference").Concat(resolved.Items("ProjectReference")).ToArray(),
            declared.Items("Reference"),
            resolved.Items("ReferencePath"));
    }

    private static EvaluatedItems EvaluateMsBuildItems(
        string projectFilePath,
        string? target,
        params string[] itemNames)
    {
        var repositoryRoot = RepositoryRoot();
        var dotnetHostPath = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        var startInfo = new ProcessStartInfo
        {
            FileName = string.IsNullOrWhiteSpace(dotnetHostPath) ? "dotnet" : dotnetHostPath,
            WorkingDirectory = repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("msbuild");
        startInfo.ArgumentList.Add(projectFilePath);
        if (target is not null)
            startInfo.ArgumentList.Add($"-target:{target}");
        startInfo.ArgumentList.Add($"-getItem:{string.Join(',', itemNames)}");
        startInfo.ArgumentList.Add($"-property:Configuration={BuildConfiguration()}");
        startInfo.ArgumentList.Add("-property:BuildProjectReferences=false");
        startInfo.ArgumentList.Add($"-property:DesignConformanceRepositoryRoot={repositoryRoot}");
        startInfo.ArgumentList.Add("-nologo");
        startInfo.ArgumentList.Add("-nodeReuse:false");
        startInfo.ArgumentList.Add("-verbosity:quiet");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start dotnet msbuild for the design conformance reference audit.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit(milliseconds: 30_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"MSBuild reference evaluation timed out for '{projectFilePath}'.");
        }

        var output = standardOutput.GetAwaiter().GetResult();
        var error = standardError.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"MSBuild reference evaluation failed for '{projectFilePath}' with exit code {process.ExitCode}.{Environment.NewLine}{error}{output}");
        }

        return EvaluatedItems.Parse(output, projectFilePath);
    }

    private static string NormalizeProjectReference(MsBuildReference reference)
    {
        var path = reference.FullPath ?? reference.Identity;
        var fullPath = Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(path, Path.GetDirectoryName(ProjectFilePath())!);
        return Path.GetRelativePath(RepositoryRoot(), fullPath).Replace('\\', '/');
    }

    private static string AssemblyName(MsBuildReference reference)
    {
        var identity = reference.Identity.Split(',', 2)[0].Trim();
        var fileName = Path.GetFileName(identity);
        return fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileNameWithoutExtension(fileName)
            : fileName;
    }

    private static string ProjectFilePath() =>
        Path.Combine(
            RepositoryRoot(),
            "tests",
            "Elsa",
            "Persistence",
            "Groundwork",
            "DesignConformance",
            "Tests",
            "Elsa.Persistence.Groundwork.DesignConformance.Tests.csproj");

    private static string InjectedReferenceFixturePath() =>
        Path.Combine(
            RepositoryRoot(),
            "tests",
            "Elsa",
            "Persistence",
            "Groundwork",
            "DesignConformance",
            "Tests",
            "TestAssets",
            "EvaluatedReferences",
            "InjectedProviderReferences.proj");

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Elsa.Server.slnx")))
                return directory.FullName;
        }

        throw new InvalidOperationException(
            $"Could not find Elsa.Server.slnx above '{AppContext.BaseDirectory}'.");
    }

    private static string BuildConfiguration() =>
        Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyConfigurationAttribute>()?
            .Configuration
        ?? throw new InvalidOperationException("The design conformance test assembly has no build configuration.");

    private sealed record EvaluatedReferenceGraph(
        IReadOnlyList<MsBuildReference> PackageReferences,
        IReadOnlyList<MsBuildReference> ProjectReferences,
        IReadOnlyList<MsBuildReference> DeclaredAssemblyReferences,
        IReadOnlyList<MsBuildReference> ResolvedReferences);

    private sealed record MsBuildReference(
        string Identity,
        string? FullPath,
        string? NuGetPackageId);

    private sealed class EvaluatedItems(IReadOnlyDictionary<string, IReadOnlyList<MsBuildReference>> items)
    {
        public IReadOnlyList<MsBuildReference> Items(string itemName) =>
            items.TryGetValue(itemName, out var values) ? values : [];

        public static EvaluatedItems Parse(string json, string projectFilePath)
        {
            try
            {
                using var document = JsonDocument.Parse(json);
                var parsed = document.RootElement
                    .GetProperty("Items")
                    .EnumerateObject()
                    .ToDictionary(
                        item => item.Name,
                        item => (IReadOnlyList<MsBuildReference>)item.Value
                            .EnumerateArray()
                            .Select(reference => new MsBuildReference(
                                reference.GetProperty("Identity").GetString()!,
                                OptionalString(reference, "FullPath"),
                                OptionalString(reference, "NuGetPackageId")))
                            .ToArray(),
                        StringComparer.Ordinal);
                return new EvaluatedItems(parsed);
            }
            catch (JsonException exception)
            {
                throw new InvalidOperationException(
                    $"MSBuild returned invalid reference JSON for '{projectFilePath}'.{Environment.NewLine}{json}",
                    exception);
            }
        }

        private static string? OptionalString(JsonElement element, string propertyName) =>
            element.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(property.GetString())
                ? property.GetString()
                : null;
    }
}
