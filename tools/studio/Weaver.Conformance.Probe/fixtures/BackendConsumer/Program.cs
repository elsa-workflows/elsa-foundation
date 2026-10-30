using System.Reflection;
using System.Runtime.CompilerServices;
using CShells.AspNetCore.Configuration;
using CShells.AspNetCore.Extensions;
using CShells.AspNetCore.Features;
using CShells.DependencyInjection;
using CShells.Features;
using CShells.Lifecycle;
using CShells.Nuplane;
using Elsa.Agent.Anthropic.Services;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.GitHubCopilot.Services;
using Elsa.Cluster.Readability;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Modularity.Api.Authorization;
using Elsa.Modularity.Api.Services;
using Elsa.Modularity.Core.Contracts;
using Elsa.Modularity.Core.Models;
using Elsa.Modularity.Nuplane.Extensions;
using Elsa.Studio.Preferences.Core.Contracts;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nuplane;
using Nuplane.Admin;
using Nuplane.Builder;
using Nuplane.Feeds.Configuration;
using Nuplane.Loading.Hosting.Builder;
using Nuplane.Reconciliation;
using Nuplane.Reconciliation.Configuration;
using Nuplane.Reconciliation.LockFile;
using Nuplane.Sources.Directory.Builder;

const string shellName = "Authoring";
var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("shells.json", optional: false, reloadOnChange: true);
var contentRoot = builder.Environment.ContentRootPath;
var keys = Directory.CreateDirectory(Path.Combine(contentRoot, "state", "data-protection-keys"));
builder.Services.AddDataProtection().PersistKeysToFileSystem(keys);
var origin = builder.Configuration["Qualification:StudioOrigin"];
if (!Uri.TryCreate(origin, UriKind.Absolute, out var studioOrigin) || !studioOrigin.IsLoopback)
    throw new InvalidOperationException("Set Qualification:StudioOrigin to the exact owned Studio origin before starting.");
builder.Services.AddCors(options => options.AddPolicy("OwnedStudio", policy => policy
    .WithOrigins(studioOrigin.GetLeftPart(UriPartial.Authority)).AllowAnyHeader().AllowAnyMethod().AllowCredentials()
    .WithExposedHeaders("Content-Disposition", "ETag")));
builder.Services.AddHttpContextAccessor();

var contracts = new Assembly[] {
    typeof(IShellFeature).Assembly, typeof(IWebShellFeature).Assembly, typeof(IAgentTool).Assembly,
    typeof(IPermissionContributor).Assembly, typeof(IStudioPreferenceService).Assembly,
    typeof(IGitHubCopilotClientFactory).Assembly, typeof(IAnthropicChatClientFactory).Assembly,
    typeof(IChatClient).Assembly, typeof(IServiceCollection).Assembly
}.Distinct().ToArray();
var state = Path.Combine(contentRoot, "native-store", "state.json");
Directory.CreateDirectory(Path.GetDirectoryName(state)!);
builder.Services.AddNuplaneAdmin();
builder.Services.AddNuplane(builder.Configuration.GetSection("Nuplane"), nuplane => {
    nuplane.UseBasePath(contentRoot);
    // Match Foundation.Host/Workbench's ordinary backend loading profile. Studio's
    // protected asset host has its own coordinated store; it is a separate composition.
    nuplane.WithStateFile(state);
    nuplane.Services.Configure<FeedResolutionOptions>(options => options.PackageInstallRoot = Path.Combine(contentRoot, "native-store", "installs"));
    nuplane.Services.Configure<LockFileOptions>(options => options.Path = Path.Combine(contentRoot, "native-store", "nuplane.lock.json"));
    nuplane.Services.Configure<ReconciliationOptions>(options => {
        options.EnableAutomaticReconciliation = false;
        options.StartupFailurePolicy = StartupFailurePolicy.FailHost;
    });
    nuplane.AddDirectoryFeed("weaver-backend", "runtime-feed", feed => { feed.Watch = false; feed.IncludeAll(); });
    nuplane.AutoloadPackages(builder.Configuration.GetSection("Nuplane:Loading"), loading => {
        foreach (var assembly in contracts) {
            var identity = assembly.GetName(); var token = identity.GetPublicKeyToken();
            loading.SharedAssembly(identity.Name!, token is { Length: > 0 } ? Convert.ToHexString(token).ToLowerInvariant() : null, identity.Version!.Major);
        }
    });
});
builder.Services.ShareWithShells<IReconciliationTriggerIngress>()
    .ShareWithShells<ManualReconcileCoordinator>().ShareWithShells<INuplaneAdminOperations>();
builder.Services.AddNuplaneFeatureCatalog();
// The real ModularityApi feature replaces these root-only guards in every shell.
// Root validation must not manufacture a shell identity or a writable feature store.
builder.Services.TryAddScoped<IShellFeatureConfigurationStore, RootFeatureStoreGuard>();
builder.Services.TryAddScoped<IShellReloader, RootShellReloadGuard>();
builder.Services.AddEfSchemaReadability();
builder.Services.AddShellStartupValidation();

