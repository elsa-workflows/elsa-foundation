using System.IO.Compression;
using System.Text;
using System.Text.Json;
using CShells.DependencyInjection;
using CShells.Nuplane;
using CShells.Lifecycle;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nuplane;
using Nuplane.Abstractions.PackageStoreProtection;
using Nuplane.Admin;
using Nuplane.Feeds.Configuration;
using Nuplane.Loading;
using Nuplane.Loading.Hosting.Builder;
using Nuplane.Reconciliation;
using Nuplane.Reconciliation.Configuration;
using Nuplane.Reconciliation.LockFile;
using Nuplane.Sources.Directory.Builder;

const string packageId = "Acme.StudioReuse";
var root = Path.Combine(Path.GetTempPath(), "studio-store-probe-" + Guid.NewGuid().ToString("N"));
var feed = Path.Combine(root, "feed");
var installs = Path.Combine(root, "installs");
var state = Path.Combine(root, "state.json");
Directory.CreateDirectory(feed);
Directory.CreateDirectory(installs);
var groups = new List<string>();
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
var token = timeout.Token;
try
{
    var legacyRoot = Path.Combine(root, "legacy");
    Directory.CreateDirectory(legacyRoot);
    var sentinel = Path.Combine(legacyRoot, "preserved.txt");
    await File.WriteAllTextAsync(sentinel, "legacy bytes", token);
    var ordinary = Host.CreateApplicationBuilder();
    ordinary.Logging.ClearProviders();
    ordinary.Services.AddNuplane(nuplane => nuplane.WithStateFile(Path.Combine(root, "legacy-state.json")));
    ordinary.Services.Configure<FeedResolutionOptions>(options => options.PackageInstallRoot = legacyRoot);
    ordinary.Services.AddNuplanePackageStoreMaintenance();
    using (var legacy = ordinary.Build())
    {
        var refused = await legacy.Services.GetRequiredService<INuplanePackageStoreMaintenanceOperations>()
            .PruneAsync(new(Retention: new(0)), token);
        Check(refused.RootIdentity is null && refused.EnrollmentEpoch is null && !refused.InventoryComplete && refused.ProtectionKnowledge == "Unknown" && refused.Diagnostics.Any(x => x.Code == "EnrollmentRequired"), "Ordinary preview claimed eligible maintenance: " + JsonSerializer.Serialize(refused));
        var rejected = false;
        try
        {
            await legacy.Services.GetRequiredService<INuplanePackageStoreMaintenanceOperations>()
                .PruneAsync(new(Retention: new(0), Mode: PackageStorePruneMode.Execute), token);
        }
        catch (ArgumentException exception) when (exception.ParamName == "request") { rejected = true; }
        Check(rejected, "Execute without exact confirmation was not rejected.");
        Check(await File.ReadAllTextAsync(sentinel, token) == "legacy bytes", "Refused maintenance changed legacy content.");
        Console.WriteLine("ordinary=" + JsonSerializer.Serialize(refused.Diagnostics));
    }
    groups.Add("ordinary nonparticipating composition refuses prune and preserves owned legacy content");
    string obsoletePath;
    using (var installer = CreateBuilder().Build())
    {
        var operations = installer.Services.GetRequiredService<INuplanePackageStoreMaintenanceOperations>();
        var observed = await operations.PreviewEnrollmentAsync(cancellationToken: token);
        Check(observed.Membership == "Unenrolled" && observed.RootIdentity is not null, "Expected owned, unenrolled root.");
        var enrolled = await operations.EnrollAsync(new("default", observed.RootIdentity!, 1, true, ["studio"], []), token);
        Check(enrolled.Status == "Complete", "Enrollment failed: " + JsonSerializer.Serialize(enrolled));
        await installer.StartAsync(token);
        Pack("0.5.0", null, "obsolete");
        var installed = await installer.Services.GetRequiredService<ManualReconcileCoordinator>().TriggerAsync("content-install", token);
        Check(installed.RunResult is { IsDegraded: false }, "Content-only installation failed.");
        obsoletePath = (await installer.Services.GetRequiredService<INuplaneAdminOperations>().GetPackagesAsync(token)).Packages.Single().InstallPath;
        await installer.StopAsync(token);
    }
    File.Delete(Path.Combine(feed, packageId + ".0.5.0.nupkg"));
    var builder = CreateBuilder();
    builder.Services.AddNuplane(nuplane => nuplane.AutoloadPackages(loading => loading.WithDefaultLoadMode(PackageLoadMode.Collectible)
        .WithDeactivationTimeout(TimeSpan.FromMilliseconds(100))
        .PackageLoadMode(packageId, PackageLoadMode.Collectible)
        .SharedAssembly("CShells.Abstractions", null, typeof(CShells.Features.IShellFeature).Assembly.GetName().Version!.Major)
        .SharedAssembly("Microsoft.Extensions.DependencyInjection.Abstractions", "adb9793829ddae60", 10)));
    builder.Services.AddCShells(shells => shells.WithNuplaneFeatureDiscovery(options =>
    {
        options.RefreshTrigger = NuplaneRefreshTrigger.ChangedOrPending;
        options.AutoReload = true;
    }).AddShell("Default", shell => shell.WithFeatures("StudioReuseProbe")));
    using var host = builder.Build();
    var maintenance = host.Services.GetRequiredService<INuplanePackageStoreMaintenanceOperations>();
    var enrollment = await maintenance.PreviewEnrollmentAsync(cancellationToken: token);
    Check(enrollment.Membership == "Complete", "Enrolled store failed to reopen.");
    groups.Add("real public enrollment completes; a content-only installer closes before the Studio composition opens");
    await host.StartAsync(token);
    var admin = host.Services.GetRequiredService<INuplaneAdminOperations>();
    var registry = host.Services.GetRequiredService<IShellRegistry>();
    Pack("1.0.0", "First", "first");
    await Reconcile();
    var first = await registry.GetOrActivateAsync("Default", token);
    await using var retained = first.BeginScope();
    Check(retained.ServiceProvider.GetRequiredService<string>() == "first", "First shell feature was not loaded.");
    var firstPath = (await admin.GetPackagesAsync(token)).Packages.Single().InstallPath;
    var activePlan = await Prune();
    Check(activePlan.Candidates.Any(x => x.Install.Version == "1.0.0" && x.PhysicalOutcome == "Retained"), "Active package was not retained.");
    Console.WriteLine(JsonSerializer.Serialize(activePlan.Candidates.Select(x => new { x.Install.Version, x.Classification, x.PhysicalOutcome, x.Reasons })));
    Check(activePlan.Candidates.Any(x => x.Install.Version == "0.5.0" && x.PhysicalOutcome == "Deleted") && !Directory.Exists(obsoletePath), "Obsolete content-only install was not physically deleted.");
    groups.Add("real directory-feed install supplies a serving shell; active package survives while obsolete content-only install is deleted");

    File.Delete(Path.Combine(feed, packageId + ".1.0.0.nupkg"));
    Pack("2.0.0", "Second", "second");
    await Reconcile();
    await using (var scope = (registry.GetActive("Default") ?? throw new Exception("Missing shell")).BeginScope())
        Check(scope.ServiceProvider.GetRequiredService<string>() == "second", "Updated shell feature was not loaded.");
    Check(retained.ServiceProvider.GetRequiredService<string>() == "first", "Retained scope broke after update.");
    var retainedPlan = await Prune();
    Console.WriteLine(JsonSerializer.Serialize(retainedPlan.Candidates.Select(x => new { x.Install.Version, x.Classification, x.PhysicalOutcome, x.Reasons })));
    Check(retainedPlan.Candidates.Any(x => x.Install.Version == "1.0.0" && x.PhysicalOutcome == "Retained" && x.Reasons.SequenceEqual(["ProtectedLiveUse"])), "Old generation lost protection.");
    Check(Directory.Exists(firstPath), "Retained generation install disappeared.");
    groups.Add("update replaces the serving shell while retained-generation use protects the first install");

    var secondPath = (await admin.GetPackagesAsync(token)).Packages.Single().InstallPath;
    var admission = host.Services.GetRequiredService<IPackageStoreAdmission>();
    var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var webBuilder = WebApplication.CreateBuilder();
    webBuilder.Logging.ClearProviders();
    webBuilder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
    await using var web = webBuilder.Build();
    web.MapGet("/asset", async (HttpContext context) =>
    {
        byte[] bytes;
        await using (var admitted = await admission.AcquireForInstallPathsAsync([secondPath], PackageStoreAdmissionKind.Loading, context.RequestAborted))
        {
            using var borrow = admitted.BorrowFor(secondPath);
            entered.TrySetResult();
            await release.Task.WaitAsync(context.RequestAborted);
            if (args.Contains("--release-reader-before-read", StringComparer.Ordinal)) borrow.Dispose();
            bytes = PackageContent.TryReadFile(secondPath, "staticwebassets/probe.txt", borrow) ?? throw new Exception("Admitted asset read failed.");
        }
        await Results.Bytes(bytes, "text/plain", enableRangeProcessing: true).ExecuteAsync(context);
    });
    await web.StartAsync(token);
    var address = web.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
    using var client = new HttpClient { BaseAddress = new(address) };
    using var request = new HttpRequestMessage(HttpMethod.Get, "/asset");
    request.Headers.Range = new(0, 2);
    var servingBeforeRequest = registry.GetActive("Default");
    var stateBeforeRequest = await File.ReadAllBytesAsync(state, token);
    var responseTask = client.SendAsync(request, token);
    try
    {
        await entered.Task.WaitAsync(token);
        File.Delete(Path.Combine(feed, packageId + ".2.0.0.nupkg"));
        var overlap = await host.Services.GetRequiredService<ManualReconcileCoordinator>().TriggerAsync("asset-read-overlap", token);
        Console.WriteLine("overlap=" + JsonSerializer.Serialize(overlap));
        Check(overlap.OutcomeCode == ManualReconcileOutcomeCode.Unavailable && overlap.RunResult is null && overlap.ReasonCode?.Contains("root.lock", StringComparison.Ordinal) == true, "Overlap did not refuse the busy coordinated root.");
        var stateDuringRequest = await File.ReadAllBytesAsync(state, token);
        Check(stateBeforeRequest.SequenceEqual(stateDuringRequest), "Busy reconciliation changed persisted state.");
        Check(ReferenceEquals(servingBeforeRequest, registry.GetActive("Default")), "Busy reconciliation replaced the serving shell.");
    }
    finally { release.TrySetResult(); }
    using var response = await responseTask;
    var detached = await response.Content.ReadAsByteArrayAsync(token);
    Check(response.StatusCode == System.Net.HttpStatusCode.PartialContent && Encoding.UTF8.GetString(detached) == "sec", "Admitted HTTP range response failed.");
    await web.StopAsync(token);
    groups.Add("actual HTTP range request owns an admitted read; overlapping reconciliation refuses the busy root without changing state or serving shell");
    await Reconcile();
    await using (var scope = (registry.GetActive("Default") ?? throw new Exception("Missing empty shell")).BeginScope())
        Check(scope.ServiceProvider.GetService<string>() is null, "Removal left the feature serving.");
    var emptyPlan = await Prune();
    Check(emptyPlan.Candidates.Any(x => x.Install.Version == "1.0.0" && x.PhysicalOutcome == "Retained" && x.Reasons.Contains("ProtectedLiveUse")), "Removal lost retained package protection.");
    Check((await admin.GetPackagesAsync(token)).Packages.Count == 0, "Removal did not empty active packages.");
    Check(Encoding.UTF8.GetString(detached) == "sec", "Detached asset payload changed after removal.");
    Check(retained.ServiceProvider.GetRequiredService<string>() == "first", "Removal broke retained scope.");
    groups.Add("removal-to-empty replaces serving registrations; confirmed prune retains old reader protection; detached asset bytes and retained scope remain usable");
    await host.StopAsync(token);
    Console.WriteLine(JsonSerializer.Serialize(new { status = "passed", runtime = Environment.Version.ToString(), nuplaneSource = typeof(PackageContent).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false).Cast<System.Reflection.AssemblyInformationalVersionAttribute>().Single().InformationalVersion, groups }, new JsonSerializerOptions { WriteIndented = true }));
    return 0;

    async Task Reconcile()
    {
        var outcome = await host.Services.GetRequiredService<ManualReconcileCoordinator>().TriggerAsync("studio-pruning-probe", token);
        Check(outcome.OutcomeCode == ManualReconcileOutcomeCode.Completed && outcome.RunResult is { IsDegraded: false }, "Reconcile failed: " + JsonSerializer.Serialize(outcome));
    }
    async Task<PackageStoreMaintenanceResult> Prune()
    {
        var retention = new PackageStoreRetentionPolicy(0);
        var before = await File.ReadAllBytesAsync(state, token);
        // Compare the actual state file for nonmutation; do not reenter Nuplane admission under a held owner.
        var preview = await maintenance.PruneAsync(new(Retention: retention), token);
        Check(preview.Status == "Preview" && preview.InventoryComplete && preview.EnrollmentEpoch is not null && preview.RootIdentity is not null,
            "Preview refused: " + JsonSerializer.Serialize(preview));
        var afterPreview = await File.ReadAllBytesAsync(state, token);
        Check(before.SequenceEqual(afterPreview), "Preview changed the member state.");
        Check(preview.Candidates.All(x => x.Attempt == "NotAttempted" && x.PhysicalOutcome is null), "Preview attempted physical work.");
        var result = await maintenance.PruneAsync(new(preview.RootLabel, retention, PackageStorePruneMode.Execute,
            new(preview.RootLabel, preview.RootIdentity!, preview.EnrollmentEpoch!.Value, retention)), token);
        Check(result.Status == "Completed", "Prune failed: " + JsonSerializer.Serialize(result));
        return result;
    }
}
catch (Exception exception)
{
    Console.WriteLine(JsonSerializer.Serialize(new { status = "failed", groups, error = exception.ToString(), root }, new JsonSerializerOptions { WriteIndented = true }));
    return 1;
}
finally { Directory.Delete(root, recursive: true); }

