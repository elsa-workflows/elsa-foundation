using System.Data.Common;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.DependencyInjection;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Models;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Api.Authorization;
using Elsa.Workflows.Runtime.Api.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Services.Recovery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Api.Tests;

public sealed partial class PersistedRuntimeHistoryOccurrenceAuthorizationTests
{
    private const string Tenant = "tenant-a";
    private const string OtherTenant = "tenant-b";
    private const string RunId = "persisted-iam-run";
    private const string Endpoint = "/runtime/workflows/instances/" + RunId + "/history/occurrences";
    private static readonly DateTimeOffset At = DateTimeOffset.UnixEpoch;
    private static readonly string[] Grants = [WorkflowRuntimePermissions.WorkflowRuntimeRead,
        HttpContextActivityExecutionInspectionAuthorizationContext.StructurePermission,
        HttpContextActivityExecutionInspectionAuthorizationContext.SensitiveValuesPermission,
        HttpContextActivityExecutionInspectionAuthorizationContext.ResolveValuePayloadsPermission];

    // Real native/history publication plus separate first-party persisted IAM authority. EnsureCreated
    // is deliberate component setup, not generated-migration, authored-capture or composed-host proof.
    private sealed partial class Fixture(WebApplication app, DirectoryInfo directory, Sessions sessions, HistoryGate gate) : IAsyncDisposable
    {
        internal HttpClient Client { get; private set; } = null!;
        internal HistoryGate Gate => gate;
        internal int SeedOccurrenceCommands { get; private set; }
        internal string UserId { get; private set; } = "";
        internal ClaimsPrincipal SignedIn { get; private set; } = new();