builder.Services.AddCShellsAspNetCore(shells => shells.WithHostAssemblies()
    .WithAssemblies(
        typeof(Elsa.Primitives.Hosting.PrimitivesFeature).Assembly,
        typeof(Elsa.Locking.FileSystem.FileSystemLockingFeature).Assembly,
        typeof(Elsa.Events.EventsFeature).Assembly,
        typeof(Elsa.Mediator.MediatorFeature).Assembly,
        typeof(Elsa.Tasks.TasksFeature).Assembly,
        typeof(Elsa.Serialization.SystemText.SerializationFeature).Assembly,
        typeof(Elsa.Expressions.ExpressionsFeature).Assembly,
        typeof(Elsa.Expressions.Api.ExpressionsApiFeature).Assembly,
        typeof(Elsa.Api.Capabilities.ApiCapabilitiesFeature).Assembly,
        typeof(Elsa.Foundation.Identity.FoundationIdentityAbstractionsFeature).Assembly,
        typeof(Elsa.Foundation.Identity.Api.FoundationIdentityApiFeature).Assembly,
        typeof(Elsa.Foundation.Identity.AspNetCoreIdentity.AspNetCoreIdentityFeature).Assembly,
        typeof(Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.AspNetCoreIdentityEntityFrameworkCoreFeature).Assembly,
        typeof(Elsa.Foundation.Identity.OpenIddict.OpenIddictIdentityFeature).Assembly,
        typeof(Elsa.Studio.Preferences.Api.StudioPreferencesApiFeature).Assembly,
        typeof(Elsa.Studio.Preferences.Persistence.EntityFrameworkCore.StudioPreferencesEntityFrameworkCoreFeature).Assembly,
        typeof(Elsa.Activities.Design.Api.ActivitiesDesignApiFeature).Assembly,
        typeof(Elsa.Activities.Design.Persistence.EntityFrameworkCore.ActivitiesDesignEntityFrameworkCoreFeature).Assembly,
        typeof(Elsa.Activities.Design.Reconciliation.ActivitiesDesignReconciliationFeature).Assembly,
        typeof(Elsa.Activities.Design.Reconciliation.Clr.ClrActivityReconciliationFeature).Assembly,
        typeof(Elsa.Activities.Flowchart.ActivitiesFlowchartFeature).Assembly,
        typeof(Elsa.Activities.Primitives.ActivitiesPrimitivesFeature).Assembly,
        typeof(Elsa.Activities.Runtime.ActivitiesRuntimeFeature).Assembly,
        typeof(Elsa.Workflows.Design.Api.WorkflowsDesignApiFeature).Assembly,
        typeof(Elsa.Workflows.Design.Validations.WorkflowDesignValidationsFeature).Assembly,
        typeof(Elsa.Workflows.Design.Persistence.EntityFrameworkCore.WorkflowsDesignEntityFrameworkCoreFeature).Assembly,
        typeof(Elsa.Workflows.Publishing.WorkflowsPublishingFeature).Assembly,
        typeof(Elsa.Workflows.Publishing.Api.WorkflowsPublishingApiFeature).Assembly,
        typeof(Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.PublishingEntityFrameworkCoreFeature).Assembly,
        typeof(Elsa.Workflows.Runtime.Api.WorkflowsRuntimeApiFeature).Assembly,
        typeof(Elsa.Workflows.Runtime.Resumption.WorkflowsRuntimeResumptionFeature).Assembly,
        typeof(Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.RuntimeArtifactsEntityFrameworkCoreFeature).Assembly,
        typeof(Elsa.Agent.FoundationAgentAbstractionsFeature).Assembly,
        typeof(Elsa.Agent.Api.FoundationAgentApiFeature).Assembly,
        typeof(Elsa.Agent.Workflows.FoundationWorkflowsAgentFeature).Assembly,
        typeof(Elsa.Agent.GitHubCopilot.GitHubCopilotAgentFeature).Assembly,
        typeof(Elsa.Agent.Anthropic.AnthropicAgentFeature).Assembly,
        typeof(Elsa.Modularity.Api.ModularityApiFeature).Assembly)
    .WithNuplaneFeatureDiscovery(integration => { integration.Enabled = true; integration.RefreshTrigger = NuplaneRefreshTrigger.ChangedOrPending; integration.AutoReload = true; })
    .WithAuthenticationAndAuthorization()
    .WithConfigurationProvider(builder.Configuration)
    .WithWebRouting(options => { options.EnablePathRouting = true; options.ExcludePaths = ["/health/live"]; }));
builder.Services.AddAuthentication();
builder.Services.AddAuthorization();
var tokenConnection = builder.Configuration.GetConnectionString("Tokens");
if (string.IsNullOrWhiteSpace(tokenConnection)) throw new InvalidOperationException("Set the separate owned Tokens connection string.");
builder.Services.AddDbContext<TokenDb>(options => { options.UseSqlite(tokenConnection); options.UseOpenIddict(); });
builder.Services.AddOpenIddict().AddCore(options => options.UseEntityFrameworkCore().UseDbContext<TokenDb>());

