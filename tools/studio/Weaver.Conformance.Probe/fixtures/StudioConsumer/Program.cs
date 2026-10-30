using System.Runtime.CompilerServices;
using CShells.DependencyInjection;
using CShells.Lifecycle;
using CShells.Nuplane;
using Elsa.Studio.Api.Authorization;
using Elsa.Studio.Dashboard;
using Elsa.Studio.Hosting;
using Elsa.Studio.Hosting.Nuplane;
using Elsa.Studio.Weaver.Workflows;
using Elsa.Studio.Workflows;
using Nuplane.Admin;
using Nuplane.Builder;
using Nuplane.Feeds.Configuration;
using Nuplane.Reconciliation.Configuration;
using Nuplane.Reconciliation.LockFile;
using Nuplane.Sources.Directory.Builder;

const string shellName = "Authoring";
var builder = WebApplication.CreateBuilder(args);
var contentRoot = builder.Environment.ContentRootPath;
var state = Path.Combine(contentRoot, "native-store", "state.json");
Directory.CreateDirectory(Path.GetDirectoryName(state)!);
var studio = builder.AddElsaStudio(options => options.ShellName = shellName)
    .ConfigureShells(shells => shells.WithAssemblies(typeof(DashboardStudioFeature).Assembly,
        typeof(WorkflowsStudioFeature).Assembly, typeof(WeaverWorkflowsStudioFeature).Assembly));
studio.AddElsaStudioNuplane(options => options.DirectoryFeedName = "weaver-studio", configureNuplane: nuplane => {
    nuplane.WithStateFile(state).UseCoordinatedPackageStore([new("default", "studio", state)], true);
    nuplane.Services.Configure<FeedResolutionOptions>(options => options.PackageInstallRoot = Path.Combine(contentRoot, "native-store", "installs"));
    nuplane.Services.Configure<LockFileOptions>(options => options.Path = Path.Combine(contentRoot, "native-store", "nuplane.lock.json"));
    nuplane.Services.Configure<ReconciliationOptions>(options => options.EnableAutomaticReconciliation = false);
    nuplane.AddDirectoryFeed("weaver-studio", "runtime-feed", feed => { feed.Watch = false; feed.IncludeAll(); });
});
studio.Services.PostConfigure<NuplaneIntegrationOptions>(integration => {
    integration.Enabled = true;
    integration.RefreshTrigger = NuplaneRefreshTrigger.ChangedOrPending;
    integration.AutoReload = true;
});
await using var app = builder.Build();
var maintenance = app.Services.GetRequiredService<INuplanePackageStoreMaintenanceOperations>();
var preview = await maintenance.PreviewEnrollmentAsync();
if (preview.Membership == "Unenrolled") {
    if (!builder.Configuration.GetValue<bool>("Qualification:EnrollOwnedStore")) throw new InvalidOperationException("Explicit owned Studio store enrollment is required.");
    var enrollment = await maintenance.EnrollAsync(new("default", preview.RootIdentity!, 1, true, ["studio"], []));
    if (enrollment.Status != "Complete") throw new InvalidOperationException("Owned Studio enrollment failed: " + enrollment.Status);
} else if (preview.Membership != "Complete") throw new InvalidOperationException("Studio native membership is not complete.");
app.UseElsaStudio();
app.MapElsaStudio();
app.MapElsaStudioNuplane();
var registry = app.Services.GetRequiredService<IShellRegistry>();
var generations = new ConditionalWeakTable<object, GenerationIdentity>();
app.MapGet("/__qualification/generation", async (CancellationToken cancellationToken) => {
    var shell = await registry.GetOrActivateAsync(shellName, cancellationToken);
    return new { generation = generations.GetValue(shell, _ => new(Guid.NewGuid().ToString("N"))).Id };
}).RequireAuthorization(StudioBridgeAuthorization.ModuleManagementReadPolicyName);
app.MapPost("/__qualification/reconcile", async (INuplaneAdminOperations operations, CancellationToken cancellationToken) =>
    await operations.TriggerReconcileAsync(cancellationToken)).RequireAuthorization(StudioBridgeAuthorization.ModuleManagementManagePolicyName);
await app.StartAsync();
var initial = await app.Services.GetRequiredService<INuplaneAdminOperations>().TriggerReconcileAsync(CancellationToken.None);
if (initial.RunResult is not { IsDegraded: false }) throw new InvalidOperationException("Initial owned Studio reconciliation failed.");
Console.WriteLine("WEAVER_STUDIO_CONSUMER_READY");
await app.WaitForShutdownAsync();

sealed record GenerationIdentity(string Id);