HostApplicationBuilder CreateBuilder()
{
    var builder = Host.CreateApplicationBuilder();
    builder.Logging.ClearProviders();
    builder.Services.AddNuplane(nuplane =>
    {
        nuplane.WithStateFile(state).UseCoordinatedPackageStore([new("default", "studio", state)], true);
        nuplane.AddDirectoryFeed("fixture", feed, directory => { directory.Watch = false; directory.IncludeAll(); });
    });
    builder.Services.Configure<FeedResolutionOptions>(options => options.PackageInstallRoot = installs);
    builder.Services.Configure<ReconciliationOptions>(options => options.EnableAutomaticReconciliation = false);
    builder.Services.Configure<LockFileOptions>(options => options.Path = Path.Combine(root, "nuplane.lock.json"));
    builder.Services.AddNuplaneAdmin();
    builder.Services.AddNuplanePackageStoreMaintenance();
    return builder;
}

void Pack(string version, string? fixture, string payload)
{
    using var archive = ZipFile.Open(Path.Combine(feed, packageId + "." + version + ".nupkg"), ZipArchiveMode.Create);
    using (var writer = new StreamWriter(archive.CreateEntry(packageId + ".nuspec").Open()))
        writer.Write($"<package><metadata><id>{packageId}</id><version>{version}</version><authors>Studio qualification</authors><description>Owned qualification fixture</description></metadata></package>");
    if (fixture is not null) archive.CreateEntryFromFile(Path.Combine(AppContext.BaseDirectory, fixture + ".dll"), "lib/net10.0/" + fixture + ".dll");
    using var asset = new StreamWriter(archive.CreateEntry("staticwebassets/probe.txt").Open());
    asset.Write(payload);
}
static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