await using var app = builder.Build();
await using (var scope = app.Services.CreateAsyncScope())
    await scope.ServiceProvider.GetRequiredService<TokenDb>().Database.EnsureCreatedAsync();
app.UseCors("OwnedStudio");
app.MapShells();
app.UseAuthentication();
app.UseAuthorization();
var generations = new ConditionalWeakTable<object, GenerationIdentity>();
app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
app.MapPost("/__qualification/reconcile", async (INuplaneAdminOperations operations, CancellationToken cancellationToken) =>
    await operations.TriggerReconcileAsync(cancellationToken)).RequirePermission(ModuleManagementPermissionKeys.Manage);
app.MapGet("/__qualification/generation", async (IShellRegistry registry, CancellationToken cancellationToken) => {
    var shell = await registry.GetOrActivateAsync(shellName, cancellationToken);
    await using var scope = shell.BeginScope();
    var featureStore = scope.ServiceProvider.GetRequiredService<IShellFeatureConfigurationStore>();
    var shellReloader = scope.ServiceProvider.GetRequiredService<IShellReloader>();
    if (featureStore.GetType() != typeof(JsonShellFeatureConfigurationStore) || shellReloader.GetType() != typeof(ShellReloader)
        || RootFeatureStoreGuard.UseCount != 0 || RootShellReloadGuard.UseCount != 0)
        throw new InvalidOperationException("The actual ModularityApi shell must own feature configuration and reload; root guards must remain unused.");
    var tools = await scope.ServiceProvider.GetRequiredService<IAgentToolRegistry>().ListAsync(cancellationToken);
    return new {
        generation = generations.GetValue(shell, _ => new(Guid.NewGuid().ToString("N"))).Id,
        contextSources = scope.ServiceProvider.GetServices<IAgentContextProvider>().Select(source => new { source.Descriptor.Id, source.Descriptor.ModuleId, assembly = source.GetType().Assembly.GetName().Name }),
        tools = tools.Select(tool => new { tool.Descriptor.Name, tool.Descriptor.ModuleId, tool.Descriptor.ContractVersion, assembly = tool.GetType().Assembly.GetName().Name }),
        featureConfiguration = new {
            store = new { type = featureStore.GetType().FullName, assembly = featureStore.GetType().Assembly.FullName },
            reloader = new { type = shellReloader.GetType().FullName, assembly = shellReloader.GetType().Assembly.FullName },
            rootGuardUses = RootFeatureStoreGuard.UseCount + RootShellReloadGuard.UseCount
        },
        sharedContracts = contracts.Select(assembly => new { name = assembly.GetName().Name, identity = assembly.FullName, location = assembly.Location })
    };
}).RequirePermission(ModuleManagementPermissionKeys.Read);
await StartupDiagnosticGate.ObserveAsync(builder.Configuration, contentRoot, "before-start", null);
try {
    await app.StartAsync();
} catch (Exception failure) {
    try { await StartupDiagnosticGate.ObserveAsync(builder.Configuration, contentRoot, "start-failed", null, failure); }
    catch (Exception diagnosticFailure) { Console.Error.WriteLine("Owned startup diagnostic failed: " + diagnosticFailure.GetType().FullName); }
    throw;
}
// Nuplane's blocking startup service has already reconciled under FailHost. Later explicit
// reconciliations belong to observed package transitions through the protected endpoint above.
await app.Services.GetRequiredService<IShellRegistry>().GetOrActivateAsync(shellName);
await QualificationPrincipals.InitializeAsync(app.Services.GetRequiredService<IShellRegistry>(), shellName,
    builder.Configuration, app.Lifetime.ApplicationStopping);
Console.WriteLine("WEAVER_BACKEND_CONSUMER_READY");
await app.WaitForShutdownAsync();

sealed record GenerationIdentity(string Id);

sealed class RootFeatureStoreGuard : IShellFeatureConfigurationStore
{
    private static int _useCount;
    public static int UseCount => Volatile.Read(ref _useCount);
    private static InvalidOperationException Refuse()
    {
        Interlocked.Increment(ref _useCount);
        return new("Resolve feature configuration through the actual shell-scoped ModularityApi service.");
    }
    public Task<ShellFeatureConfigurationSnapshot> LoadAsync(CancellationToken cancellationToken = default) => throw Refuse();
    public Task<ShellFeatureConfigurationSnapshot> SaveAsync(string expectedRevision, IReadOnlyList<FeatureConfigurationChange> features, CancellationToken cancellationToken = default) => throw Refuse();
}

sealed class RootShellReloadGuard : IShellReloader
{
    private static int _useCount;
    public static int UseCount => Volatile.Read(ref _useCount);
    public Task<int> ReloadAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _useCount);
        throw new InvalidOperationException("Resolve feature reload through the actual shell-scoped ModularityApi service.");
    }
}