        internal static async Task<Fixture> Start(string grantsFrom = "direct")
        {
            var directory = Directory.CreateTempSubdirectory("elsa-history-persisted-iam-");
            var builder = WebApplication.CreateBuilder(); builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
            var sessions = new Sessions(); var gate = new HistoryGate();
            builder.Services.AddSingleton(sessions);
            builder.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(At.AddMinutes(1)));
            builder.Services.AddDataProtection().PersistKeysToFileSystem(Directory.CreateDirectory(Path.Combine(directory.FullName, "keys")));
            var api = new WorkflowsRuntimeApiFeature(); api.ConfigureServices(builder.Services);
            builder.Services.AddFoundationAspNetCoreIdentityEntityFrameworkCore(new IdentityIamEntityFrameworkCoreOptions
                { ConnectionString = "Data Source=" + Path.Combine(directory.FullName, "iam.sqlite") + ";Pooling=False" },
                isDevelopmentOrDemo: true, configureIdentity: options => options.DefaultTenantId = Tenant);
            builder.Services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = "PersistedHistory"; options.DefaultChallengeScheme = "PersistedHistory";
            }).AddScheme<AuthenticationSchemeOptions, Authentication>("PersistedHistory", _ => { });
            builder.Services.AddAuthorization();
            builder.Services.Configure<RuntimeRecoveryContinuationOptions>(options =>
            { options.SigningKey = "persisted-history-owned-component-signing-key-0123456789"; options.AllowEphemeralDevelopmentKey = false; });
            Runtime(builder.Services, "Data Source=" + Path.Combine(directory.FullName, "runtime.sqlite") + ";Pooling=False");
            builder.Services.AddDbContext<RuntimeSqliteDbContext>(options => options.AddInterceptors(gate));
            var app = builder.Build();
            app.UseAuthentication();
            app.Use(async (http, next) =>
            {
                if (http.RequestServices.GetRequiredService<NormalizedPrincipalValidator>().TryGetNormalizedPrincipal(http.User, out var principal))
                {
                    var tenants = principal.FindAll(IdentityClaimTypes.TenantId).Select(claim => claim.Value).ToArray();
                    if (tenants.Length == 1) Bind(http.RequestServices, tenants[0]);
                }
                await next(http);
            });
            app.UseAuthorization(); WorkflowsRuntimeApi.MapWorkflowsRuntimeApi(app);
            var fixture = new Fixture(app, directory, sessions, gate);
            try
            {
                await fixture.Scope(Tenant, async services =>
                {
                    await services.GetRequiredService<IdentityIamDbContext>().Database.EnsureCreatedAsync();
                    await services.GetRequiredService<RuntimeDbContext>().Database.EnsureCreatedAsync();
                    await fixture.CreateUser(services, Tenant, grantsFrom);
                    await SeedHistory(services);
                });
                await app.StartAsync(); fixture.SeedOccurrenceCommands = gate.OccurrenceCommands;
                fixture.Client = app.GetTestClient(); fixture.UsePrincipal(fixture.SignedIn); return fixture;
            }
            catch { await fixture.DisposeAsync(); throw; }
        }

        private static void Runtime(IServiceCollection services, string connection)
        {
            services.AddRuntimeOperationalStateEntityFrameworkCore(new() { ConnectionString = connection });
            services.AddRuntimeArtifactsEntityFrameworkCore(new() { ConnectionString = connection });
            services.AddRuntimeActivityExecutionEntityFrameworkCore(new() { ConnectionString = connection });
            services.AddRuntimeBookmarksEntityFrameworkCore(new() { ConnectionString = connection });
            services.AddRuntimeWorkflowExecutionEntityFrameworkCore(new() { ConnectionString = connection });
            services.AddRuntimeWorkflowTestScopeEntityFrameworkCore(new() { ConnectionString = connection });
            services.AddRuntimeWorkflowAlterationEntityFrameworkCore(new() { ConnectionString = connection });
            services.AddRuntimeSchedulerWorkQueueEntityFrameworkCore(new() { ConnectionString = connection });
            services.AddRuntimeDurableTimerEntityFrameworkCore(new() { ConnectionString = connection });
            services.AddRuntimeWorkflowDispatchEntityFrameworkCore(); services.AddRuntimePostCommitOutboxEntityFrameworkCore();
            services.AddRuntimeCheckpointCommitEntityFrameworkCore();
        }
        private static async Task SeedHistory(IServiceProvider services)
        {
            var policy = new RuntimeHistoryCapturePolicy("persisted-iam-controls", 1, true, ["ActivityStarted"]);
            var workflow = new WorkflowExecutionState(RunId, new("artifact", "definition", "version", "1", "hash"), WorkflowExecutionStatus.Running,
                null, At, At, At, null, null, null, Tenant, new Dictionary<string, string>())
                { HistoryCapturePolicy = policy, HistoryCaptureEnabledAtAdmission = true };
            await services.GetRequiredService<IWorkflowExecutionStateStore>().SaveAsync(workflow);
            var facts = Enumerable.Range(0, 3).Select(index => new RuntimeHistoryCandidate(new("event-" + index, RunId, 0,
                RuntimeHistoryEventCategory.ActivityLifecycle, "ActivityStarted", At, "activity-" + index, checkpointId: "seed")
                { Authority = RuntimeHistoryAuthority.CommittedTransition, ActivityAncestryKnown = true }, index)).ToArray();
            var commit = new RuntimeCheckpointCommit("physical-seed", new("seed", "control", RunId, At, [], new Dictionary<string, string>()),
                new(null, null, [], [], [], [], []), [], new Dictionary<string, string>()) { HistoryBatches = [new(policy, "seed", At, true, facts)] };
            await services.GetRequiredService<IRuntimeCheckpointCommitStore>().CommitAsync(commit, new(RuntimeCheckpointPersistenceMode.Immediate));
            var reader = services.GetRequiredService<IRuntimeHistoryStore>();
            Assert.IsAssignableFrom<IRuntimeHistoryOccurrenceStore>(reader);
            Assert.Equal(3, (await reader.FindHeadAsync(RunId))!.LastSequence);
        }

        private static void Bind(IServiceProvider services, string tenant) =>
            services.GetRequiredService<IPersistenceAccessContextBinder>().Bind(PersistenceAccessContext.Scoped(new(tenant)));
        internal async Task Scope(string tenant, Func<IServiceProvider, Task> action)
        {
            await using var scope = app.Services.CreateAsyncScope(); Bind(scope.ServiceProvider, tenant); await action(scope.ServiceProvider);
        }
        internal void UsePrincipal(ClaimsPrincipal principal)
        {
            var key = Guid.NewGuid().ToString("N"); sessions.Items[key] = principal.Clone();
            Client.DefaultRequestHeaders.Remove("X-Persisted-History-Session"); Client.DefaultRequestHeaders.Add("X-Persisted-History-Session", key);
        }
        public async ValueTask DisposeAsync()
        { gate.Release(); Client?.Dispose(); await app.DisposeAsync(); directory.Delete(recursive: true); }
    }

    private sealed class Sessions
    { internal System.Collections.Concurrent.ConcurrentDictionary<string, ClaimsPrincipal> Items { get; } = new(StringComparer.Ordinal); }
    private sealed class Authentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, Sessions sessions)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(
            sessions.Items.TryGetValue(Request.Headers["X-Persisted-History-Session"].ToString(), out var principal)
                ? AuthenticateResult.Success(new AuthenticationTicket(principal.Clone(), Scheme.Name)) : AuthenticateResult.NoResult());
    }
    private sealed class HistoryGate : DbCommandInterceptor
    {
        private int armed;
        private TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int OccurrenceCommands;
        internal Task Entered => entered.Task;
        internal void Arm()
        { entered = new(TaskCreationOptions.RunContinuationsAsynchronously); release = new(TaskCreationOptions.RunContinuationsAsynchronously); Interlocked.Exchange(ref armed, 1); }
        internal void Release() => release.TrySetResult();
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData data, DbDataReader result, CancellationToken token = default)
        {
            if (command.CommandText.Contains("elsa_runtime_history_occurrence_member", StringComparison.OrdinalIgnoreCase))
            {
                Interlocked.Increment(ref OccurrenceCommands);
                if (Interlocked.Exchange(ref armed, 0) == 1) { entered.TrySetResult(); await release.Task.WaitAsync(token); }
            }
            return result;
        }
    }
}
