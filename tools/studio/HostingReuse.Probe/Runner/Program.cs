using System.Reflection;
using System.Text.Json;
using CShells.DependencyInjection;
using CShells.Features;
using CShells.Lifecycle;
using CShells.Nuplane;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nuplane.Abstractions;
using Nuplane.Loading;

var events = new List<string>();
var automaticReload = !args.Contains("--disable-auto-reload", StringComparer.Ordinal);
var catalog = new ScriptedPackageCatalog(events);
var builder = Host.CreateApplicationBuilder();
builder.Logging.ClearProviders();
builder.Services.AddSingleton<IPackageAssemblyCatalog>(catalog);
builder.Services.AddCShells(shells => shells
    .WithNuplaneFeatureDiscovery(options =>
    {
        options.RefreshTrigger = NuplaneRefreshTrigger.ChangedOrPending;
        options.AutoReload = automaticReload;
        options.OnReloadResults = (results, _) =>
        {
            Check(results.All(result => result.Error is null), "Automatic reload returned a shell error.");
            events.Add("reload");
            return ValueTask.CompletedTask;
        };
    })
    .AddShell("Default", shell => shell.WithFeatures("StudioReuseProbe")));

using var host = builder.Build();
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
var token = timeout.Token;
var services = host.Services;
var registry = services.GetRequiredService<IShellRegistry>();
var runtimeCatalog = services.GetRequiredService<IRuntimeFeatureCatalog>();
// CShells.Nuplane 0.0.31 identifies the lifecycle observer through this public participant contract.
// Requalify this lookup when reshaping fixtures or upgrading the adapter; do not copy its coordinator.
var observer = services.GetServices<INuplaneObserver>().Single(value => value is IShellGenerationBuildParticipant);
var completed = new List<string>();

try
{
    catalog.Select(typeof(StudioReuse.First.FirstFeature).Assembly, "1.0.0");
    await observer.OnPackagesReconciledAsync(Change(added: true), [Resolved("1.0.0")], token);
    Check(catalog.Reads == 0, "Cold completion unexpectedly scanned the package catalog.");
    Check(events.Count == 0, "Cold completion unexpectedly reloaded shells.");

    var first = await registry.GetOrActivateAsync("Default", token);
    await using var retainedScope = first.BeginScope();
    Check(retainedScope.ServiceProvider.GetRequiredService<string>() == "first", "Cold shell did not discover the first fixture.");
    Check((await runtimeCatalog.GetSnapshotAsync(token)).FeatureDescriptors.Any(feature => feature.Id == "StudioReuseProbe"), "Feature absent from runtime catalog.");
    completed.Add("cold completion defers discovery; first shell discovers the loaded package feature");

    catalog.Select(typeof(StudioReuse.Second.SecondFeature).Assembly, "2.0.0");
    events.Clear();
    await observer.OnPackagesReconciledAsync(Change(updated: true), [Resolved("2.0.0")], token);
    var second = registry.GetActive("Default") ?? throw new InvalidOperationException("Default shell became unavailable.");
    Check(!ReferenceEquals(first, second), "An update did not replace the serving shell generation.");
    await using (var scope = second.BeginScope())
        Check(scope.ServiceProvider.GetRequiredService<string>() == "second", "Updated generation did not use the second assembly.");
    Check(events.IndexOf("catalog:2.0.0") >= 0 && events.IndexOf("catalog:2.0.0") < events.IndexOf("reload"), "Feature discovery did not precede reload completion.");
    Check(retainedScope.ServiceProvider.GetRequiredService<string>() == "first", "An update broke a retained old-generation scope.");
    completed.Add("update refreshes discovery before reload; new generation uses the second assembly; retained scope remains usable");

    catalog.Clear();
    events.Clear();
    await observer.OnPackagesReconciledAsync(Change(removed: true), [], token);
    var empty = registry.GetActive("Default") ?? throw new InvalidOperationException("Removal lost the default shell.");
    Check(!ReferenceEquals(second, empty), "Removal-to-empty did not replace the serving generation.");
    await using (var scope = empty.BeginScope())
        Check(scope.ServiceProvider.GetService<string>() is null, "Removed feature remained registered in the replacement shell.");
    Check(!(await runtimeCatalog.GetSnapshotAsync(token)).FeatureDescriptors.Any(feature => feature.Id == "StudioReuseProbe"), "Removed feature remained discoverable.");
    Check(events.IndexOf("catalog:empty") >= 0 && events.IndexOf("catalog:empty") < events.IndexOf("reload"), "Removal did not refresh before reload completion.");
    Check(retainedScope.ServiceProvider.GetRequiredService<string>() == "first", "Removal broke a retained old-generation scope.");
    completed.Add("removal-to-empty removes discovery and serving registrations while a retained generation remains usable");

    events.Clear();
    await observer.OnPackagesReconciledAsync(Change(), [], token);
    Check(events.Count == 0, "An unchanged empty completion unexpectedly refreshed or reloaded.");
    completed.Add("unchanged empty completion does no work");

    Console.WriteLine(JsonSerializer.Serialize(new { status = "passed", automaticReload, cshellsAssembly = VersionOf(typeof(CShellsNuplaneBuilderExtensions)), nuplaneAssembly = VersionOf(typeof(INuplaneObserver)), completed }, new JsonSerializerOptions { WriteIndented = true }));
    return 0;
}
catch (Exception exception)
{
    Console.WriteLine(JsonSerializer.Serialize(new { status = "failed", automaticReload, completed, error = exception.Message }, new JsonSerializerOptions { WriteIndented = true }));
    return 1;
}

static string? VersionOf(Type type) => type.Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
static ResolvedPackage Resolved(string version) => new("Acme.StudioReuse", version, "scripted", "/probe/not-an-install", DateTimeOffset.UnixEpoch, "scripted");
static PackageChangeSet Change(bool added = false, bool updated = false, bool removed = false) => new(
    added ? [Resolved("1.0.0")] : [], updated ? [Resolved("2.0.0")] : [], removed ? ["Acme.StudioReuse"] : [], Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow);

internal sealed class ScriptedPackageCatalog(List<string> events) : IPackageAssemblyCatalog
{
    private PackageAssemblies[] packages = [];
    public int Reads { get; private set; }

    public void Select(Assembly assembly, string version) => packages = [new("Acme.StudioReuse", version, [assembly], [], LoadMode: default, FrameworkIntegrationSafe: true)];
    public void Clear() => packages = [];

    public Task<IReadOnlyList<PackageAssemblies>> GetPackagedAssembliesAsync(CancellationToken cancellationToken = default)
    {
        Reads++;
        events.Add("catalog:" + (packages.FirstOrDefault()?.Version ?? "empty"));
        return Task.FromResult<IReadOnlyList<PackageAssemblies>>(packages);
    }

    public Task<PackageAssemblies?> GetPackagedAssembliesAsync(string packageId, CancellationToken cancellationToken = default) =>
        Task.FromResult(packages.SingleOrDefault(package => package.PackageId == packageId));
}
