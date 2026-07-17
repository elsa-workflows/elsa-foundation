using System.Text.RegularExpressions;
using Xunit;

namespace Elsa.Architecture.Tests;

public sealed class GroundworkPersistenceLifetimeTests
{
    private static readonly Regex GenericRegistrationPattern = new(
        @"(?:\b[A-Za-z_][A-Za-z0-9_]*\s*\.\s*(?:Try)?Add|\bServiceDescriptor\s*\.\s*)(?<lifetime>Singleton|Scoped|Transient)\s*<(?<types>(?:[^<>]|<[^<>]*>)+)>",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex NonGenericNewRegistrationPattern = new(
        @"\b[A-Za-z_][A-Za-z0-9_]*\s*\.\s*(?:Try)?Add(?<lifetime>Singleton|Scoped|Transient)\s*\(\s*(?:[A-Za-z_][A-Za-z0-9_]*\s*=>\s*)?new\s+(?<implementation>(?:global::)?[A-Za-z_][A-Za-z0-9_.]*(?:<[^;()]+>)?)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex NewFactoryImplementationPattern = new(
        @"(?:=>\s*|\(\s*)new\s+(?<implementation>(?:global::)?[A-Za-z_][A-Za-z0-9_.]*(?:<[^;()]+>)?)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly string[] LogicBearingSuffixes =
    [
        "Store",
        "Repository",
        "Transport",
        "WorkQueue",
        "Writer",
        "Command",
        "Handler",
        "Factory",
        "Mapper",
        "Validator",
        "Aggregator",
        "AccessContextAccessor",
        "SessionFactory",
        "PrivilegedAccessRecorder",
        "RecoveryScanner",
        "OwnershipService",
        "ManifestSource",
        "PageQuery",
        "StoreSessionSource",
        "PageRouteSource",
        "Initializer"
    ];

    private static readonly LongerLivedException[] LongerLivedExceptions =
    [
        new(
            "src/Elsa/Persistence/Groundwork/Unified/DependencyInjection/GroundworkStorageCompositionRegistration.cs",
            "IPhysicalSchemaManifestSource",
            "The deployment schema source is one immutable application-wide value shared by runtime admission and schema tooling; it carries no request context or mutable operation state."),
        new(
            "src/Elsa/Persistence/Groundwork/DependencyInjection/GroundworkStoreSessionRegistration.cs",
            "GroundworkStoreSessionSource",
            "The provider publishes one application-lifetime factory over admitted static resources; every invocation returns a fresh access-bound session and the source stores no request context."),
        new(
            "src/Elsa/Persistence/Groundwork/DependencyInjection/GroundworkStoreSessionRegistration.cs",
            "IGroundworkStoreSessionSource",
            "This interface aliases the same provider-owned static session source; access selection remains an argument to each invocation and is never retained."),
        new(
            "src/Elsa/Persistence/Groundwork/DependencyInjection/GroundworkStoreSessionRegistration.cs",
            "GroundworkWorkflowExecutionStatePageRouteSource",
            "Provider startup publishes one immutable admitted physical route; scoped page-query adapters hold all request access and operation state."),
        new(
            "src/Elsa/Persistence/Groundwork/Sqlite/DependencyInjection/SqliteGroundworkDocumentStoreRegistration.cs",
            "SqliteGroundworkDocumentStoreInitializer",
            "The shell initializer owns one provider startup lifecycle and delegates every access-bound runtime operation to scoped sessions."),
        new(
            "src/Elsa/Persistence/Groundwork/Sqlite/DependencyInjection/SqliteGroundworkDocumentStoreRegistration.cs",
            "IShellInitializer",
            "This application-lifetime alias exposes the same provider startup initializer through the shell contract."),
        new(
            "src/Elsa/Persistence/Groundwork/SqlServer/DependencyInjection/SqlServerGroundworkDocumentStoreRegistration.cs",
            "SqlServerGroundworkDocumentStoreInitializer",
            "The shell initializer owns one provider startup lifecycle and delegates every access-bound runtime operation to scoped sessions."),
        new(
            "src/Elsa/Persistence/Groundwork/SqlServer/DependencyInjection/SqlServerGroundworkDocumentStoreRegistration.cs",
            "IShellInitializer",
            "This application-lifetime alias exposes the same provider startup initializer through the shell contract."),
        new(
            "src/Elsa/Persistence/Groundwork/PostgreSql/DependencyInjection/PostgreSqlGroundworkDocumentStoreRegistration.cs",
            "PostgreSqlGroundworkDocumentStoreInitializer",
            "The shell initializer owns one provider startup lifecycle and delegates every access-bound runtime operation to scoped sessions."),
        new(
            "src/Elsa/Persistence/Groundwork/PostgreSql/DependencyInjection/PostgreSqlGroundworkDocumentStoreRegistration.cs",
            "IShellInitializer",
            "This application-lifetime alias exposes the same provider startup initializer through the shell contract."),
        new(
            "src/Elsa/Persistence/Groundwork/MongoDb/DependencyInjection/MongoDbGroundworkDocumentStoreRegistration.cs",
            "MongoDbGroundworkDocumentStoreInitializer",
            "The shell initializer owns one provider startup lifecycle and delegates every access-bound runtime operation to scoped sessions."),
        new(
            "src/Elsa/Persistence/Groundwork/MongoDb/DependencyInjection/MongoDbGroundworkDocumentStoreRegistration.cs",
            "IShellInitializer",
            "This application-lifetime alias exposes the same provider startup initializer through the shell contract.")
    ];

    private static readonly ScopedConsumerRegistration[] ScopedConsumerRegistrations =
    [
        new("src/Elsa/Activities/Runtime/ActivitiesRuntimeFeature.cs", "ActivityFaultIncidentRecorder"),
        new("src/Elsa/Secrets/Extensions/SecretsServiceCollectionExtensions.cs", "ISecretManager"),
        new("src/Elsa/Secrets/Extensions/SecretsServiceCollectionExtensions.cs", "ISecretValueResolver"),
        new("src/Elsa/Workflows/Publishing/Api/WorkflowsPublishingApiFeature.cs", "IPublicationProjectionPreparer"),
        new("src/Elsa/Workflows/Publishing/Api/WorkflowsPublishingApiFeature.cs", "IPublicationActivator"),
        new("src/Elsa/Workflows/Publishing/Api/WorkflowsPublishingApiFeature.cs", "PublicationSnapshotReviewService"),
        new("src/Elsa/Workflows/Runtime/Api/WorkflowsRuntimeTriggersFeature.cs", "IWorkflowTriggerIndexer"),
        new("src/Elsa/Workflows/Runtime/Api/WorkflowsRuntimeTriggersFeature.cs", "IBookmarkStimulusIndex"),
        new("src/Elsa/Workflows/Runtime/Api/WorkflowsRuntimeTriggersFeature.cs", "IGlobalBookmarkStimulusLookup"),
        new("src/Elsa/Workflows/Runtime/Api/WorkflowsRuntimeTriggersFeature.cs", "IStimulusRouter"),
        new("src/Elsa/Workflows/Runtime/Distributed/WorkflowsRuntimeDistributedFeature.cs", "IExecutionPlacementService"),
        new("src/Elsa/Workflows/Runtime/Http/WorkflowsRuntimeHttpFeature.cs", "IWorkflowTriggerIndexValidator"),
        new("src/Elsa/Workflows/Runtime/ReferenceGarbageCollection/WorkflowsRuntimeReferenceGarbageCollectionFeature.cs", "IWorkflowExecutableReferenceGarbageCollector"),
        new("src/Elsa/Workflows/Runtime/Resumption/WorkflowsRuntimeResumptionFeature.cs", "IRuntimeResumptionService"),
        new("src/Elsa/Workflows/Runtime/Scheduling/WorkflowsRuntimeRecurringTriggersFeature.cs", "IWorkflowTriggerIndexer"),
        new("src/Elsa/Workflows/Runtime/Scheduling/WorkflowsRuntimeSchedulingFeature.cs", "IDurableTimerScheduler")
    ];

    private static readonly LongLivedScopeBridge[] LongLivedScopeBridges =
    [
        new(
            "src/Elsa/Workflows/Runtime/Extensions/RuntimeCoreServiceCollectionExtensions.cs",
            "IWorkflowExecutionCommandExecutor",
            "ScopedWorkflowExecutionCommandExecutor",
            "src/Elsa/Workflows/Runtime/Services/ScopedWorkflowExecutionCommandExecutor.cs",
            "IPersistenceOperationScopeFactory"),
        new(
            "src/Elsa/Workflows/Runtime/Extensions/RuntimeCoreServiceCollectionExtensions.cs",
            "IWorkflowExecutionActorProvider",
            "InProcessWorkflowExecutionActorProvider",
            "src/Elsa/Workflows/Runtime/Services/InProcessWorkflowExecutionActorProvider.cs",
            "IWorkflowExecutionCommandExecutor"),
        new(
            "src/Elsa/Workflows/Runtime/Distributed/WorkflowsRuntimeDistributedFeature.cs",
            "DistributedWorkflowExecutionActorProvider",
            "DistributedWorkflowExecutionActorProvider",
            "src/Elsa/Workflows/Runtime/Distributed/Services/DistributedWorkflowExecutionActorProvider.cs",
            "IPersistenceOperationScopeFactory"),
        new(
            "src/Elsa/Workflows/Runtime/Scheduling/WorkflowsRuntimeSchedulingFeature.cs",
            "IRecurringTask",
            "DurableTimerPumpTask",
            "src/Elsa/Workflows/Runtime/Scheduling/DurableTimerPumpTask.cs",
            "IPersistenceScopeRunner"),
        new(
            "src/Elsa/Workflows/Runtime/Scheduling/WorkflowsRuntimeRecurringTriggersFeature.cs",
            "IRecurringTask",
            "RecurringTriggerPumpTask",
            "src/Elsa/Workflows/Runtime/Scheduling/RecurringTriggerPumpTask.cs",
            "IPersistenceScopeRunner"),
        new(
            "src/Elsa/Workflows/Runtime/Resumption/WorkflowsRuntimeResumptionFeature.cs",
            "IRecurringTask",
            "RuntimeResumptionPumpTask",
            "src/Elsa/Workflows/Runtime/Resumption/RuntimeResumptionPumpTask.cs",
            "IPersistenceScopeRunner"),
        new(
            "src/Elsa/Workflows/Runtime/ReferenceGarbageCollection/WorkflowsRuntimeReferenceGarbageCollectionFeature.cs",
            "IRecurringTask",
            "WorkflowExecutableReferenceGarbageCollectionPumpTask",
            "src/Elsa/Workflows/Runtime/ReferenceGarbageCollection/WorkflowExecutableReferenceGarbageCollectionPumpTask.cs",
            "IPersistenceScopeRunner"),
        new(
            "src/Elsa/Workflows/Runtime/Distributed/WorkflowsRuntimeDistributedFeature.cs",
            "IRecurringTask",
            "ExecutionPlacementPumpTask",
            "src/Elsa/Workflows/Runtime/Distributed/Services/ExecutionPlacementPumpTask.cs",
            "IPersistenceScopeRunner")
    ];

    [Fact]
    public void Logic_bearing_Groundwork_services_are_scoped()
    {
        var violations = DiscoverRegistrations()
            .Where(IsLogicBearing)
            .Where(registration => registration.Lifetime != "Scoped")
            .Where(registration => !IsDocumentedException(registration))
            .Select(registration =>
                $"{registration.SourcePath}: {registration.ServiceType}" +
                (registration.ImplementationType is null ? string.Empty : $" -> {registration.ImplementationType}") +
                $" is registered {registration.Lifetime}; logic-bearing Groundwork services must be scoped.")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(violations.Length == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Longer_lived_Groundwork_exceptions_are_exact_and_documented()
    {
        var registrations = DiscoverRegistrations();

        Assert.All(LongerLivedExceptions, exception =>
        {
            Assert.False(string.IsNullOrWhiteSpace(exception.Rationale));
            var registration = Assert.Single(registrations, candidate =>
                candidate.SourcePath == exception.SourcePath &&
                candidate.ServiceType == exception.ServiceType);
            Assert.Equal("Singleton", registration.Lifetime);
        });

        var duplicateKeys = LongerLivedExceptions
            .GroupBy(exception => (exception.SourcePath, exception.ServiceType))
            .Where(group => group.Count() != 1)
            .Select(group => $"{group.Key.SourcePath}: {group.Key.ServiceType}")
            .ToArray();
        Assert.Empty(duplicateKeys);
    }

    [Fact]
    public void Provider_neutral_consumers_of_access_bound_stores_are_scoped()
    {
        var registrations = DiscoverAllRegistrations();

        Assert.All(ScopedConsumerRegistrations, expected =>
        {
            var registration = Assert.Single(registrations, candidate =>
                candidate.SourcePath == expected.SourcePath &&
                candidate.ServiceType == expected.ServiceType);
            Assert.Equal("Scoped", registration.Lifetime);
        });
    }

    [Fact]
    public void Host_lifetime_runtime_bridges_cross_the_scope_boundary_explicitly()
    {
        var registrations = DiscoverAllRegistrations();

        Assert.All(LongLivedScopeBridges, expected =>
        {
            var registration = Assert.Single(registrations, candidate =>
                candidate.SourcePath == expected.RegistrationSourcePath &&
                candidate.ServiceType == expected.ServiceType &&
                candidate.ImplementationType == expected.ImplementationType);
            Assert.Equal("Singleton", registration.Lifetime);

            var implementationSource = File.ReadAllText(Path.Combine(RepoRoot, expected.ImplementationSourcePath));
            Assert.Contains(expected.RequiredScopeBoundary, implementationSource, StringComparison.Ordinal);
        });
    }

    private static bool IsLogicBearing(GroundworkRegistration registration) =>
        HasLogicBearingSuffix(registration.ServiceType) ||
        registration.ImplementationType is not null && HasLogicBearingSuffix(registration.ImplementationType);

    private static bool HasLogicBearingSuffix(string typeName)
    {
        var simpleName = SimpleTypeName(typeName);
        return LogicBearingSuffixes.Any(suffix => simpleName.EndsWith(suffix, StringComparison.Ordinal));
    }

    private static bool IsDocumentedException(GroundworkRegistration registration) =>
        LongerLivedExceptions.Any(exception =>
            exception.SourcePath == registration.SourcePath &&
            exception.ServiceType == registration.ServiceType);

    private static IReadOnlyList<GroundworkRegistration> DiscoverRegistrations() =>
        DiscoverAllRegistrations()
            .Where(registration => registration.SourcePath.Contains("/Persistence/Groundwork/", StringComparison.Ordinal))
            .ToArray();

    private static IReadOnlyList<GroundworkRegistration> DiscoverAllRegistrations() =>
        Directory.EnumerateFiles(Path.Combine(RepoRoot, "src", "Elsa"), "*.cs", SearchOption.AllDirectories)
            .Select(file => new
            {
                FullPath = file,
                RelativePath = Path.GetRelativePath(RepoRoot, file).Replace(Path.DirectorySeparatorChar, '/'),
                Source = File.ReadAllText(file)
            })
            .Where(file => file.Source.Contains("IServiceCollection", StringComparison.Ordinal))
            .SelectMany(file => GenericRegistrationPattern.Matches(file.Source)
                .Select(match => Registration(file.RelativePath, file.Source, match))
                .Concat(NonGenericNewRegistrationPattern.Matches(file.Source)
                    .Select(match => InferredRegistration(file.RelativePath, match))))
            .ToArray();

    private static GroundworkRegistration Registration(string sourcePath, string source, Match match)
    {
        var typeArguments = SplitTypeArguments(match.Groups["types"].Value);
        var implementationType = typeArguments.Count > 1
            ? typeArguments[^1]
            : InferFactoryImplementation(source, match);
        return new(
            sourcePath,
            match.Groups["lifetime"].Value,
            typeArguments[0],
            implementationType);
    }

    private static GroundworkRegistration InferredRegistration(string sourcePath, Match match)
    {
        var implementationType = match.Groups["implementation"].Value;
        return new(
            sourcePath,
            match.Groups["lifetime"].Value,
            implementationType,
            implementationType);
    }

    private static string? InferFactoryImplementation(string source, Match registrationMatch)
    {
        var statementEnd = source.IndexOf(';', registrationMatch.Index + registrationMatch.Length);
        if (statementEnd < 0)
            statementEnd = source.Length;
        var statementTail = source[registrationMatch.Index..statementEnd];
        var implementation = NewFactoryImplementationPattern.Match(statementTail);
        return implementation.Success ? implementation.Groups["implementation"].Value : null;
    }

    private static IReadOnlyList<string> SplitTypeArguments(string typeArguments)
    {
        var results = new List<string>();
        var start = 0;
        var depth = 0;
        for (var index = 0; index < typeArguments.Length; index++)
        {
            switch (typeArguments[index])
            {
                case '<':
                    depth++;
                    break;
                case '>':
                    depth--;
                    break;
                case ',' when depth == 0:
                    results.Add(typeArguments[start..index].Trim());
                    start = index + 1;
                    break;
            }
        }

        results.Add(typeArguments[start..].Trim());
        return results;
    }

    private static string SimpleTypeName(string typeName)
    {
        var normalized = typeName.Replace("global::", string.Empty, StringComparison.Ordinal).Trim();
        var genericStart = normalized.IndexOf('<');
        if (genericStart >= 0)
            normalized = normalized[..genericStart];
        var namespaceSeparator = normalized.LastIndexOf('.');
        return namespaceSeparator >= 0 ? normalized[(namespaceSeparator + 1)..] : normalized;
    }

    private static string RepoRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Elsa.Server.slnx")))
                directory = directory.Parent;

            return directory?.FullName
                   ?? throw new InvalidOperationException("Could not locate the Elsa Foundation repository root.");
        }
    }

    private sealed record GroundworkRegistration(
        string SourcePath,
        string Lifetime,
        string ServiceType,
        string? ImplementationType);

    private sealed record LongerLivedException(
        string SourcePath,
        string ServiceType,
        string Rationale);

    private sealed record ScopedConsumerRegistration(string SourcePath, string ServiceType);

    private sealed record LongLivedScopeBridge(
        string RegistrationSourcePath,
        string ServiceType,
        string ImplementationType,
        string ImplementationSourcePath,
        string RequiredScopeBoundary);
}
