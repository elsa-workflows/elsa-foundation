using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Elsa.ActorFoundation.Hosting;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Extensions;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.KernelApi;
using Elsa.Workflows.Runtime.KernelHosting;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using static Elsa.Workflows.Runtime.KernelHost.Tests.Compiled;
using Contract = Kernel.Foundation.Contracts;

namespace Elsa.Workflows.Runtime.KernelHost.Tests;

public sealed class KernelPublishedExecuteTests
{
    private const string FoundationDatabase = "KERNEL_EXECUTE_FOUNDATION_POSTGRES";
    private const string RuntimeDatabase = "KERNEL_EXECUTE_RUNTIME_POSTGRES";
    private static readonly PersistenceScope Scope = new(PersistenceScope.DefaultValue);

    [Fact]
    public Task Sqlite_standard_execute_admits_published_native_and_flowchart() => RunAsync(false);

    [SkippableFact]
    public Task PostgreSql_standard_execute_admits_published_native_and_flowchart()
    {
        Skip.If(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(FoundationDatabase)) ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(RuntimeDatabase)), "Requires two dedicated PostgreSQL databases.");
        return RunAsync(true);
    }

    private static async Task RunAsync(bool postgres)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var directory = postgres ? null : Directory.CreateTempSubdirectory("kernel-published-execute-");
        try
        {
            var foundation = postgres ? Environment.GetEnvironmentVariable(FoundationDatabase) : null;
            var runtime = postgres ? Environment.GetEnvironmentVariable(RuntimeDatabase)! : new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(directory!.FullName, "runtime.db"), Pooling = false
            }.ToString();
            if (postgres) Assert.NotEqual(new Npgsql.NpgsqlConnectionStringBuilder(foundation).Database,
                new Npgsql.NpgsqlConnectionStringBuilder(runtime).Database);
            await RunAsync(postgres ? "PostgreSql" : "Sqlite", directory?.FullName, foundation, runtime, deadline.Token);
        }
        finally { directory?.Delete(recursive: true); }
    }

    private static async Task RunAsync(string provider, string? directory, string? foundation, string runtime, CancellationToken token)
    {
        var canonical = ExecutableOf("execute-native", "canonical-publication", Sequence("sequence",
            SetOutput("input", "message", Request("input", "message")), SetOutput("variable", "seed", VariableRead("input", "seed-key"))),
            Variable("seed-key", "seed", "authored-variable"));
        var contract = new WorkflowExecutableInputContract(WorkflowExecutableInputContract.CurrentVersion,
            [new("message", new TypeReference("String"), isRequired: false, JsonSerializer.SerializeToElement("authored-input"))]);
        canonical = new(canonical.Identity, canonical.RootActivity, canonical.ResumeTargets, canonical.CreatedAt,
            canonical.CompatibilityMetadata, contract, canonical.Dependencies, canonical.RuntimeRequirements, canonical.StorageDriverRequirements,
            canonical.IncidentStrategy, workflowVariables: canonical.WorkflowVariables);
        var alias = PublishedAs(canonical, "native-blue", "2");
        var sibling = PublishedAs(canonical, "native-sibling", "3");
        var flowchart = ExecutableOf("execute-flowchart", "flowchart", Node("flow", Types.Flowchart,
            structure: Structure("elsa.flowchart.structure", new { startNodeId = "output", connections = Array.Empty<object>(), variables = Array.Empty<object>() }),
            slots: [Slot("Flowchart.Activities", SetOutput("output", "message", Request("input", "message")))]));

        var waiting = ExecutableOf("execute-waiting", "waiting", Sequence("sequence",
            Node("delay", Types.Delay, [Literal("Duration", "00:00:45", "TimeSpan")])));

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0));
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.AddElsaEndpoints();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddAuthentication("KernelExecuteFixture").AddScheme<AuthenticationSchemeOptions, FixtureAuthentication>("KernelExecuteFixture", _ => { });
        builder.Services.AddFoundationIdentityAbstractions(options => options.NormalizedAuthenticationTypes = new HashSet<string>(StringComparer.Ordinal)
            { "KernelExecuteFixture" });
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton<IWorkflowExecutableStartPolicy, FixturePolicy>();
        builder.Services.AddSingleton<IPermissionResourceHandler, DeleteResourcePolicy>();
        builder.Services.AddSingleton<IWellKnownTypeRegistry, FixtureTypes>();
        builder.Services.AddWorkflowRuntime();
        builder.Services.AddRuntimeEntityFrameworkCore(new()
        {
            Provider = provider, ConnectionString = runtime,
            RecoveryContinuationSigningKey = new string('r', 32), HierarchyCursorSigningKey = new string('h', 32)
        });
        var pinBoundary = new PinBoundary();
        var pinDescriptor = builder.Services.Last(item => item.ServiceType == typeof(IRuntimeKernelExecutablePinStore));
        builder.Services.Remove(pinDescriptor);
        builder.Services.AddScoped<IRuntimeKernelExecutablePinStore>(services => new FixturePins(
            (IRuntimeKernelExecutablePinStore)ActivatorUtilities.CreateInstance(services, pinDescriptor.ImplementationType!), pinBoundary));
        var api = new WorkflowsRuntimeApiFeature();
        api.ConfigureServices(builder.Services);
        new WorkflowsRuntimeKernelHostingFeature
        {
            Provider = provider,
            SqlitePath = directory is null ? null : Path.Combine(directory, "foundation.db"),
            PostgresConnectionString = foundation
        }.ConfigureServices(builder.Services);
        new WorkflowsRuntimeKernelFeature().PostConfigureServices(builder.Services);
        api.PostConfigureServices(builder.Services);
        new WorkflowsRuntimeKernelApiFeature().PostConfigureServices(builder.Services);

        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        WorkflowsRuntimeApi.MapWorkflowsRuntimeApi(app);
        var scopes = app.Services.GetRequiredService<IServiceScopeFactory>();
        await using var publication = await scopes.CreateAsyncScopeAsync(Scope);
        var stores = publication.ServiceProvider;
        await stores.GetRequiredService<RuntimeDbContext>().Database.MigrateAsync(token);
        var materials = stores.GetRequiredService<IWorkflowExecutableStore>();
        await materials.SaveAsync(canonical, token);
        await materials.SaveAsync(flowchart, token);
        await materials.SaveAsync(waiting, token);
        await PublishAsync(alias, "blue");
        await PublishAsync(sibling, "default");
        await PublishAsync(flowchart, "default");
        await PublishAsync(waiting, "default");
        var shell = app.Services.GetRequiredService<KernelShellRuntime>();
        await shell.StartAsync(token);
        try
        {
            await app.StartAsync(token);
            var address = Assert.Single(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses);
            using var http = new HttpClient { BaseAddress = new(address) };
            var target = WorkflowActivationReferenceIdentity.Create("publish-native-blue");
            using var ambiguous = await ExecuteAsync(canonical.Identity.ArtifactId, new { });
            Assert.Equal(HttpStatusCode.Conflict, ambiguous.StatusCode);
            using var wrongTenant = await ExecuteAsync(alias.Identity.ArtifactId, new { sourceReferenceId = target }, tenant: "another-tenant");
            Assert.Equal(HttpStatusCode.NotFound, wrongTenant.StatusCode);
            using var invalid = await ExecuteAsync(alias.Identity.ArtifactId, new { sourceReferenceId = target, inputs = new { unexpected = 1 } });
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            using var denied = await ExecuteAsync(alias.Identity.ArtifactId, new { sourceReferenceId = target }, subject: "deny");
            Assert.True(denied.StatusCode == HttpStatusCode.InternalServerError,
                $"Expected Execute policy failure, got {denied.StatusCode}: {await denied.Content.ReadAsStringAsync(token)}");
            pinBoundary.BeforeAdmission = async (source, cancellationToken) =>
            {
                if (source.DefinitionId != sibling.Identity.DefinitionId) return;
                await using var retracting = await scopes.CreateAsyncScopeAsync(Scope);
                var coordinator = retracting.ServiceProvider.GetRequiredService<IWorkflowActivationCoordinator>();
                Assert.True((await coordinator.DeactivateAsync(new(sibling, "default", WorkflowActivationSource.Publishing, 1), cancellationToken)).Succeeded);
                pinBoundary.Retracted = true;
            };
            using var stale = await ExecuteAsync(sibling.Identity.ArtifactId,
                new { sourceReferenceId = WorkflowActivationReferenceIdentity.Create("publish-native-sibling") }, subject: "retract");
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            Assert.True(pinBoundary.Retracted);
            var bound = EmbeddedTenant.Bind(Scope.Value);
            var reads = await shell.ReadAsync(token);
            Assert.Empty((await reads.ListAsync(bound, new(), cancellationToken: token)).Items);
            await using var pinScope = await scopes.CreateAsyncScopeAsync(Scope);
            var pins = pinScope.ServiceProvider.GetRequiredService<IRuntimeKernelExecutablePinStore>();
            Assert.Empty(await pins.ListPinnedExecutableArtifactIdsAsync(token));

            using var native = await ExecuteAsync(alias.Identity.ArtifactId, new { sourceReferenceId = target });
            var completed = await VerifyAsync(native, alias, canonical.Identity, "blue", "authored-input", "authored-variable");
            using var flow = await ExecuteAsync(flowchart.Identity.ArtifactId, new { inputs = new { message = "flowchart-input" } }, tenant: "default");
            await VerifyAsync(flow, flowchart, flowchart.Identity, "default", "flowchart-input");
            Assert.Equal(new[] { canonical.Identity.ArtifactId, flowchart.Identity.ArtifactId }.Order(StringComparer.Ordinal),
                (await pins.ListPinnedExecutableArtifactIdsAsync(token)).Order(StringComparer.Ordinal));

            using var forbiddenDelete = await DeleteAsync(completed, manage: false);
            Assert.Equal(HttpStatusCode.Forbidden, forbiddenDelete.StatusCode);
            using var foreignDelete = await DeleteAsync(completed, manage: true, tenant: "another-tenant");
            Assert.Equal(HttpStatusCode.NotFound, foreignDelete.StatusCode);
            Assert.NotNull(await reads.GetAsync(bound, new(completed), token));
            await VerifyDeletedAsync(completed);

            var delayed = await StartWaitingAsync();
            await VerifyDeletedAsync(delayed);

            var reauthorized = await StartWaitingAsync();
            using var deniedPurge = await DeleteAsync(reauthorized, manage: true, subject: "deny-terminal-purge");
            Assert.Equal(HttpStatusCode.NotFound, deniedPurge.StatusCode);
            Assert.Equal(Contract.ActorStatus.Cancelled, (await reads.GetAsync(bound, new(reauthorized), token))?.Header.Status);
            await VerifyDeletedAsync(reauthorized);

            async Task<string> StartWaitingAsync()
            {
                using var delayed = await ExecuteAsync(waiting.Identity.ArtifactId, new { });
                Assert.Equal(HttpStatusCode.OK, delayed.StatusCode);
                var receipt = Assert.IsType<WorkflowExecutionStartDispatchView>(await delayed.Content.ReadFromJsonAsync<WorkflowExecutionStartDispatchView>(token));
                Contract.ActorView? actor;
                do
                {
                    await Task.Delay(50, token);
                    actor = await reads.GetAsync(bound, new(receipt.WorkflowExecutionId), token);
                } while (actor is null);
                Assert.Equal(Contract.ActorStatus.Running, actor.Header.Status);
                return receipt.WorkflowExecutionId;
            }

            async Task VerifyDeletedAsync(string run)
            {
                using var response = await DeleteAsync(run, manage: true);
                Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync(token));
                Assert.Equal(new WorkflowInstanceDeletionView(run), await response.Content.ReadFromJsonAsync<WorkflowInstanceDeletionView>(token));
                Assert.Null(await reads.GetAsync(bound, new(run), token));
            }

            async Task<HttpResponseMessage> DeleteAsync(string run, bool manage, string? tenant = null, string subject = "accepted")
            {
                using var request = new HttpRequestMessage(HttpMethod.Delete, $"/runtime/workflows/instances/{run}");
                request.Headers.Add("X-Fixture-Subject", subject);
                if (manage) request.Headers.Add("X-Fixture-Manage", "yes");
                if (tenant is not null) request.Headers.Add("X-Fixture-Tenant", tenant);
                return await http.SendAsync(request, token);
            }

            async Task<HttpResponseMessage> ExecuteAsync(string artifact, object body, string? tenant = null, string subject = "accepted")
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, $"/runtime/workflows/executables/{artifact}/execute")
                    { Content = JsonContent.Create(body) };
                request.Headers.Add("X-Fixture-Subject", subject);
                if (tenant is not null) request.Headers.Add("X-Fixture-Tenant", tenant);
                return await http.SendAsync(request, token);
            }

            async Task<string> VerifyAsync(HttpResponseMessage response, WorkflowExecutable source, WorkflowExecutableIdentity material, string slot,
                string message, string? seed = null)
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var receipt = Assert.IsType<WorkflowExecutionStartDispatchView>(await response.Content.ReadFromJsonAsync<WorkflowExecutionStartDispatchView>(token));
                Assert.Equal(nameof(WorkflowExecutionCommandDispatchStatus.Accepted), receipt.CommandDispatchStatus);
                Assert.Equal(material.ArtifactId, receipt.ArtifactId);
                Assert.Equal(material.ArtifactHash, receipt.ArtifactHash);
                Assert.Equal(source.Identity.ArtifactVersion, receipt.ArtifactVersion);
                Assert.Equal("publish-" + source.Identity.DefinitionId, receipt.PublicationId);
                Assert.Equal(WorkflowActivationSlotIdentity.Create(source.Identity.DefinitionId, slot), receipt.SlotId);
                var actor = new Contract.ActorRef(receipt.WorkflowExecutionId);
                Contract.ActorView? view;
                do
                {
                    await Task.Delay(50, token);
                    view = await reads.GetAsync(bound, actor, token);
                } while (view is null || view.Header.Status == Contract.ActorStatus.Running);
                Assert.Equal(Contract.ActorStatus.Finished, view.Header.Status);
                var selection = Assert.IsType<KernelExecutableSelection>(KernelExecutableSelection.Read(view.Header.Type, view.Header.TypeData));
                Assert.Equal(source.Identity.DefinitionId, selection.Definition);
                Assert.Equal(material, selection.Executable);
                var run = Kernel.WorkflowActor.WorkflowActor.DescribeRun(view.Header);
                Assert.Equal("accepted", run?.PublishedStart?.SubjectId);
                Assert.Equal(receipt.SourceReferenceId, run?.PublishedStart?.SourceReferenceId);
                var output = await Assert.IsAssignableFrom<Contract.IActorTerminalOutputReads>(reads).ReadTerminalOutputAsync(bound, actor,
                    view.Header.EndedAt!.Value, includeValues: true, cancellationToken: token);
                using var document = JsonDocument.Parse(output!.Values!);
                Assert.Equal(message, document.RootElement.GetProperty("message").GetString());
                if (seed is not null) Assert.Equal(seed, document.RootElement.GetProperty("seed").GetString());
                return actor.Id;
            }
        }
        finally { await shell.StopAsync(CancellationToken.None); }

        async Task PublishAsync(WorkflowExecutable executable, string slot)
        {
            var id = executable.Identity;
            var now = DateTimeOffset.UtcNow;
            var source = new WorkflowExecutableSourceReference("candidate", id.ArtifactId, "definition", id.DefinitionId, id.ArtifactVersion,
                id.DefinitionId, id.DefinitionVersionId, id.ArtifactVersion, now, now, WorkflowExecutableReferenceScope.Published);
            var coordinator = stores.GetRequiredService<IWorkflowActivationCoordinator>();
            var result = await coordinator.ActivateAsync(new(executable, source, slot, "publish-" + id.DefinitionId,
                WorkflowActivationSource.Publishing, 0), token);
            Assert.True(result.Succeeded, result.Diagnostic);
        }
    }

    private static WorkflowExecutable PublishedAs(WorkflowExecutable canonical, string definition, string version) =>
        new(canonical.Identity with { DefinitionId = definition, DefinitionVersionId = definition + "-v" + version, ArtifactVersion = version },
            canonical.RootActivity, canonical.ResumeTargets, canonical.CreatedAt, canonical.CompatibilityMetadata,
            canonical.InputContract, canonical.Dependencies, canonical.RuntimeRequirements, canonical.StorageDriverRequirements, canonical.IncidentStrategy,
            workflowVariables: canonical.WorkflowVariables);

    private sealed class FixtureAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new List<Claim>
            {
                new(IdentityClaimTypes.UserId, Request.Headers["X-Fixture-Subject"].ToString()),
                new(IdentityClaimTypes.Permission, "workflows/runtime:execute"),
                new(IdentityClaimTypes.Normalized, "v1")
            };
            if (Request.Headers.ContainsKey("X-Fixture-Manage")) claims.Add(new(IdentityClaimTypes.Permission, "workflows/runtime:manage"));
            if (Request.Headers.TryGetValue("X-Fixture-Tenant", out var tenant)) claims.Add(new(IdentityClaimTypes.TenantId, tenant.ToString()));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name)));
        }
    }

    private sealed class DeleteResourcePolicy : IPermissionResourceHandler
    {
        public ValueTask<PermissionEvaluationResult?> EvaluateAsync(PermissionEvaluationContext context, CancellationToken cancellationToken = default)
        {
            var deny = context.Resource is WorkflowExecutionState { Status: WorkflowExecutionStatus.Cancelled } &&
                context.Principal.FindFirstValue(IdentityClaimTypes.UserId) == "deny-terminal-purge";
            return ValueTask.FromResult<PermissionEvaluationResult?>(deny ? PermissionEvaluationResult.Denied() : null);
        }
    }

    private sealed class FixturePolicy : IWorkflowExecutableStartPolicy
    {
        public ValueTask<WorkflowExecutableStartDecision> EvaluateAsync(WorkflowExecutableStartPolicyContext context, CancellationToken cancellationToken)
        {
            Assert.Equal(WorkflowRunKind.PublishedRun, context.RunKind);
            Assert.Equal(WorkflowExecutableStartAuthorityKind.LiveReference, context.AuthorityKind);
            Assert.Equal(Scope.Value, context.Partition?.Value);
            var subject = context.Authority.Metadata["elsa.identity.subject-id"];
            return ValueTask.FromResult(subject == "deny" ? WorkflowExecutableStartDecision.Deny("denied", "Denied by fixture") :
                WorkflowExecutableStartDecision.Allow());
        }
    }

    private sealed class PinBoundary
    {
        public Func<WorkflowExecutableSourceReference, CancellationToken, ValueTask>? BeforeAdmission { get; set; }
        public bool Retracted { get; set; }
    }

    private sealed class FixturePins(IRuntimeKernelExecutablePinStore inner, PinBoundary boundary) : IRuntimeKernelExecutablePinStore
    {
        public async ValueTask<bool> TryPinPublishedAsync(RuntimeKernelExecutablePin pin, WorkflowActivationSlot slot,
            WorkflowExecutableSourceReference source, CancellationToken token)
        {
            if (boundary.BeforeAdmission is { } before) await before(source, token);
            return await inner.TryPinPublishedAsync(pin, slot, source, token);
        }

        public ValueTask<bool> TryPinAsync(IReadOnlyCollection<RuntimeKernelExecutablePin> pins, CancellationToken token) => inner.TryPinAsync(pins, token);
        public ValueTask<string?> ReadOpenTailAsync(PersistenceScope scope, CancellationToken token) => inner.ReadOpenTailAsync(scope, token);
        public ValueTask<RuntimeKernelExecutablePinPage> ListOpenAsync(PersistenceScope scope, string? after, string through, int limit,
            CancellationToken token) => inner.ListOpenAsync(scope, after, through, limit, token);
        public ValueTask<bool> TryReleaseAsync(RuntimeKernelExecutablePin pin,
            Func<RuntimeKernelExecutablePin, CancellationToken, ValueTask<RuntimeKernelExecutablePinProof>> confirmAsync,
            CancellationToken token) => inner.TryReleaseAsync(pin, confirmAsync, token);
        public ValueTask<IReadOnlyCollection<string>> ListPinnedExecutableArtifactIdsAsync(CancellationToken token) =>
            inner.ListPinnedExecutableArtifactIdsAsync(token);
    }

    private sealed class FixtureTypes : IWellKnownTypeRegistry
    {
        public void RegisterType(Type type, string alias) => throw new NotSupportedException();
        public bool TryGetAlias(Type type, out string alias) { alias = "String"; return type == typeof(string); }
        public bool TryGetType(string alias, out Type type) { type = typeof(string); return alias == "String"; }
        public IEnumerable<Type> ListTypes() => [typeof(string)];
        public string GetAliasOrDefault(Type type) => type == typeof(string) ? "String" : type.FullName!;
        public Type GetTypeOrDefault(string alias) => alias == "String" ? typeof(string) : typeof(object);
        public bool TryGetTypeOrDefault(string alias, out Type type) => TryGetType(alias, out type);
    }
}
