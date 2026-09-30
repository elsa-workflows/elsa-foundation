using System.Diagnostics;
using System.Reflection;
using CShells.Features;
using CShells.Lifecycle;
using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Models;
using Elsa.Foundation.Host.ModuleManagement;
using Elsa.Persistence.Schema;
using Elsa.Persistence.Schema.SchemaFinalization;
using Elsa.Workbench;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Nuplane.Admin;
using Nuplane.Reconciliation;
using Xunit;

namespace Elsa.Modularity.Tests;

/// <summary>
/// CShells copies every root registration into every shell container, and a registration made by type or by factory is
/// instantiated again there. A service that holds the host's own state (the trigger queue only the host's dispatcher reads,
/// the membership the host publishes through, the fleet the finalization gates count) is wrong in a shell: the shell's copy
/// waits for a dispatcher that does not exist, or counts a fleet of its own (#2159). This builds the real
/// <c>Elsa.Foundation.Host</c> and <c>Elsa.Workbench</c> compositions by running their entry points up to the built host, activates a
/// shell through CShells, and requires every host-owned stateful service present in either host to be the same instance in
/// the shell as at the root.
/// </summary>
public sealed class HostOwnedServicesAreSharedWithShellsTests
{
    private const string ProbeShell = "probe";

    /// <summary>The services that hold the host's own state, by the type a consumer resolves them under.</summary>
    private static readonly Type[] HostOwned =
    [
        typeof(INuplaneAdminOperations),
        typeof(ManualReconcileCoordinator),
        typeof(IReconciliationTriggerIngress),
        typeof(ISupersededAssemblySource),
        typeof(IEfSchemaFleet),
        typeof(IClusterMembership),
        typeof(EfSchemaFinalizationObservations),
        typeof(IShellRegistry),
        typeof(IRuntimeFeatureCatalog)
    ];

    /// <summary>Each host, on the in-process membership it is a cluster of one with, and on the durable EF membership that joins a cluster.</summary>
    public static TheoryData<string, bool> Hosts => new()
    {
        { "Elsa.Foundation.Host", false },
        { "Elsa.Foundation.Host", true },
        { "Elsa.Workbench", false },
        { "Elsa.Workbench", true }
    };

    [Theory]
    [MemberData(nameof(Hosts))]
    public async Task A_shell_of_the_real_composition_resolves_the_hosts_own_stateful_services(string host, bool durableMembership)
    {
        using var content = ContentRoot.For(host);
        using var built = BuiltHost.Run(EntryAssembly(host), content.Arguments(durableMembership));
        var root = built.Host.Services;
        var registry = root.GetRequiredService<IShellRegistry>();

        var shell = await registry.GetOrActivateAsync(ProbeShell);

        var notShared = HostOwned
            .Select(type => (Type: type, Root: root.GetService(type), Shell: shell.ServiceProvider.GetService(type)))
            .Where(service => service.Root is null || !ReferenceEquals(service.Root, service.Shell))
            .Select(service => $"{service.Type.Name} ({(service.Root is null ? "not composed by the host" : service.Shell is null ? "absent from the shell" : "a second instance in the shell")})")
            .ToArray();
        Assert.True(notShared.Length == 0, $"{host}'s shell does not share the host's own instance of: {string.Join(", ", notShared)}.");
        Assert.Equal(durableMembership ? ClusterProviderKind.Durable : ClusterProviderKind.InProcess, root.GetRequiredService<IClusterMembership>().ProviderKind);
    }

    private static Assembly EntryAssembly(string host) => host == "Elsa.Workbench"
        ? typeof(ManagementApiKeyAuthentication).Assembly
        : typeof(ModuleManagementOptions).Assembly;

    /// <summary>A content root of the host's own settings and a shell file of its own, so the shell that is activated enables no feature.</summary>
    private sealed class ContentRoot : IDisposable
    {
        private readonly string _directory = Directory.CreateTempSubdirectory("elsa-host-owned-services-").FullName;

        public static ContentRoot For(string host)
        {
            var content = new ContentRoot();
            var source = Path.Combine(RepositoryRoot(), "src", "apps", host);
            foreach (var file in new[] { "appsettings.json", "appsettings.Development.json" })
                File.Copy(Path.Combine(source, file), Path.Combine(content._directory, file));
            Directory.CreateDirectory(Path.Combine(content._directory, "packages"));
            File.WriteAllText(Path.Combine(content._directory, "shells.json"), $$"""{ "CShells": { "Shells": { "{{ProbeShell}}": { "Name": "{{ProbeShell}}", "Features": {} } } } }""");
            return content;
        }

        public string[] Arguments(bool durableMembership)
        {
            List<string> arguments =
            [
                "--contentRoot", _directory,
                "--environment", "Development",
                "--urls", "http://127.0.0.1:0",
                "--Nuplane:Setup:StateFilePath", Path.Combine(_directory, ".nuplane", "store-state.json"),
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
                    "--Elsa:Cluster:Membership:EntityFrameworkCore:ConnectionString", $"Data Source={Path.Combine(_directory, "membership.db")};Pooling=False"
                ]);

            return [.. arguments];
        }

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
                if (File.Exists(Path.Combine(directory.FullName, "Elsa.Server.slnx")))
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
            var capture = new Capture();
            Current.Value = capture;
            using var subscription = DiagnosticListener.AllListeners.Subscribe(capture);
            try
            {
                assembly.EntryPoint!.Invoke(null, [arguments]);
            }
            catch (TargetInvocationException exception) when (exception.InnerException is HostAbortedException)
            {
            }
            finally
            {
                Current.Value = null;
            }

            return new BuiltHost(capture.Host ?? throw new InvalidOperationException($"{assembly.GetName().Name} did not build a host."));
        }

        public void Dispose() => Host.Dispose();

        private sealed class Capture : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>
        {
            public IHost? Host { get; private set; }

            public void OnNext(DiagnosticListener listener)
            {
                if (listener.Name == "Microsoft.Extensions.Hosting")
                    listener.Subscribe(this);
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
