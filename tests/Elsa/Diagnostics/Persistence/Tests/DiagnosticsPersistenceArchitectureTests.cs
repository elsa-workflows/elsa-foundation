using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Elsa.Diagnostics.OpenTelemetry.Core.Contracts;
using Elsa.Diagnostics.Persistence.Extensions;
using Elsa.Diagnostics.Persistence.Observability;
using Elsa.Diagnostics.StructuredLogs.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Diagnostics.Persistence.Tests;

public sealed partial class DiagnosticsPersistenceArchitectureTests
{
    private const string CurrentGroundworkVersion = "0.4.0-preview.3";
    private static string RepoRoot { get; } = FindRepoRoot();
    private static readonly string DiagnosticsSourceRoot = Path.Combine(RepoRoot, "src", "Elsa", "Diagnostics");
    private static readonly string DiagnosticsTestRoot = Path.Combine(RepoRoot, "tests", "Elsa", "Diagnostics");

    [Fact]
    public void Provider_neutral_diagnostics_projects_are_Groundwork_free_in_the_project_graph_and_source_tree()
    {
        var violations = FindDiagnosticsSourceProjects()
            .Where(project => !IsGroundworkAdapterProject(project))
            .SelectMany(project => FindGroundworkProjectGraphViolations(project).Select(violation =>
                $"{RelativePath(project)} -> {violation}"))
            .Concat(FindGroundworkSourceViolations())
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "Groundwork is permitted only in concrete diagnostics persistence adapters. Provider-neutral diagnostics projects and source must remain infrastructure-free:" +
            Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Diagnostics_core_and_shared_lifecycle_have_no_Groundwork_references()
    {
        var assemblies = new[]
        {
            typeof(IStructuredLogStore).Assembly,
            typeof(IOpenTelemetryStore).Assembly,
            typeof(DiagnosticsPersistenceRegistration).Assembly
        };

        foreach (var assembly in assemblies)
        {
            Assert.DoesNotContain(assembly.GetReferencedAssemblies(), reference => reference.Name?.StartsWith("Groundwork", StringComparison.Ordinal) == true);

            var publicSurfaceViolations = PublicSurfaceTypes(assembly)
                .Where(reference => IsGroundworkType(reference.Type))
                .Select(reference => $"{assembly.GetName().Name}: {reference.Owner.FullName}.{reference.Member} -> {reference.Type}")
                .ToArray();
            Assert.True(
                publicSurfaceViolations.Length == 0,
                "Diagnostics core and shared lifecycle public contracts must not expose Groundwork types:" +
                Environment.NewLine + string.Join(Environment.NewLine, publicSurfaceViolations));
        }
    }

    [Fact]
    public void Current_groundwork_family_and_ef_oracle_ledger_are_closeout_ready()
    {
        var packageVersions = XDocument.Load(Path.Combine(RepoRoot, "Directory.Packages.props"))
            .Descendants("PackageVersion")
            .Where(element => element.Attribute("Include")?.Value.StartsWith("Groundwork", StringComparison.Ordinal) == true)
            .ToDictionary(
                element => element.Attribute("Include")!.Value,
                element => element.Attribute("Version")?.Value,
                StringComparer.Ordinal);

        Assert.NotEmpty(packageVersions);
        Assert.All(packageVersions, package => Assert.Equal(CurrentGroundworkVersion, package.Value));

        var explicitProjectVersions = FindDiagnosticsProjects()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .SelectMany(project =>
            {
                var document = XDocument.Load(project);
                var localVersion = document.Descendants("GroundworkVersion")
                    .Select(element => element.Value)
                    .FirstOrDefault();
                return document.Descendants("PackageReference")
                    .Where(element => element.Attribute("Include")?.Value.StartsWith("Groundwork", StringComparison.Ordinal) == true)
                    .Select(element =>
                    {
                        var version = element.Attribute("Version")?.Value ?? element.Attribute("VersionOverride")?.Value;
                        return (Path: RelativePath(project), Package: element.Attribute("Include")!.Value,
                            Version: version == "$(GroundworkVersion)" ? localVersion : version);
                    });
            })
            .Where(reference => reference.Version is not null)
            .ToArray();

        Assert.NotEmpty(explicitProjectVersions);
        Assert.All(explicitProjectVersions, reference =>
            Assert.Equal(CurrentGroundworkVersion, reference.Version));

        var ledger = File.ReadAllLines(Path.Combine(
            RepoRoot, "specs", "139-groundwork-diagnostics-persistence", "ef-test-removal-ledger.md"));
        var factRows = ledger
            .Where(line => line.StartsWith("| `", StringComparison.Ordinal) || line.StartsWith("| .", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(46, factRows.Length);
        Assert.Equal(43, factRows.Count(line => line.Contains("covered", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(3, factRows.Count(line => line.Contains(
            "Retired at the Groundwork boundary", StringComparison.Ordinal)));
        Assert.All(factRows, line => Assert.True(
            line.Contains("covered", StringComparison.OrdinalIgnoreCase) ^
            line.Contains("Retired at the Groundwork boundary", StringComparison.Ordinal),
            $"Ledger row must have exactly one closeout disposition: {line}"));
        var currentTestSources = Directory.EnumerateFiles(DiagnosticsTestRoot, "*.cs", SearchOption.AllDirectories)
            .Select(File.ReadAllText)
            .ToArray();
        foreach (var row in factRows.Where(line => line.Contains("covered", StringComparison.OrdinalIgnoreCase)))
        {
            var evidenceColumn = row.Split('|')[3];
            var evidence = LedgerEvidencePattern().Matches(evidenceColumn)
                .Select(match => (Class: match.Groups["class"].Value, Method: match.Groups["method"].Value))
                .ToArray();
            Assert.NotEmpty(evidence);
            Assert.All(evidence, reference => Assert.Contains(currentTestSources, source =>
                Regex.IsMatch(source, $@"\bclass\s+{Regex.Escape(reference.Class)}\b", RegexOptions.CultureInvariant) &&
                Regex.IsMatch(source, $@"\b{Regex.Escape(reference.Method)}\s*\(", RegexOptions.CultureInvariant)));
        }
        var ledgerText = string.Join(Environment.NewLine, ledger);
        Assert.Contains($"**Groundwork baseline:** exact `{CurrentGroundworkVersion}`", ledgerText, StringComparison.Ordinal);
        Assert.Contains("Disposition: 43 covered; 3 EF-mechanism-only facts retired at the Groundwork boundary", ledgerText, StringComparison.Ordinal);
        Assert.DoesNotContain("one remaining OpenTelemetry test", ledgerText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Replacement_helper_leaves_exactly_one_store_and_one_shared_instance()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITestStore, FirstStore>();
        services.AddSingleton<ITestStore, SecondStore>();

        services.ReplaceDiagnosticsStore<ITestStore, ReplacementStore>();

        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(ITestStore));
        using var provider = services.BuildServiceProvider();
        Assert.Same(provider.GetRequiredService<ITestStore>(), provider.GetRequiredService<ReplacementStore>());
    }

    [Fact]
    public void Two_explicit_store_replacements_are_rejected_instead_of_last_write_wins()
    {
        var services = new ServiceCollection();
        services.ReplaceDiagnosticsStore<ITestStore, FirstStore>();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.ReplaceDiagnosticsStore<ITestStore, SecondStore>());

        Assert.Contains(typeof(ITestStore).FullName!, exception.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(FirstStore).FullName!, exception.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(SecondStore).FullName!, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Default_and_explicit_registration_have_order_independent_selection_semantics()
    {
        var defaultThenExplicit = new ServiceCollection();
        defaultThenExplicit.AddDefaultDiagnosticsStore<ITestStore, FirstStore>();
        defaultThenExplicit.ReplaceDiagnosticsStore<ITestStore, ReplacementStore>();
        using var firstProvider = defaultThenExplicit.BuildServiceProvider();

        var explicitThenDefault = new ServiceCollection();
        explicitThenDefault.ReplaceDiagnosticsStore<ITestStore, ReplacementStore>();
        explicitThenDefault.AddDefaultDiagnosticsStore<ITestStore, FirstStore>();
        using var secondProvider = explicitThenDefault.BuildServiceProvider();

        Assert.IsType<ReplacementStore>(firstProvider.GetRequiredService<ITestStore>());
        Assert.IsType<ReplacementStore>(secondProvider.GetRequiredService<ITestStore>());
        Assert.Single(defaultThenExplicit, descriptor => descriptor.ServiceType == typeof(ITestStore));
        Assert.Single(explicitThenDefault, descriptor => descriptor.ServiceType == typeof(ITestStore));
    }

    [Fact]
    public void Repeated_default_registration_and_preexisting_contract_remain_non_overriding()
    {
        var repeatedDefault = new ServiceCollection();
        repeatedDefault.AddDefaultDiagnosticsStore<ITestStore, FirstStore>();
        repeatedDefault.AddDefaultDiagnosticsStore<ITestStore, SecondStore>();
        using var firstProvider = repeatedDefault.BuildServiceProvider();

        var preexisting = new ServiceCollection();
        preexisting.AddSingleton<ITestStore, ReplacementStore>();
        preexisting.AddDefaultDiagnosticsStore<ITestStore, FirstStore>();
        using var secondProvider = preexisting.BuildServiceProvider();

        Assert.IsType<FirstStore>(firstProvider.GetRequiredService<ITestStore>());
        Assert.IsType<ReplacementStore>(secondProvider.GetRequiredService<ITestStore>());
        Assert.Single(repeatedDefault, descriptor => descriptor.ServiceType == typeof(ITestStore));
        Assert.Single(preexisting, descriptor => descriptor.ServiceType == typeof(ITestStore));
    }

    [Fact]
    public void Diagnostics_stores_default_to_scoped_and_share_one_instance_within_each_scope()
    {
        var services = new ServiceCollection();
        services.AddDefaultDiagnosticsStore<ITestStore, FirstStore>();

        Assert.Equal(ServiceLifetime.Scoped,
            Assert.Single(services, descriptor => descriptor.ServiceType == typeof(ITestStore)).Lifetime);
        Assert.Equal(ServiceLifetime.Scoped,
            Assert.Single(services, descriptor => descriptor.ServiceType == typeof(FirstStore)).Lifetime);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();
        Assert.Same(
            firstScope.ServiceProvider.GetRequiredService<ITestStore>(),
            firstScope.ServiceProvider.GetRequiredService<FirstStore>());
        Assert.NotSame(
            firstScope.ServiceProvider.GetRequiredService<ITestStore>(),
            secondScope.ServiceProvider.GetRequiredService<ITestStore>());
    }

    [Theory]
    [InlineData(nameof(DiagnosticsPersistenceRegistration.AddDefaultDiagnosticsStore))]
    [InlineData(nameof(DiagnosticsPersistenceRegistration.ReplaceDiagnosticsStore))]
    public void Store_registration_allows_an_explicit_documented_lifetime(string methodName)
    {
        var method = typeof(DiagnosticsPersistenceRegistration)
            .GetMethods()
            .Single(candidate => candidate.Name == methodName);

        Assert.Contains(method.GetParameters(), parameter => parameter.ParameterType == typeof(ServiceLifetime));
    }

    [Fact]
    public void Explicit_singleton_store_lifetime_is_preserved_for_default_and_replacement_registrations()
    {
        var defaultServices = new ServiceCollection();
        defaultServices.AddDefaultDiagnosticsStore<ITestStore, FirstStore>(ServiceLifetime.Singleton);
        var replacementServices = new ServiceCollection();
        replacementServices.ReplaceDiagnosticsStore<ITestStore, ReplacementStore>(ServiceLifetime.Singleton);

        Assert.All(
            defaultServices.Where(descriptor =>
                descriptor.ServiceType == typeof(ITestStore) || descriptor.ServiceType == typeof(FirstStore)),
            descriptor => Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime));
        Assert.All(
            replacementServices.Where(descriptor =>
                descriptor.ServiceType == typeof(ITestStore) || descriptor.ServiceType == typeof(ReplacementStore)),
            descriptor => Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime));
    }

    [Fact]
    public void Invalid_store_lifetime_is_rejected_by_default_and_replacement_registration()
    {
        var invalid = (ServiceLifetime)int.MaxValue;

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ServiceCollection().AddDefaultDiagnosticsStore<ITestStore, FirstStore>(invalid));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ServiceCollection().ReplaceDiagnosticsStore<ITestStore, FirstStore>(invalid));
    }

    [Fact]
    public void Observer_replacement_is_order_independent_and_rejects_a_second_explicit_selection()
    {
        var defaultThenExplicit = new ServiceCollection();
        defaultThenExplicit.AddDiagnosticsPersistenceObservability();
        defaultThenExplicit.ReplaceDiagnosticsStore<IDiagnosticsPersistenceObserver, FirstObserver>(ServiceLifetime.Singleton);
        Assert.Throws<InvalidOperationException>(() =>
            defaultThenExplicit.ReplaceDiagnosticsStore<IDiagnosticsPersistenceObserver, SecondObserver>(ServiceLifetime.Singleton));

        var explicitThenDefault = new ServiceCollection();
        explicitThenDefault.ReplaceDiagnosticsStore<IDiagnosticsPersistenceObserver, FirstObserver>(ServiceLifetime.Singleton);
        explicitThenDefault.AddDiagnosticsPersistenceObservability();
        Assert.Throws<InvalidOperationException>(() =>
            explicitThenDefault.ReplaceDiagnosticsStore<IDiagnosticsPersistenceObserver, SecondObserver>(ServiceLifetime.Singleton));

        using var firstProvider = defaultThenExplicit.BuildServiceProvider();
        using var secondProvider = explicitThenDefault.BuildServiceProvider();
        Assert.IsType<FirstObserver>(firstProvider.GetRequiredService<IDiagnosticsPersistenceObserver>());
        Assert.IsType<FirstObserver>(secondProvider.GetRequiredService<IDiagnosticsPersistenceObserver>());
    }

    [Fact]
    public void Observability_composition_rejects_untracked_explicit_observer_conflicts_before_or_after_default()
    {
        var beforeDefault = new ServiceCollection();
        beforeDefault.AddSingleton<IDiagnosticsPersistenceObserver, FirstObserver>();
        beforeDefault.AddSingleton<IDiagnosticsPersistenceObserver, SecondObserver>();
        Assert.Throws<InvalidOperationException>(beforeDefault.AddDiagnosticsPersistenceObservability);

        var afterDefault = new ServiceCollection();
        afterDefault.AddDiagnosticsPersistenceObservability();
        afterDefault.AddSingleton<IDiagnosticsPersistenceObserver, FirstObserver>();
        afterDefault.AddSingleton<IDiagnosticsPersistenceObserver, SecondObserver>();
        Assert.Throws<InvalidOperationException>(afterDefault.AddDiagnosticsPersistenceObservability);
    }

    [Fact]
    public void Startup_validation_rejects_observer_conflicts_registered_after_composition()
    {
        var services = new ServiceCollection();
        services.AddDiagnosticsPersistenceObservability();
        services.AddSingleton<IDiagnosticsPersistenceObserver, FirstObserver>();
        services.AddSingleton<IDiagnosticsPersistenceObserver, SecondObserver>();

        using var provider = services.BuildServiceProvider();
        var exception = Assert.Throws<OptionsValidationException>(
            provider.GetRequiredService<IStartupValidator>().Validate);

        Assert.Contains(typeof(DiagnosticsPersistenceCounters).FullName!, exception.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(FirstObserver).FullName!, exception.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(SecondObserver).FullName!, exception.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(DiagnosticsPersistenceRegistration.ReplaceDiagnosticsStore), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Startup_validation_rejects_two_direct_observer_types_with_actionable_descriptions()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDiagnosticsPersistenceObserver, FirstObserver>();
        services.AddDiagnosticsPersistenceObservability();
        services.AddSingleton<IDiagnosticsPersistenceObserver, SecondObserver>();

        using var provider = services.BuildServiceProvider();
        var exception = Assert.Throws<OptionsValidationException>(
            provider.GetRequiredService<IStartupValidator>().Validate);

        Assert.Contains(typeof(FirstObserver).FullName!, exception.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(SecondObserver).FullName!, exception.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(DiagnosticsPersistenceRegistration.ReplaceDiagnosticsStore), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Startup_validation_describes_factory_and_type_observer_conflicts()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDiagnosticsPersistenceObserver>(_ => new FirstObserver());
        services.AddDiagnosticsPersistenceObservability();
        services.AddSingleton<IDiagnosticsPersistenceObserver, SecondObserver>();

        using var provider = services.BuildServiceProvider();
        var exception = Assert.Throws<OptionsValidationException>(
            provider.GetRequiredService<IStartupValidator>().Validate);

        Assert.Contains("factory registration", exception.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(SecondObserver).FullName!, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Startup_validation_accepts_an_instance_observer_and_duplicate_composition_registers_one_validator()
    {
        var observer = new FirstObserver();
        var services = new ServiceCollection();
        services.AddSingleton<IDiagnosticsPersistenceObserver>(observer);
        services.AddDiagnosticsPersistenceObservability();
        services.AddDiagnosticsPersistenceObservability();

        Assert.Single(services, descriptor =>
            descriptor.ServiceType == typeof(DiagnosticsPersistenceObserverRegistrationValidator));
        Assert.Single(services, descriptor =>
            descriptor.ImplementationType?.Name == "DiagnosticsPersistenceObserverRegistrationOptionsAdapter");
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IStartupValidator>().Validate();
        Assert.Same(observer, provider.GetRequiredService<IDiagnosticsPersistenceObserver>());
    }

    [Fact]
    public void Observer_registration_validation_exports_only_the_constitution_mandated_implementation()
    {
        var assembly = typeof(DiagnosticsPersistenceObserverRegistrationValidator).Assembly;
        var exportedRegistrationTypes = assembly.ExportedTypes
            .Where(type => type.Name.StartsWith("DiagnosticsPersistenceObserverRegistration", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal([typeof(DiagnosticsPersistenceObserverRegistrationValidator)], exportedRegistrationTypes);
        Assert.True(typeof(DiagnosticsPersistenceObserverRegistrationValidator).IsSealed);
        var constructor = Assert.Single(typeof(DiagnosticsPersistenceObserverRegistrationValidator).GetConstructors());
        Assert.Equal([typeof(IServiceCollection)], constructor.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.Contains(assembly.GetTypes(), type =>
            type.Name == "DiagnosticsPersistenceObserverRegistrationOptionsAdapter" && !type.IsPublic);
    }

    [Fact]
    public void Startup_validation_describes_instance_and_type_observer_conflicts()
    {
        var observer = new FirstObserver();
        var services = new ServiceCollection();
        services.AddSingleton<IDiagnosticsPersistenceObserver>(observer);
        services.AddDiagnosticsPersistenceObservability();
        services.AddSingleton<IDiagnosticsPersistenceObserver, SecondObserver>();

        using var provider = services.BuildServiceProvider();
        var exception = Assert.Throws<OptionsValidationException>(
            provider.GetRequiredService<IStartupValidator>().Validate);

        Assert.Contains(observer.GetType().FullName!, exception.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(SecondObserver).FullName!, exception.Message, StringComparison.Ordinal);
    }

    private interface ITestStore;
    private sealed class FirstStore : ITestStore;
    private sealed class SecondStore : ITestStore;
    private sealed class ReplacementStore : ITestStore;
    private sealed class FirstObserver : TestObserver;
    private sealed class SecondObserver : TestObserver;

    private abstract class TestObserver : IDiagnosticsPersistenceObserver
    {
        public void RecordState(DiagnosticsDrainState state) { }
        public void RecordRetry(DiagnosticsPersistenceOperation operation, int attempt, int maxAttempts) { }
        public void RecordOperationFailure(DiagnosticsPersistenceOperation operation) { }
        public void RecordLoss(DiagnosticsPersistenceLossReason reason, long count) { }
    }

    private static IEnumerable<string> FindDiagnosticsProjects() =>
        Directory.EnumerateFiles(DiagnosticsSourceRoot, "*.csproj", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(DiagnosticsTestRoot, "*.csproj", SearchOption.AllDirectories));

    private static IEnumerable<string> FindDiagnosticsSourceProjects() =>
        Directory.EnumerateFiles(DiagnosticsSourceRoot, "*.csproj", SearchOption.AllDirectories);

    private static IEnumerable<string> FindEfProjectViolations(string project)
    {
        var relativePath = RelativePath(project);
        if (ContainsEfCore(project))
            yield return $"{relativePath}: EF Core project path";

        var document = XDocument.Load(project);
        foreach (var package in document.Descendants("PackageReference")
                     .Select(element => element.Attribute("Include")?.Value)
                     .OfType<string>()
                     .Where(ContainsEfCore))
            yield return $"{relativePath}: PackageReference {package}";

        foreach (var reference in document.Descendants("ProjectReference")
                     .Select(element => element.Attribute("Include")?.Value)
                     .OfType<string>()
                     .Where(ContainsEfCore))
            yield return $"{relativePath}: ProjectReference {reference}";
    }

    private static IEnumerable<string> FindEfDirectoryViolations() =>
        Directory.EnumerateDirectories(DiagnosticsSourceRoot, "*", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateDirectories(DiagnosticsTestRoot, "*", SearchOption.AllDirectories))
            .Where(ContainsEfCore)
            .Select(directory => $"{RelativePath(directory)}: EF Core directory");

    private static IEnumerable<string> FindEfSourceViolations() =>
        Directory.EnumerateFiles(DiagnosticsSourceRoot, "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(DiagnosticsTestRoot, "*.cs", SearchOption.AllDirectories))
            .Where(file => !string.Equals(file, SourceFilePath, StringComparison.Ordinal))
            .Select(file => (Path: RelativePath(file), Match: EfSourcePattern().Match(File.ReadAllText(file))))
            .Where(hit => hit.Match.Success)
            .Select(hit => $"{hit.Path}: {hit.Match.Value}");

    private static IEnumerable<string> FindGroundworkProjectGraphViolations(string project)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return Traverse(project, visited);

        static IEnumerable<string> Traverse(string current, ISet<string> visited)
        {
            if (!visited.Add(current))
                yield break;

            var document = XDocument.Load(current);
            foreach (var package in document.Descendants("PackageReference")
                         .Select(element => element.Attribute("Include")?.Value)
                         .OfType<string>()
                         .Where(package => package.StartsWith("Groundwork", StringComparison.Ordinal)))
                yield return $"{RelativePath(current)} -> PackageReference {package}";

            foreach (var include in document.Descendants("ProjectReference")
                         .Select(element => element.Attribute("Include")?.Value)
                         .OfType<string>())
            {
                var referencedProject = Path.GetFullPath(Path.Combine(
                    Path.GetDirectoryName(current)!,
                    include.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar)));
                if (RelativePath(referencedProject).Contains("Groundwork", StringComparison.OrdinalIgnoreCase))
                    yield return $"{RelativePath(current)} -> ProjectReference {include}";

                if (File.Exists(referencedProject))
                    foreach (var violation in Traverse(referencedProject, visited))
                        yield return violation;
            }
        }
    }

    private static IEnumerable<string> FindGroundworkSourceViolations() =>
        Directory.EnumerateFiles(DiagnosticsSourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsGroundworkAdapterSource(file))
            .Select(file => (Path: RelativePath(file), Match: GroundworkSourcePattern().Match(File.ReadAllText(file))))
            .Where(hit => hit.Match.Success)
            .Select(hit => $"{hit.Path}: {hit.Match.Value}");

    private static IEnumerable<(Type Owner, string Member, Type Type)> PublicSurfaceTypes(Assembly assembly)
    {
        foreach (var type in assembly.GetExportedTypes())
        {
            yield return (type, "base type", type.BaseType ?? typeof(void));
            foreach (var implemented in type.GetInterfaces())
                yield return (type, "interface", implemented);
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                yield return (type, field.Name, field.FieldType);
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                yield return (type, property.Name, property.PropertyType);
            foreach (var @event in type.GetEvents(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                yield return (type, @event.Name, @event.EventHandlerType ?? typeof(void));
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                         .Cast<MethodBase>()
                         .Concat(type.GetConstructors(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)))
            {
                if (method is MethodInfo methodInfo)
                    yield return (type, method.Name, methodInfo.ReturnType);
                foreach (var parameter in method.GetParameters())
                    yield return (type, $"{method.Name}({parameter.Name})", parameter.ParameterType);
            }
        }
    }

    private static bool IsGroundworkType(Type type) =>
        type.Assembly.GetName().Name?.StartsWith("Groundwork", StringComparison.Ordinal) == true ||
        (type.HasElementType && type.GetElementType() is { } element && IsGroundworkType(element)) ||
        type.IsGenericType && type.GetGenericArguments().Any(IsGroundworkType);

    private static bool IsGroundworkAdapterProject(string project) =>
        IsGroundworkAdapterSource(Path.GetDirectoryName(project)!);

    private static bool IsGroundworkAdapterSource(string path)
    {
        var relativePath = RelativePath(path);
        return IsWithin("src/Elsa/Diagnostics/OpenTelemetry/Persistence/Groundwork") ||
               IsWithin("src/Elsa/Diagnostics/StructuredLogs/Persistence/Groundwork") ||
               IsWithin("src/Elsa/Diagnostics/Persistence/Groundwork");

        bool IsWithin(string root) =>
            string.Equals(relativePath, root, StringComparison.Ordinal) ||
            relativePath.StartsWith(root + "/", StringComparison.Ordinal);
    }

    private static bool ContainsEfCore(string value) =>
        value.Contains("EFCore", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("EntityFrameworkCore", StringComparison.OrdinalIgnoreCase);

    private static string RelativePath(string path) =>
        Path.GetRelativePath(RepoRoot, path).Replace(Path.DirectorySeparatorChar, '/');

    private static string SourceFilePath { get; } = Path.GetFullPath(Path.Combine(
        RepoRoot,
        "tests",
        "Elsa",
        "Diagnostics",
        "Persistence",
        "Tests",
        "DiagnosticsPersistenceArchitectureTests.cs"));

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Elsa.Server.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root from the test output directory.");
    }

    [GeneratedRegex(@"\b(?:Microsoft\.EntityFrameworkCore|Persistence\.EFCore|IDbContextFactory|DbContext|IEntityTypeConfiguration|MigrationBuilder|MigrationAttribute|ModelBuilder|AddDbContext|UseSqlite|UseSqlServer|UseNpgsql|UseInMemoryDatabase)\b|:\s*Migration\b", RegexOptions.CultureInvariant)]
    private static partial Regex EfSourcePattern();

    [GeneratedRegex(@"\b(?:using\s+Groundwork(?:\.|\s*;)|Groundwork\.)", RegexOptions.CultureInvariant)]
    private static partial Regex GroundworkSourcePattern();

    [GeneratedRegex(@"`(?<class>[A-Za-z0-9_]+Tests)\.(?<method>[A-Za-z0-9_]+)`", RegexOptions.CultureInvariant)]
    private static partial Regex LedgerEvidencePattern();
}
