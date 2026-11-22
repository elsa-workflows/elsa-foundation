using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Nuplane.Admin;
using Nuplane.Builder;
using Nuplane.Feeds.Configuration;
using Nuplane.Reconciliation.Configuration;
using Nuplane.Reconciliation.LockFile;
using Elsa.Studio.Hosting.Themes;
using Elsa.Studio.Hosting.Nuplane;
using CShells.DependencyInjection;
using Elsa.Studio.Hosting;
using Elsa.Studio.Api.Features;
using Elsa.Studio.Attention;
using Elsa.Studio.UserTasks;
using Elsa.Studio.ConsoleStream;
using Elsa.Studio.Dashboard;
using Elsa.Studio.Diagnostics.OpenTelemetry;
using Elsa.Studio.Diagnostics.StructuredLogs;
using Elsa.Studio.ExpressionEditors.JavaScript;
using Elsa.Studio.ExpressionEditors.Liquid;
using Elsa.Studio.ExtensionBuilder;
using Elsa.Studio.FeatureManagement;
using Elsa.Studio.Connections;
using Elsa.Studio.Etl;
using Elsa.Studio.Secrets;
using Elsa.Studio.Agent;
using Elsa.Studio.Weaver.Workflows;
using Elsa.Studio.Workflows;
using Elsa.Studio.Workflows.Dashboard;

using Elsa.Studio.Backends;
using Elsa.Studio.Identity.ExternalAuthentication;


var builder = WebApplication.CreateBuilder(args);
var root = builder.Environment.ContentRootPath;
var marker = new FileInfo(Path.Combine(root, ".ux-acceptance-owner.json"));
if (!marker.Exists || marker.LinkTarget is not null)
    throw new InvalidOperationException("The SDK consumer requires a regular owned acceptance marker.");
using (var document = JsonDocument.Parse(File.ReadAllText(marker.FullName)))
    if (!document.RootElement.GetProperty("sdkStoreFixture").GetBoolean() ||
        string.IsNullOrWhiteSpace(document.RootElement.GetProperty("ownerToken").GetString()))
        throw new InvalidOperationException("Only the owned acceptance producer may enroll this consumer's fresh store.");
var store = Directory.CreateDirectory(Path.Combine(root, "native-store"));
if (store.LinkTarget is not null) throw new InvalidOperationException("The owned SDK store must be a regular directory.");
var installs = Directory.CreateDirectory(Path.Combine(store.FullName, "installs"));
if (installs.LinkTarget is not null) throw new InvalidOperationException("The owned SDK installs must be a regular directory.");
var state = Path.Combine(store.FullName, "state.json");
builder.Services.AddDataProtection().PersistKeysToFileSystem(Directory.CreateDirectory(Path.Combine(root, "state", "keys")));
var studio = builder.AddElsaStudio();
builder.Services.AddStudioBackendsHostConfiguration(builder.Configuration);
studio.AddElsaStudioNuplane(options => options.DirectoryFeedName = "studio-local-packages", configureNuplane: nuplane => {
    // The producer owns one fresh root and this complete, single-client roster.
    nuplane.WithStateFile(state).UseCoordinatedPackageStore([new("default", "studio", state)], true);
    nuplane.Services.Configure<FeedResolutionOptions>(options => options.PackageInstallRoot = installs.FullName);
    nuplane.Services.Configure<LockFileOptions>(options => options.Path = Path.Combine(store.FullName, "nuplane.lock.json"));
    nuplane.Services.Configure<ReconciliationOptions>(options => {
        options.EnableAutomaticReconciliation = false;
        options.StartupFailurePolicy = StartupFailurePolicy.FailHost;
    });
});
studio.AddElsaStudioThemes();
studio.ConfigureShells(shells => {
    shells.WithStudioStartupAssemblies(
                typeof(StudioApiFeature).Assembly,
                typeof(BackendsStudioFeature).Assembly,
                typeof(ConsoleStreamStudioFeature).Assembly,
                typeof(DiagnosticsOpenTelemetryStudioFeature).Assembly,
                typeof(DiagnosticsStructuredLogsStudioFeature).Assembly,
                typeof(ExtensionBuilderStudioFeature).Assembly,
                typeof(FeatureManagementStudioFeature).Assembly,
                typeof(DashboardStudioFeature).Assembly,
                typeof(AttentionStudioFeature).Assembly,
                typeof(UserTasksStudioFeature).Assembly,
                typeof(WorkflowsDashboardStudioFeature).Assembly,
                typeof(WeaverWorkflowsStudioFeature).Assembly,
                typeof(WorkflowsStudioFeature).Assembly,
                typeof(SecretsStudioFeature).Assembly,
                typeof(AgentStudioFeature).Assembly,
                typeof(ConnectionsStudioFeature).Assembly,
                typeof(ExternalAuthenticationStudioFeature).Assembly,
                typeof(Elsa.Studio.Decisions.DecisionsStudioFeature).Assembly,
                typeof(EtlStudioFeature).Assembly,
                typeof(JavaScriptExpressionEditorStudioFeature).Assembly,
                typeof(LiquidExpressionEditorStudioFeature).Assembly);
});
await using var app = builder.Build();
var maintenance = app.Services.GetRequiredService<INuplanePackageStoreMaintenanceOperations>();
var preview = await maintenance.PreviewEnrollmentAsync();
if (preview.Membership == "Unenrolled") {
    var enrolled = await maintenance.EnrollAsync(new("default", preview.RootIdentity!, 1, true, ["studio"], []));
    if (enrolled.Status != "Complete") throw new InvalidOperationException("Owned SDK store enrollment failed: " + enrolled.Status);
} else if (preview.Membership != "Complete")
    throw new InvalidOperationException("Owned SDK store membership is not complete.");
var membership = await maintenance.PreviewEnrollmentAsync();
if (membership.Membership != "Complete") throw new InvalidOperationException("Owned SDK store did not retain complete enrollment.");
await File.WriteAllTextAsync(Path.Combine(root, "sdk-store-enrollment.json"), JsonSerializer.Serialize(new {
    processId = Environment.ProcessId, sourceSha = Environment.GetEnvironmentVariable("UX_SOURCE_SHA"),
    membership = membership.Membership, clientId = "studio", rootLabel = "default", completeOwnedRoster = true
}));
app.UseElsaStudio();
app.MapElsaStudio();
app.MapElsaStudioNuplane();
await app.RunAsync();
