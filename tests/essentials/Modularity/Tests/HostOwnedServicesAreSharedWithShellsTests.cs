using System.Diagnostics;
using System.Reflection;
using CShells.DependencyInjection;
using CShells.Lifecycle;
using Elsa.Attention.Core;
using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Models;
using Elsa.Foundation.DataProtection.EntityFrameworkCore;
using Elsa.ExtensionBuilder.Api.Extensions;
using Elsa.Foundation.Host.ModuleManagement;
using Elsa.Foundation.Host.Shells;
using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.Schema.SchemaFinalization;
using Elsa.Workbench;
using Elsa.Workbench.OpenIddict;
using Elsa.Workbench.OpenIddictEngines;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Nuplane.Abstractions;
using Nuplane.Reconciliation;
using Xunit;

namespace Elsa.Modularity.Tests;

/// <summary>
/// CShells copies every root registration into every shell container, and a registration made by type or by factory is
/// instantiated again there. A service that holds the host's own state (the trigger queue only the host's dispatcher reads, the
/// fleet the finalization gates count) is wrong in a shell: the shell's copy waits for a dispatcher that does not exist, or counts
/// a fleet of its own (#2159). This builds the real <c>Elsa.Foundation.Host</c> and <c>Elsa.Workbench</c> compositions by running
/// their entry points up to the built host, activates a shell through CShells, and compares every singleton the host registers
/// under a type of an <c>Elsa.*</c> or <c>Nuplane.*</c> assembly: each must be the same instance in the shell as at the root, or
/// be named in <see cref="PerShell"/> with the reason it is not.
/// </summary>
/// <remarks>
/// The comparison is over what the host registered, so a service someone adds later is held without anyone remembering to list
/// it. A new difference fails with the type named: share it with <c>ShareWithShells</c> when a shell can reach the host's
/// state through it, or list it with the reason a shell's own copy is right.
/// </remarks>
public sealed class HostOwnedServicesAreSharedWithShellsTests
{
    private const string ProbeShell = "probe";

    private const string IntentionallyPerShell = "intentionally per shell: ";

    private const string UnreachableFromShells = "unreachable from shell code today; an upstream Nuplane instance registration would remove the entry";

    /// <summary>
    /// The singletons a shell is meant to hold its own copy of, or that nothing in a shell reaches, by the name the failure gives,
    /// each with why. A service a shell can reach that holds the host's own state belongs to <c>ShareWithShells</c>, not here: an
    /// entry here for one hides the bug, so the reason has to hold.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> PerShell = new[]
    {
        Entries(
            IntentionallyPerShell + "a shell's runnability source publishes through its own in-process member",
            "Elsa.Cluster.Core.Contracts.IClusterMembership"),
        Entries(
            IntentionallyPerShell + "the report source of the shell's own in-process member; the host's member, in-process or durable, reads the host's own",
            "Elsa.Cluster.Core.Contracts.IMemberReportSource<Elsa.Cluster.Core.Models.ReadabilitySection>",
            "Elsa.Cluster.Core.Contracts.IMemberReportSource<Elsa.Cluster.Core.Models.RunnabilitySection>"),
        Entries(
            IntentionallyPerShell + "registered by type so each shell reads the finalization gates of the modules activated in its own container",
            "Elsa.Cluster.Core.Contracts.IObservedSchemaFinalization",
            "Elsa.Cluster.Core.Contracts.ISchemaDormancyCheck",
            "Elsa.Persistence.Schema.SchemaFinalization.EfSchemaFinalizationGates"),
        Entries(
            IntentionallyPerShell + "registered as a shell initializer, so each shell runs the validation of its own container's provider bindings",
            "Elsa.Persistence.EntityFramework.EfProviderBindingValidator"),
        Entries(
            IntentionallyPerShell + "holds no state; it reads the ISupersededAssemblySource every container shares",
            "Elsa.Modularity.EntityFramework.IEfModuleAssemblySource"),
        Entries(
            IntentionallyPerShell + "holds no state; each shell's permission catalog aggregates the contributors of its own container, the host's root-registered ones copied in",
            "Elsa.Foundation.Identity.Core.Authorization.IPermissionContributor"),
        Entries(
            "unreachable from shell code: a hosted service the host starts once, and a shell container never starts its copy (a disposable one could not be shared in any case)",
            "Elsa.Persistence.EntityFramework.EfModuleMigrator<Elsa.Cluster.EntityFrameworkCore.ClusterMembershipDbContext>",
            "Elsa.Persistence.EntityFramework.EfModuleMigrator<Elsa.Foundation.DataProtection.EntityFrameworkCore.DataProtectionKeysDbContext>",
            "Elsa.Workbench.OpenIddict.OpenIddictIdentityStoreInitializer",
            "Elsa.Workbench.Readiness.DefaultShellWarmup",
            "Elsa.Workbench.WorkbenchOpenIddictMigrator"),
        Entries(
            IntentionallyPerShell + "records what its own container's options pipeline resolved the store's AutoMigrate to, for the host's migrator, which a shell never starts",
            "Elsa.Workbench.WorkbenchOpenIddictMigrationSwitch"),
        Entries(
            "unreachable from shell code: only CShells' runtime feature catalog, which the host holds, resolves the feature assembly provider, and a shell's copy would read a Nuplane catalog that has loaded nothing",
            "Elsa.Foundation.Host.Feed.NuplaneAssemblyProvider",
            "Elsa.Workbench.NuplaneAssemblyProvider"),
        Entries(
            "unreachable from shell code: Nuplane's dispatcher, which runs on the host's own container, is the only caller of its observers",
            "Elsa.Foundation.Host.Shells.ShellReloadOnPackagesChanged",
            "Elsa.Workbench.ShellCatalogRefreshOnPackagesChanged"),
        Entries(
            UnreachableFromShells,
        "Nuplane.Abstractions.IActivePackageCatalog", "Nuplane.Abstractions.ICycleFailureContributor", "Nuplane.Abstractions.IDesiredPackageSource", "Nuplane.Abstractions.IDesiredStateContributor", "Nuplane.Abstractions.INuplaneObserver", "Nuplane.Abstractions.IPackageResolver",
        "Nuplane.Capabilities.CapabilityContributionLedger", "Nuplane.Capabilities.CapabilityDesiredStateContributor",
        "Nuplane.Events.IObserverEventDispatcher", "Nuplane.Events.ObserverEventDispatcher",
        "Nuplane.Feeds.IRemotePackageAcquirer",
        "Nuplane.Feeds.Configuration.FeedCredentialOptionsValidator",
        "Nuplane.Feeds.Credentials.ISecretReferenceProvider", "Nuplane.Feeds.Credentials.ISecretReferenceResolver", "Nuplane.Feeds.Credentials.SecretReferenceResolver",
        "Nuplane.Feeds.Policy.FeedResolutionPolicy",
        "Nuplane.Feeds.Versioning.IFeedVersionEnumerator", "Nuplane.Feeds.Versioning.IVersionRangeEvaluator", "Nuplane.Feeds.Versioning.NuGetFeedVersionEnumerator",
        "Nuplane.Health.IReconciliationHealthEvaluator", "Nuplane.Health.ObservationDegradationTracker", "Nuplane.Health.ReconciliationHealthEvaluator",
        "Nuplane.Hosting.ILastKnownGoodStartupRecoveryService", "Nuplane.Hosting.ReconciliationTriggerQueue", "Nuplane.Hosting.StartupRecoveryState",
        "Nuplane.Loading.AssemblyScanCandidateProjector", "Nuplane.Loading.HostIntegratedAssemblyResolutionCatalog", "Nuplane.Loading.HostIntegratedAssemblyResolver", "Nuplane.Loading.IPackageAssemblyCatalog", "Nuplane.Loading.IPackageLoadModeAdvisor", "Nuplane.Loading.IPackageLoadStateCatalog", "Nuplane.Loading.IPackageTypeFinder", "Nuplane.Loading.LoadingCatalog", "Nuplane.Loading.LoadingCatalogRefreshTracker", "Nuplane.Loading.LoadingEventDispatcher", "Nuplane.Loading.LoadingFailureTracker", "Nuplane.Loading.LoadingOptionsValidator", "Nuplane.Loading.PackageAssemblyCatalog", "Nuplane.Loading.PackageAssemblyProvider", "Nuplane.Loading.PackageLoadModeSelector", "Nuplane.Loading.PackageLoader", "Nuplane.Loading.PackageMetadataLoadModeAdvisor", "Nuplane.Loading.PackageMetadataLoadModeReader", "Nuplane.Loading.PackageTypeFinder", "Nuplane.Loading.PackageUnloadCoordinator", "Nuplane.Loading.SharedAssemblyPolicyMatcher",
        "Nuplane.Metadata.IPackageMetadataReader", "Nuplane.Metadata.NuplanePackageMetadataReader",
        "Nuplane.Observability.IReconciliationLogger", "Nuplane.Observability.ReconciliationLogger", "Nuplane.Observability.ReconciliationMetrics", "Nuplane.Observability.ReconciliationTelemetry",
        "Nuplane.Operational.ActivePackageCatalog", "Nuplane.Operational.IOperationalStateContributor", "Nuplane.Operational.OperationalSnapshotProjector",
        "Nuplane.Reconciliation.DesiredActualDiffEngine", "Nuplane.Reconciliation.DryRunPlanner", "Nuplane.Reconciliation.IDesiredActualDiffEngine", "Nuplane.Reconciliation.IDryRunPlanner", "Nuplane.Reconciliation.ILockFileCoordinator", "Nuplane.Reconciliation.IReconciliationRetryPolicy", "Nuplane.Reconciliation.IReconciliationService", "Nuplane.Reconciliation.LockFileCoordinator", "Nuplane.Reconciliation.LockFileStore", "Nuplane.Reconciliation.ReconciliationRetryPolicy", "Nuplane.Reconciliation.ReconciliationService",
        "Nuplane.Sources.DesiredManifestPackageSource", "Nuplane.Sources.DesiredManifestReader", "Nuplane.Sources.DesiredStateAggregator", "Nuplane.Sources.FeedRuleResultSelector", "Nuplane.Sources.IDesiredStateAggregator",
        "Nuplane.Store.Cleanup.CleanupPolicyEvaluator", "Nuplane.Store.Cleanup.IPackageCleanupService", "Nuplane.Store.Cleanup.PackageCleanupService",
        "Nuplane.Store.State.EffectiveStorePersistenceSettings", "Nuplane.Store.State.FailureRecorder", "Nuplane.Store.State.IFailureRecorder", "Nuplane.Store.State.IStoreLock", "Nuplane.Store.State.IStoreRegistry", "Nuplane.Store.State.IStoreStateSerializer", "Nuplane.Store.State.StoreLock", "Nuplane.Store.State.StoreRegistry", "Nuplane.Store.State.StoreStateSerializer"),
    }.SelectMany(entries => entries).ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);

    private static IEnumerable<KeyValuePair<string, string>> Entries(string reason, params string[] types) =>
        types.Select(type => KeyValuePair.Create(type, reason));

    /// <summary>The root singletons the optional Extension Builder composes (#2294); the host's build worker drains the queue and only the host's storage holds the state gate.</summary>
    private static readonly string[] ExtensionBuilderHostOwned =
    [
        "Elsa.ExtensionBuilder.Api.ExtensionBuilderBackgroundBuildQueue",
        "Elsa.ExtensionBuilder.Api.IExtensionBuilderBuildQueue",
        "Elsa.ExtensionBuilder.Api.IExtensionBuilderStorage",
        "Elsa.ExtensionBuilder.Api.IExtensionBuilderTemplateCatalog"
    ];

    /// <summary>
    /// Each host, on the in-process membership it is a cluster of one with, and on the durable EF membership that joins a cluster;
    /// the Workbench also with the optional Extension Builder enabled, which composes root singletons of its own.
    /// </summary>
    public static TheoryData<string, bool, bool> Hosts => new()
    {
        { "Elsa.Foundation.Host", false, false },
        { "Elsa.Foundation.Host", true, false },
        { "Elsa.Workbench", false, false },
        { "Elsa.Workbench", true, false },
        { "Elsa.Workbench", false, true }
    };

    [Theory]
    [MemberData(nameof(Hosts))]
    public async Task A_shell_of_the_real_composition_holds_the_hosts_own_instance_of_every_singleton_it_does_not_own(string host, bool durableMembership, bool extensionBuilder)
    {
        using var content = ContentRoot.For(host);
        string[] arguments = extensionBuilder
            ? [.. content.Arguments(durableMembership), $"--{ExtensionBuilderServiceCollectionExtensions.EnabledConfigurationKey}", "true"]
            : content.Arguments(durableMembership);
        using var built = BuiltHost.Run(EntryAssembly(host), arguments);
        var root = built.Host.Services;

        var shell = await root.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ProbeShell);

        Assert.Equal(durableMembership ? ClusterProviderKind.Durable : ClusterProviderKind.InProcess, root.GetRequiredService<IClusterMembership>().ProviderKind);
        var compared = SingletonServiceTypes(root).Select(type => (Type: type, Difference: Difference(type, root, shell.ServiceProvider))).ToArray();
        var notShared = compared
            .Where(service => service.Difference is not null && !PerShell.ContainsKey(Describe(service.Type)))
            .Select(service => $"{Describe(service.Type)} ({service.Difference})")
            .ToArray();
        Assert.True(
            notShared.Length == 0,
            $"{host}'s shell holds a different instance from the host's of: {string.Join("; ", notShared)}. Share each with ShareWithShells if a shell can reach the host's state through it, or list it in {nameof(PerShell)} with the reason it is per shell.");
        // The allowlist and the other things this compared are only worth anything if the comparison saw the services it exists for.
        Assert.Contains(typeof(IReconciliationTriggerIngress), compared.Select(service => service.Type));
        Assert.Contains(typeof(IEfSchemaFleet), compared.Select(service => service.Type));
        // The Foundation.Host's record of the shells it could not activate, and the Attention contributor that lists them from inside
        // whichever shell is active, are the host's own instances there (#2202).
        if (host == "Elsa.Foundation.Host")
            Assert.All([typeof(ShellActivationTracker), typeof(IAttentionContributor)], type => Assert.Contains(type, compared.Select(service => service.Type)));
        if (durableMembership)
            Assert.Contains(typeof(EfDataProtectionKeyRepository), compared.Select(service => service.Type));
        // The Extension Builder's singletons are internal to its assembly, so they are named here: the comparison must have seen
        // every one when the switch is on, and none when it is off, which is what makes it optional.
        var extensionBuilderCompared = compared.Select(service => Describe(service.Type)).Where(type => type.StartsWith("Elsa.ExtensionBuilder.", StringComparison.Ordinal)).ToArray();
        if (extensionBuilder)
            Assert.All(ExtensionBuilderHostOwned, type => Assert.Contains(type, extensionBuilderCompared));
        else
            Assert.Empty(extensionBuilderCompared);
    }

    /// <summary>
    /// The key ring is the host's (#2191): a shell of the real composition protects and unprotects with the host's key store,
    /// under the one application name, so a cookie or antiforgery token a shell issues is read by the host and by every other
    /// shell, and, through the shared store, by every other host. Each direction is checked, since a shell that built a key
    /// store of its own, from its own configuration, would still round-trip its own payloads.
    /// </summary>
    [Theory]
    [InlineData("Elsa.Foundation.Host")]
    [InlineData("Elsa.Workbench")]
    public async Task A_shell_of_the_real_composition_protects_with_the_hosts_key_ring(string host)
    {
        using var content = ContentRoot.For(host);
        using var built = BuiltHost.Run(EntryAssembly(host), [.. content.Arguments(durableMembership: false), .. content.KeyStoreArguments()]);
        var root = built.Host.Services;
        // The host is built, not started: its migrator is what creates the key table as it starts.
        await root.GetRequiredService<EfModuleMigrator<DataProtectionKeysDbContext>>().StartAsync(CancellationToken.None);

        var shell = (await root.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ProbeShell)).ServiceProvider;

        foreach (var container in new[] { root, shell })
            Assert.Equal("Elsa", container.GetRequiredService<IOptions<DataProtectionOptions>>().Value.ApplicationDiscriminator);
        var keyStore = root.GetRequiredService<IOptions<KeyManagementOptions>>().Value.XmlRepository;
        Assert.IsType<EfDataProtectionKeyRepository>(keyStore);
        Assert.Same(keyStore, shell.GetRequiredService<IOptions<KeyManagementOptions>>().Value.XmlRepository);
        var atRoot = root.GetRequiredService<IDataProtectionProvider>().CreateProtector(nameof(HostOwnedServicesAreSharedWithShellsTests));
        var inShell = shell.GetRequiredService<IDataProtectionProvider>().CreateProtector(nameof(HostOwnedServicesAreSharedWithShellsTests));
        Assert.Equal("from the shell", atRoot.Unprotect(inShell.Protect("from the shell")));
        Assert.Equal("from the host", inShell.Unprotect(atRoot.Protect("from the host")));
    }

    /// <summary>
    /// Elsa's migration policy for the OpenIddict store is wired in Workbench's own entry point, so deleting that call, or moving it
    /// ahead of the vendor registration, is caught here and nowhere else: the policy's hosted service must be registered, and after
    /// the vendor initializer, which turns its own migration off for it and creates the demo store (#2196).
    /// </summary>
    [Fact]
    public void Workbench_registers_the_openiddict_migration_policy_after_the_vendor_initializer()
    {
        using var content = ContentRoot.For("Elsa.Workbench");
        using var built = BuiltHost.Run(EntryAssembly("Elsa.Workbench"), content.Arguments(durableMembership: false));

        var hosted = built.Host.Services.GetServices<IHostedService>().Select(service => service.GetType()).ToList();

        Assert.Contains(typeof(WorkbenchOpenIddictMigrator), hosted);
        Assert.Contains(typeof(OpenIddictIdentityStoreInitializer), hosted);
        Assert.True(
            hosted.IndexOf(typeof(OpenIddictIdentityStoreInitializer)) < hosted.IndexOf(typeof(WorkbenchOpenIddictMigrator)),
            "The migration policy starts before the vendor initializer, which would then migrate the store itself.");
    }

    /// <summary>
    /// Nuplane calls its observers in the order they were registered, and the Workbench's catalog refresh reads what Nuplane's
    /// auto-loader loaded in the same reconcile, so Workbench's own entry point registers the refresh after the auto-loader.
    /// Registered before it, the refresh would run ahead of the load and rebuild the catalog without the package that arrived.
    /// </summary>
    [Fact]
    public void Workbench_registers_its_catalog_refresh_after_the_package_auto_loader()
    {
        const string AutoLoader = "Nuplane.Loading.PackageAutoLoadingObserver";
        using var content = ContentRoot.For("Elsa.Workbench");
        using var built = BuiltHost.Run(EntryAssembly("Elsa.Workbench"), content.Arguments(durableMembership: false));

        var observers = built.Host.Services.GetServices<INuplaneObserver>().Select(observer => observer.GetType().FullName).ToList();

        Assert.Contains(AutoLoader, observers);
        Assert.Contains(typeof(ShellCatalogRefreshOnPackagesChanged).FullName, observers);
        Assert.True(
            observers.IndexOf(AutoLoader) < observers.IndexOf(typeof(ShellCatalogRefreshOnPackagesChanged).FullName),
            "The catalog refresh is called before the auto-loader, so it refreshes before the new assemblies are loaded.");
    }

    /// <summary>
    /// The OpenIddict store's engine and its prune are wired in Workbench's own entry point too: the engine the default shell's
    /// OpenIddict settings name is the one the store resolves to, and the prune starts after the migration policy, so it prunes a
    /// store that is migrated (#2201).
    /// </summary>
    [Fact]
    public void Workbench_selects_the_openiddict_store_engine_and_prunes_the_store_after_it_is_migrated()
    {
        const string Settings = "--CShells:Shells:default:Features:FoundationIdentityOpenIddict:";
        using var content = ContentRoot.For("Elsa.Workbench");
        string[] arguments =
        [
            .. content.Arguments(durableMembership: false),
            $"{Settings}IsDevelopmentOrDemo", "false",
            $"{Settings}Provider", "PostgreSql",
            "--ConnectionStrings:Elsa", "Host=wiring-test;Database=elsa;Username=elsa;Password=x"
        ];
        using var built = BuiltHost.Run(EntryAssembly("Elsa.Workbench"), arguments);

        var hosted = built.Host.Services.GetServices<IHostedService>().Select(service => service.GetType()).ToList();
        using var scope = built.Host.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<OpenIddictIdentityDbContext>();

        Assert.IsType<OpenIddictIdentityPostgreSqlDbContext>(store);
        Assert.Contains(typeof(WorkbenchOpenIddictPruningService), hosted);
        Assert.True(
            hosted.IndexOf(typeof(WorkbenchOpenIddictMigrator)) < hosted.IndexOf(typeof(WorkbenchOpenIddictPruningService)),
            "The prune starts before the migration policy has migrated the store.");
    }

    /// <summary>The service types the host registered as non-keyed singletons, of an Elsa or Nuplane assembly and closed, which a shell is built with copies of.</summary>
    private static Type[] SingletonServiceTypes(IServiceProvider root) => root.GetRequiredService<IRootServiceCollectionAccessor>().Services
        .Where(descriptor => descriptor is { Lifetime: ServiceLifetime.Singleton, IsKeyedService: false } && !descriptor.ServiceType.ContainsGenericParameters)
        .Select(descriptor => descriptor.ServiceType)
        .Where(type => type.Assembly.GetName().Name is { } assembly && (assembly.StartsWith("Elsa.", StringComparison.Ordinal) || assembly == "Nuplane" || assembly.StartsWith("Nuplane.", StringComparison.Ordinal)))
        .Distinct()
        .OrderBy(Describe, StringComparer.Ordinal)
        .ToArray();

    /// <summary>How a shell's instances of a service differ from the root's, or <see langword="null"/> when they are the same objects or either container cannot resolve it.</summary>
    private static string? Difference(Type type, IServiceProvider root, IServiceProvider shell)
    {
        if (Resolve(root, type) is not { } atRoot || Resolve(shell, type) is not { } inShell)
        {
            return null;
        }

        if (atRoot.Count != inShell.Count)
            return $"{atRoot.Count} registered at the root, {inShell.Count} in the shell";

        return atRoot.Zip(inShell).Any(pair => !ReferenceEquals(pair.First, pair.Second)) ? "a second instance in the shell" : null;
    }

    private static IReadOnlyList<object>? Resolve(IServiceProvider services, Type type)
    {
        try
        {
            return [.. (IEnumerable<object>)services.GetRequiredService(typeof(IEnumerable<>).MakeGenericType(type))];
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    private static string Describe(Type type) => type.IsGenericType
        ? $"{type.Namespace}.{type.Name[..type.Name.IndexOf('`')]}<{string.Join(", ", type.GetGenericArguments().Select(Describe))}>"
        : $"{type.Namespace}.{type.Name}";

    private static Assembly EntryAssembly(string host) => host == "Elsa.Workbench"
        ? typeof(WorkbenchOpenIddictMigrator).Assembly
        : typeof(ModuleManagementOptions).Assembly;

    /// <summary>A content root of the host's own settings and a shell file of its own, so the shell that is activated enables no feature.</summary>
    private sealed class ContentRoot : IDisposable
    {
        private readonly string _directory = Directory.CreateTempSubdirectory("elsa-host-owned-services-").FullName;

        public static ContentRoot For(string host)
        {
            var content = new ContentRoot();
            var source = Path.Join(RepositoryRoot(), "src", "apps", host);
            foreach (var file in new[] { "appsettings.json", "appsettings.Development.json" })
                File.Copy(Path.Join(source, file), Path.Join(content._directory, file));
            Directory.CreateDirectory(Path.Join(content._directory, "packages"));
            File.WriteAllText(Path.Join(content._directory, "shells.json"), $$"""{ "CShells": { "Shells": { "{{ProbeShell}}": { "Name": "{{ProbeShell}}", "Features": {} } } } }""");
            return content;
        }

        public string[] Arguments(bool durableMembership)
        {
            List<string> arguments =
            [
                "--contentRoot", _directory,
                "--environment", "Development",
                "--urls", "http://127.0.0.1:0",
                "--Nuplane:Setup:StateFilePath", Path.Join(_directory, ".nuplane", "store-state.json"),
                // The Foundation.Host maps and composes its module-management operations only when this is on; Workbench always does.
                "--Elsa:ModuleManagement:Enabled", "true",
                "--Elsa:ModuleManagement:ApiKey", "guard-test-key"
            ];
            if (durableMembership)
                arguments.AddRange(
                [
                    "--Elsa:Cluster:Membership:HostId", "guard-test-host",
                    "--Elsa:Cluster:Membership:EntityFrameworkCore:Enabled", "true",
                    "--Elsa:Cluster:Membership:EntityFrameworkCore:Provider", "Sqlite",
                    "--Elsa:Cluster:Membership:EntityFrameworkCore:ConnectionString", $"Data Source={Path.Join(_directory, "membership.db")};Pooling=False",
                    .. KeyStoreArguments()
                ]);

            return [.. arguments];
        }

        /// <summary>The Data Protection key store a clustered host shares its key ring through (#2191).</summary>
        public string[] KeyStoreArguments() =>
        [
            "--Elsa:DataProtection:EntityFrameworkCore:Enabled", "true",
            "--Elsa:DataProtection:EntityFrameworkCore:Provider", "Sqlite",
            "--Elsa:DataProtection:EntityFrameworkCore:ConnectionString", $"Data Source={Path.Join(_directory, "keys.db")};Pooling=False"
        ];

        public void Dispose()
        {
            try
            {
                Directory.Delete(_directory, recursive: true);
            }
            catch (IOException)
            {
                // A temp directory left behind is harmless.
            }
        }

        private static string RepositoryRoot()
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Join(directory.FullName, "Elsa.Server.slnx")))
                    return directory.FullName;
            }

            throw new InvalidOperationException($"No Elsa.Server.slnx above {AppContext.BaseDirectory}.");
        }
    }

    /// <summary>
    /// A host's entry point run as far as its host being built, the way the host-factory of the test host runs one: the host
    /// announces itself on <c>Microsoft.Extensions.Hosting</c>'s diagnostic listener as it is built, and the entry point is
    /// stopped there with <see cref="HostAbortedException"/>, before anything is started. Only this call's own execution flow is
    /// captured, so a host another test builds in parallel is left alone.
    /// </summary>
    private sealed class BuiltHost : IDisposable
    {
        private static readonly AsyncLocal<Capture?> Current = new();

        private BuiltHost(IHost host) => Host = host;

        public IHost Host { get; }

        public static BuiltHost Run(Assembly assembly, string[] arguments)
        {
            using var capture = new Capture();
            Current.Value = capture;
            using var subscription = DiagnosticListener.AllListeners.Subscribe(capture);
            try
            {
                assembly.EntryPoint!.Invoke(null, [arguments]);
            }
            catch (TargetInvocationException exception) when (exception.InnerException is HostAbortedException)
            {
                // The abort is how the capture stops the entry point once the host is built.
            }
            finally
            {
                Current.Value = null;
            }

            return new BuiltHost(capture.Host ?? throw new InvalidOperationException($"{assembly.GetName().Name} did not build a host."));
        }

        public void Dispose() => Host.Dispose();

        private sealed class Capture : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
        {
            private readonly List<IDisposable> _subscriptions = [];

            public IHost? Host { get; private set; }

            public void OnNext(DiagnosticListener listener)
            {
                if (listener.Name == "Microsoft.Extensions.Hosting")
                    lock (_subscriptions)
                        _subscriptions.Add(listener.Subscribe(this));
            }

            /// <summary>Ends the subscriptions made to the hosting listener, which the subscription to <see cref="DiagnosticListener.AllListeners"/> does not end.</summary>
            public void Dispose()
            {
                lock (_subscriptions)
                {
                    foreach (var subscription in _subscriptions)
                        subscription.Dispose();
                    _subscriptions.Clear();
                }
            }

            public void OnNext(KeyValuePair<string, object?> value)
            {
                if (value is { Key: "HostBuilt", Value: IHost host } && ReferenceEquals(Current.Value, this))
                {
                    Host = host;
                    throw new HostAbortedException();
                }
            }

            public void OnCompleted()
            {
            }

            public void OnError(Exception error)
            {
            }
        }
    }
}
