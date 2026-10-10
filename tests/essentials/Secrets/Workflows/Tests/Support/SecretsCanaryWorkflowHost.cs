using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using CShells;
using CShells.AspNetCore.Extensions;
using CShells.DependencyInjection;
using CShells.Lifecycle;
using Elsa.Activities.Design.Api;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore;
using Elsa.Activities.Design.Reconciliation;
using Elsa.Activities.Design.Reconciliation.Clr;
using Elsa.Activities.Http;
using Elsa.Activities.Primitives;
using Elsa.Activities.Runtime;
using Elsa.Api.Capabilities;
using Elsa.Canary.Fixtures;
using Elsa.Events;
using Elsa.Expressions;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Extensions;
using Elsa.Http;
using Elsa.Locking.Core;
using Elsa.Mediator;
using Elsa.Persistence.EntityFramework;
using Elsa.Primitives.Hosting;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Features;
using Elsa.Secrets.Workflows.Features;
using Elsa.Serialization.SystemText;
using Elsa.Tasks;
using Elsa.Workflows.Design.Api;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore;
using Elsa.Git;
using Elsa.Workflows.Design.Reconciliation.Git;
using Elsa.Workflows.Design.Reconciliation.Git.Contracts;
using Elsa.Workflows.Design.Validations;
using Elsa.Workflows.ExecutionEvidence;
using Elsa.Workflows.ExecutionEvidence.Contracts;
using Elsa.Workflows.ExecutionEvidence.Models;
using Elsa.Workflows.Publishing;
using Elsa.Workflows.Publishing.Api;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Http;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Resumption;
using Elsa.Workflows.Runtime.Scheduling;
using Elsa.Workflows.Runtime.Services.Incidents;
using Elsa.Workflows.Runtime.Tracing;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Secrets.Workflows.Tests.Support;

/// <summary>How the runtime runs a canary activity's schedule, start and invoke stages.</summary>
public enum CanaryStartMode
{
    /// <summary>Immediate checkpoint persistence: each stage is its own dispatch and its own commit.</summary>
    Discrete,

    /// <summary>
    /// Coalesced checkpoint persistence, under which the runtime fuses a ReplaySafe leaf's schedule, start and invoke
    /// stages into one dispatch (ADR 0047 D1).
    /// </summary>
    Fused
}

/// <summary>Who an inspector request is made as.</summary>
public enum CanaryCaller
{
    /// <summary>An operator holding every permission, the grants to inspect and to resolve captured values included.</summary>
    Operator,

    /// <summary>An operator who may inspect a run's structure but holds no grant over its captured values.</summary>
    StructureOnly
}

/// <summary>A canary definition as the design and publishing APIs stored and published it.</summary>
public sealed record CanaryPublication(string DefinitionId, string VersionId, string ArtifactId, string SourceReferenceId);

/// <summary>
/// The canary host (spec 188, T079): one CShells web host on a test server whose one shell composes what the canary
/// reads its eight surfaces from. Workflow design, its validations and its EF persistence and the activity catalog
/// with CLR reconciliation, on SQLite files under <see cref="DesignDirectory"/>; publishing; the runtime, its EF
/// persistence on a SQLite file under <see cref="RuntimeDirectory"/>, its engine tracing, its triggers and its API with
/// the run inspector and the executable inspector; Secrets with its encrypted store and the <c>SecretsWorkflows</c>
/// bridge; execution evidence; git export in the Writer role, into a clone of a temporary repository; and the
/// canary's test-only injection seams (<see cref="SecretsCanaryInjectionFeature"/>). Every log line of every category
/// is captured (<see cref="Logs"/>), and every span of the runtime's activity source is recorded (<see cref="Spans"/>).
/// </summary>
/// <remarks>
/// Its encryption key and signing keys are generated per host, and every file it writes is under one temporary
/// directory, which disposal deletes; a file a git or SQLite process still holds then is left to the system's cleanup.
/// </remarks>
public sealed class SecretsCanaryWorkflowHost : IAsyncDisposable
{
    public const string ShellName = "canary";
    public const string TenantId = PersistenceScope.DefaultValue;
    private const string AuthenticationScheme = "SecretsCanary";
    private const string CallerHeader = "X-Secrets-Canary-Caller";
    private const string GitBranch = "main";
    public const string GitWorkflowsPath = "workflows";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web) { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly WebApplication _app;
    private readonly DirectoryInfo _root;
    private readonly ConcurrentDictionary<Type, string> _activityVersionIds = new();

    private SecretsCanaryWorkflowHost(WebApplication app, IShell shell, DirectoryInfo root, CanaryStartMode mode, CanaryLogCapture logs, CanarySpanRecorder spans)
    {
        _app = app;
        _root = root;
        Shell = shell;
        Mode = mode;
        Logs = logs;
        Spans = spans;
        Client = app.GetTestClient();
    }

    public CanaryStartMode Mode { get; }
    public IShell Shell { get; }
    public HttpClient Client { get; }
    public CanaryLogCapture Logs { get; }
    public CanarySpanRecorder Spans { get; }
    public CanaryRecorder Activations => Shell.ServiceProvider.GetRequiredService<CanaryRecorder>();
    public CanaryInjections Injections => Shell.ServiceProvider.GetRequiredService<CanaryInjections>();
    public CanaryHttpEndpoint HttpEndpoint => Shell.ServiceProvider.GetRequiredService<CanaryHttpEndpoint>();
    public string DesignDirectory => Path.Join(_root.FullName, "design");
    public string RuntimeDirectory => Path.Join(_root.FullName, "runtime");
    public string GitClonePath => Path.Join(_root.FullName, "git-clone");
    private string GitRemotePath => Path.Join(_root.FullName, "git-remote.git");

    /// <summary>
    /// Starts a canary host, activates its shell, and waits for its startup tasks (catalog reconciliation among them).
    /// With <paramref name="withHttpActivities"/> the shell also composes <c>ActivitiesHttp</c>, whose named client sends
    /// to <see cref="HttpEndpoint"/> instead of the network (<see cref="SecretsCanaryHttpTransportFeature"/>).
    /// </summary>
    public static async Task<SecretsCanaryWorkflowHost> StartAsync(CanaryStartMode mode, bool withHttpActivities = false)
    {
        var root = Directory.CreateTempSubdirectory("elsa-secrets-canary-");
        var logs = new CanaryLogCapture();
        var spans = new CanarySpanRecorder();
        try
        {
            Directory.CreateDirectory(Path.Join(root.FullName, "design"));
            Directory.CreateDirectory(Path.Join(root.FullName, "runtime"));
            await File.WriteAllTextAsync(EmptyGitConfigPath(root.FullName), "");
            await CreateGitRemoteAsync(root.FullName, Path.Join(root.FullName, "git-remote.git"), Path.Join(root.FullName, "git-seed"));

            var configuration = new ConfigurationBuilder().AddInMemoryCollection(ShellSettings(root.FullName, mode, withHttpActivities)).Build();
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
            builder.Logging.SetMinimumLevel(LogLevel.Trace);
            builder.Logging.AddProvider(logs);
            builder.Configuration.AddConfiguration(configuration);
            builder.Services.AddSingleton<IConfiguration>(configuration);
            // Instances, not types: a shell copies the host's registrations into its own container, which would build its
            // own singleton from a type registration, and the tests read the ones the shell's activities and seams use.
            builder.Services.AddSingleton(new CanaryRecorder());
            builder.Services.AddSingleton(new CanaryInjections());
            builder.Services.AddSingleton(new CanaryHttpEndpoint());
            builder.Services.AddSingleton<IDistributedLockProvider, ProcessLockProvider>();
            // Registered ahead of the git feature's AddGitClient, which only adds a client when none is registered.
            builder.Services.AddSingleton<IGitClient>(new ConfigIsolatedGitClient(new GitClient("git", NullLogger.Instance), EmptyGitConfigPath(root.FullName)));
            builder.Services.AddFoundationIdentityAbstractions(options =>
                options.NormalizedAuthenticationTypes = new HashSet<string>(StringComparer.Ordinal) { AuthenticationScheme });
            builder.Services.AddAuthentication(AuthenticationScheme).AddScheme<AuthenticationSchemeOptions, CanaryAuthenticationHandler>(AuthenticationScheme, _ => { });
            builder.Services.AddAuthorization();
            builder.Services.AddCShellsAspNetCore(shells => shells
                .WithAssemblies(
                    typeof(PrimitivesFeature).Assembly,
                    typeof(SerializationFeature).Assembly,
                    typeof(MediatorFeature).Assembly,
                    typeof(EventsFeature).Assembly,
                    typeof(ExpressionsFeature).Assembly,
                    typeof(ApiCapabilitiesFeature).Assembly,
                    typeof(TasksFeature).Assembly,
                    typeof(ActivitiesRuntimeFeature).Assembly,
                    typeof(ActivitiesPrimitivesFeature).Assembly,
                    typeof(ActivitiesDesignApiFeature).Assembly,
                    typeof(ActivitiesDesignEntityFrameworkCoreFeature).Assembly,
                    typeof(ActivitiesDesignReconciliationFeature).Assembly,
                    typeof(ClrActivityReconciliationFeature).Assembly,
                    typeof(WorkflowDesignValidationsFeature).Assembly,
                    typeof(WorkflowsDesignApiFeature).Assembly,
                    typeof(WorkflowsDesignEntityFrameworkCoreFeature).Assembly,
                    typeof(WorkflowsDesignGitReconciliationFeature).Assembly,
                    typeof(WorkflowsPublishingFeature).Assembly,
                    typeof(WorkflowsPublishingApiFeature).Assembly,
                    typeof(PublishingEntityFrameworkCoreFeature).Assembly,
                    typeof(WorkflowsRuntimeApiFeature).Assembly,
                    typeof(RuntimeEntityFrameworkCoreFeature).Assembly,
                    typeof(WorkflowsRuntimeResumptionFeature).Assembly,
                    typeof(WorkflowsRuntimeRecurringTriggersFeature).Assembly,
                    typeof(WorkflowsRuntimeTracingFeature).Assembly,
                    typeof(SecretsFeature).Assembly,
                    typeof(SecretsWorkflowsFeature).Assembly,
                    typeof(WorkflowsExecutionEvidenceFeature).Assembly,
                    typeof(SecretsCanaryInjectionFeature).Assembly,
                    // The feature assemblies a host that composes the HTTP activities adds; a host that does not enable the
                    // features by name gets none of their services.
                    typeof(HttpFeature).Assembly,
                    typeof(WorkflowsRuntimeHttpFeature).Assembly,
                    typeof(ActivitiesHttpFeature).Assembly)
                .WithConfigurationProvider(configuration));

            var app = builder.Build();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapShells();
            await app.StartAsync();
            var shell = await app.Services.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName);
            return new SecretsCanaryWorkflowHost(app, shell, root, mode, logs, spans);
        }
        catch
        {
            spans.Dispose();
            DeleteQuietly(root);
            throw;
        }
    }

    /// <summary>The shell's features and their settings: every feature the canary reads a surface from, and its seams.</summary>
    private static Dictionary<string, string?> ShellSettings(string root, CanaryStartMode mode, bool withHttpActivities)
    {
        var features = $"CShells:Shells:{ShellName}:Features:";
        var settings = new Dictionary<string, string?>
        {
            [$"CShells:Shells:{ShellName}:Configuration:WebRouting:Path"] = "",
            [$"{features}ActivitiesDesignEntityFrameworkCore:ConnectionString"] = Sqlite(Path.Join(root, "design", "activities.db")),
            [$"{features}WorkflowsDesignEntityFrameworkCore:ConnectionString"] = Sqlite(Path.Join(root, "design", "workflows.db")),
            [$"{features}WorkflowsPublishingEntityFrameworkCore:ConnectionString"] = Sqlite(Path.Join(root, "design", "publishing.db")),
            [$"{features}WorkflowsRuntimeEntityFrameworkCore:ConnectionString"] = Sqlite(Path.Join(root, "runtime", "runtime.db")),
            [$"{features}WorkflowsRuntimeEntityFrameworkCore:RecoveryContinuationSigningKey"] = GeneratedKey(),
            [$"{features}WorkflowsRuntimeEntityFrameworkCore:HierarchyCursorSigningKey"] = GeneratedKey(),
            [$"{features}WorkflowsDesignGitReconciliation:RemoteUrl"] = Path.Join(root, "git-remote.git"),
            [$"{features}WorkflowsDesignGitReconciliation:Branch"] = GitBranch,
            [$"{features}WorkflowsDesignGitReconciliation:WorkflowsPath"] = GitWorkflowsPath,
            [$"{features}WorkflowsDesignGitReconciliation:LocalCachePath"] = Path.Join(root, "git-clone"),
            [$"{features}WorkflowsDesignGitReconciliation:Role"] = "Writer",
            [$"{features}Secrets:EncryptionKey"] = GeneratedKey()
        };
        if (mode == CanaryStartMode.Fused)
            settings[$"{features}WorkflowsRuntimeCheckpointPersistence:Mode"] = "Coalesced";

        foreach (var feature in Features.Concat(withHttpActivities ? HttpFeatures : []))
            settings.TryAdd($"{features}{feature}", null);
        return settings;
    }

    /// <summary>The features the canary shell enables by name. CShells adds what they depend on.</summary>
    private static readonly string[] Features =
    [
        "Primitives",
        "Serialization",
        "Mediator",
        "Events",
        "Expressions",
        "ApiCapabilities",
        "Tasks",
        "ActivitiesRuntime",
        "ActivitiesPrimitives",
        "ActivitiesDesignApi",
        "ActivitiesDesignEntityFrameworkCore",
        "ActivitiesDesignReconciliation",
        "ClrActivityReconciliation",
        "WorkflowDesignValidations",
        "WorkflowsDesignApi",
        "WorkflowsDesignEntityFrameworkCore",
        "WorkflowsDesignGitReconciliation",
        "WorkflowsPublishing",
        "WorkflowsPublishingApi",
        "WorkflowsPublishingEntityFrameworkCore",
        "WorkflowsRuntimeApi",
        "WorkflowsRuntimeTriggers",
        "WorkflowsRuntimeEntityFrameworkCore",
        "WorkflowsRuntimeResumption",
        "WorkflowsRuntimeRecurringTriggers",
        "WorkflowsRuntimeTracing",
        "Secrets",
        "SecretsWorkflows",
        "WorkflowsExecutionEvidence",
        SecretsCanaryInjectionFeature.Name
    ];

    /// <summary>The features a host with the HTTP activities adds: the activities, and the canary's endpoint standing in for the network.</summary>
    private static readonly string[] HttpFeatures = ["ActivitiesHttp", SecretsCanaryHttpTransportFeature.Name];

    // ---- Secrets -------------------------------------------------------------------------------------------------

    /// <summary>Creates a text secret in the host's tenant.</summary>
    public async Task CreateSecretAsync(string name, string value)
    {
        await using var scope = Shell.ServiceProvider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ISecretManager>().CreateAsync(
            TenantId,
            new CreateSecretRequest { Name = name, Value = value, TypeName = SecretTypeNames.Text });
    }

    /// <summary>Rotates the secret <paramref name="name"/> to <paramref name="value"/>.</summary>
    public async Task RotateSecretAsync(string name, string value)
    {
        await using var scope = Shell.ServiceProvider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ISecretManager>().RotateAsync(TenantId, name, new RotateSecretRequest { Value = value });
    }

    // ---- Design and publishing, through the APIs -----------------------------------------------------------------

    /// <summary>An authored input binding of the canary activity: a literal, or a secret reference as the Studio picker writes it.</summary>
    public static JsonObject Literal(string referenceKey, string value) => Input(referenceKey, JsonValue.Create(value), "Literal");

    public static JsonObject SecretReference(string referenceKey, string secretName) =>
        Input(referenceKey, new JsonObject { ["name"] = secretName, ["typeName"] = SecretTypeNames.Text }, "Secret");

    private static JsonObject Input(string referenceKey, JsonNode? value, string expressionType) => new()
    {
        ["referenceKey"] = referenceKey,
        ["value"] = new JsonObject { ["value"] = value, ["expressionType"] = expressionType },
        ["autoEvaluate"] = null,
        ["evaluatorType"] = null,
        ["storageDriverType"] = null,
        ["isSensitive"] = null
    };

    /// <summary>
    /// Submits a definition whose root is one canary activity node <paramref name="nodeId"/> with
    /// <paramref name="inputs"/>, through the Design API (which admits it through the credential-literal rule), publishes
    /// its version through the Publishing API, and returns both.
    /// </summary>
    public Task<CanaryPublication> PublishCanaryAsync(string name, string nodeId, params JsonObject[] inputs) =>
        PublishActivityAsync(name, nodeId, typeof(CanaryActivity), inputs);

    /// <summary>As <see cref="PublishCanaryAsync"/>, for a root node of the CLR activity <paramref name="activityType"/>.</summary>
    public async Task<CanaryPublication> PublishActivityAsync(string name, string nodeId, Type activityType, params JsonObject[] inputs)
    {
        var root = await RootNodeAsync(nodeId, activityType, inputs);
        var (definitionId, versionId) = await SubmitAsync(name, root);
        var published = await PostAsync($"publishing/workflows/{versionId}/publish", new JsonObject());
        return new CanaryPublication(definitionId, versionId, Required(published, "artifactId"), Required(published, "sourceReferenceId"));
    }

    /// <summary>A definition state whose root is one node of the CLR activity <paramref name="activityType"/>, as the Design API takes it.</summary>
    public async Task<JsonObject> StateOfAsync(string nodeId, Type activityType, params JsonObject[] inputs) =>
        State(await RootNodeAsync(nodeId, activityType, inputs));

    private async Task<JsonObject> RootNodeAsync(string nodeId, Type activityType, JsonObject[] inputs)
    {
        if (!_activityVersionIds.TryGetValue(activityType, out var activityVersionId))
            _activityVersionIds[activityType] = activityVersionId = await FindActivityVersionIdAsync(activityType);
        return new JsonObject
        {
            ["nodeId"] = nodeId,
            ["activityVersionId"] = activityVersionId,
            ["inputs"] = new JsonArray(inputs.Cast<JsonNode>().ToArray()),
            ["outputs"] = new JsonArray()
        };
    }

    private static JsonObject State(JsonObject root, JsonArray? variables = null) => new()
    {
        ["variables"] = variables ?? new JsonArray(),
        ["inputs"] = new JsonArray(),
        ["outputs"] = new JsonArray(),
        ["strategyOptions"] = null,
        ["rootActivity"] = root
    };

    /// <summary>Submits a definition whose root node is <paramref name="root"/>; returns the definition and version ids.</summary>
    public async Task<(string DefinitionId, string VersionId)> SubmitAsync(string name, JsonObject root, JsonArray? variables = null)
    {
        var submitted = await PostAsync("design/workflows/definitions/submit", new JsonObject
        {
            ["name"] = name,
            ["description"] = "Spec 188 canary",
            ["state"] = State(root, variables)
        });
        return (Required(submitted, "definition", "id"), Required(submitted, "version", "id"));
    }

    /// <summary>Publishes <paramref name="versionId"/> and returns the response, whatever its status.</summary>
    public async Task<HttpResponseMessage> TryPublishAsync(string versionId) =>
        await Send(HttpMethod.Post, $"publishing/workflows/{versionId}/publish", CanaryCaller.Operator, new JsonObject());

    /// <summary>The catalog's head version of the CLR activity <paramref name="activityType"/>.</summary>
    public async Task<string> FindActivityVersionIdAsync(Type activityType)
    {
        var typeKey = activityType.FullName!;
        var catalog = await GetJsonAsync($"design/activities/definitions?search={Uri.EscapeDataString(typeKey)}");
        var item = catalog["items"]?.AsArray().SingleOrDefault(candidate => (string?)candidate?["definition"]?["activityTypeKey"] == typeKey);
        return Required(item, "definition", "headVersionId");
    }

    // ---- Runs ----------------------------------------------------------------------------------------------------

    /// <summary>
    /// Starts the published canary through the runtime API and returns its workflow execution id. The dispatch must
    /// answer <paramref name="dispatchStatus"/>: <c>AcceptedButFaulted</c> when the run's drain records a handler fault.
    /// </summary>
    public async Task<string> ExecuteAsync(CanaryPublication publication, string dispatchStatus = "Accepted") =>
        await ExecuteAsync(publication.ArtifactId, publication.SourceReferenceId, dispatchStatus);

    public async Task<string> ExecuteAsync(string artifactId, string sourceReferenceId, string dispatchStatus = "Accepted")
    {
        var (run, status) = await StartAsync(artifactId, sourceReferenceId);
        Assert.Equal(dispatchStatus, status);
        return run;
    }

    /// <summary>Starts an artifact through the runtime API and returns its workflow execution id and the dispatch status, whatever it is.</summary>
    public async Task<(string WorkflowExecutionId, string? DispatchStatus)> StartAsync(string artifactId, string sourceReferenceId)
    {
        var started = await PostAsync($"runtime/workflows/executables/{artifactId}/execute", new JsonObject { ["sourceReferenceId"] = sourceReferenceId });
        return (Required(started, "workflowExecutionId"), (string?)started["commandDispatchStatus"]);
    }

    /// <summary>Delivers the stimulus the suspended canary activity of <paramref name="workflowExecutionId"/> waits for, through the runtime API.</summary>
    public async Task ResumeAsync(string workflowExecutionId)
    {
        var resumed = await PostAsync("runtime/workflows/stimuli", new JsonObject
        {
            ["stimulusType"] = CanaryActivity.StimulusType,
            ["stimulusHash"] = CanaryActivity.StimulusHash(workflowExecutionId),
            ["input"] = JsonSerializer.SerializeToNode(new CanaryWaitTrigger(true)),
            ["mode"] = "ResumeOnly"
        });
        Assert.Equal(1, (int?)resumed["resumedCount"]);
    }

    /// <summary>Saves <paramref name="executable"/> with a live published source reference, as a runtime artifact import does, without publishing it.</summary>
    public async Task<string> ImportAsync(WorkflowExecutable executable)
    {
        var sourceReferenceId = $"{TenantId}:{executable.Identity.ArtifactId}";
        await using var scope = Shell.ServiceProvider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(executable);
        await scope.ServiceProvider.GetRequiredService<IWorkflowExecutableSourceReferenceStore>().SaveAsync(new WorkflowExecutableSourceReference(
            SourceReferenceId: sourceReferenceId,
            ArtifactId: executable.Identity.ArtifactId,
            SourceKind: "WorkflowDefinitionVersion",
            SourceId: executable.Identity.DefinitionVersionId,
            SourceVersion: executable.Identity.ArtifactVersion,
            DefinitionId: executable.Identity.DefinitionId,
            DefinitionVersionId: executable.Identity.DefinitionVersionId,
            ArtifactVersion: executable.Identity.ArtifactVersion,
            CreatedAt: DateTimeOffset.UtcNow,
            PublishedAt: DateTimeOffset.UtcNow,
            Scope: WorkflowExecutableReferenceScope.Published,
            ExpiresAt: null,
            ActivationId: $"canary-import-{sourceReferenceId}",
            SlotId: $"canary-slot-{sourceReferenceId}"));
        return sourceReferenceId;
    }

    /// <summary>The committed activity states and workflow state of a run, read through the runtime stores.</summary>
    public async Task<(IReadOnlyCollection<ActivityExecutionState> Activities, WorkflowExecutionState? Workflow)> ReadRunAsync(string workflowExecutionId)
    {
        await using var scope = Shell.ServiceProvider.CreateAsyncScope();
        return (
            await scope.ServiceProvider.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(workflowExecutionId),
            await scope.ServiceProvider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(workflowExecutionId));
    }

    /// <summary>The executable artifact <paramref name="artifactId"/>, as the runtime store holds it.</summary>
    public async Task<WorkflowExecutable> ReadExecutableAsync(string artifactId)
    {
        await using var scope = Shell.ServiceProvider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>().FindAsync(artifactId)
               ?? throw new InvalidOperationException($"Artifact '{artifactId}' is not stored.");
    }

    /// <summary>The incidents recorded for a run.</summary>
    public async Task<IReadOnlyCollection<IncidentState>> ReadIncidentsAsync(string workflowExecutionId)
    {
        await using var scope = Shell.ServiceProvider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IIncidentStateStore>().ListAsync(workflowExecutionId);
    }

    /// <summary>A run's committed state, as the settle predicates read it.</summary>
    public sealed record RunState(IReadOnlyCollection<ActivityExecutionState> Activities, WorkflowExecutionState? Workflow)
    {
        public ActivityExecutionState? Node(string nodeId) => Activities.SingleOrDefault(activity => activity.Execution.ExecutableNodeId == nodeId);
    }

    public static bool Completed(RunState run) => run.Workflow?.Status == WorkflowExecutionStatus.Completed;

    /// <summary>
    /// Generous because the tests run on a shared machine whose parallel sessions compete for the CPU, which stretches
    /// every timing (AGENTS.md, "Builds on a shared machine"). No scenario relies on reaching it: a fault fails the wait
    /// at once.
    /// </summary>
    public static readonly TimeSpan SettleTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Polls the run's committed state until <paramref name="settled"/> holds; the drain may outlive the request that
    /// started it. A run that records an activity fault or an incident <paramref name="settled"/> does not accept fails
    /// at once, naming that state, rather than waiting out the timeout: a handler fault (poisoned work) and an activity
    /// fault both record one. The incidents are read before the run state, so an incident committed with an activity
    /// fault is never seen without that fault. A checkpoint-rule incident does not fail the wait: it records a commit the
    /// backstop refused, which the settle predicates of S5 and S6 judge through the refusal's log line, written after it.
    /// </summary>
    public async Task<RunState> WaitForAsync(string workflowExecutionId, Func<RunState, bool> settled)
    {
        var deadline = Stopwatch.StartNew();
        while (true)
        {
            var incidents = await ReadIncidentsAsync(workflowExecutionId);
            var (activities, workflow) = await ReadRunAsync(workflowExecutionId);
            var state = new RunState(activities, workflow);
            if (settled(state))
                return state;
            var describedState = $"workflow {workflow?.Status}, activities {string.Join(", ", activities.Select(activity => $"{activity.Execution.ExecutableNodeId}={activity.Status}{(activity.Fault is { } fault ? $" (fault {fault.Code})" : string.Empty)}"))}";
            var faults = incidents.Where(incident => incident.FailureType != CheckpointRuleViolationWorkflowFaulter.IncidentFailureType)
                .Select(incident => $"{incident.FailureType} on node {incident.ExecutableNodeId ?? "(none)"}")
                .Concat(activities.Where(activity => activity.Fault is not null).Select(activity => $"activity fault on node {activity.Execution.ExecutableNodeId}"))
                .ToArray();
            Assert.True(faults.Length == 0, $"Run '{workflowExecutionId}' faulted before it settled: {string.Join("; ", faults)}; {describedState}.");
            Assert.True(deadline.Elapsed < SettleTimeout, $"Run '{workflowExecutionId}' did not settle: {describedState}.");
            await Task.Delay(100);
        }
    }

    /// <summary>Saves the runtime diagnostics settings through the runtime API: <paramref name="level"/> by default and for every subject.</summary>
    public async Task SaveDiagnosticsLevelAsync(RuntimeDiagnosticsEvidenceLevel level)
    {
        var overrides = new JsonObject();
        foreach (var subject in RuntimeDiagnosticsSubjects.All)
            overrides[subject] = level.ToString();
        using var response = await Send(HttpMethod.Put, "runtime/workflows/diagnostics/settings", CanaryCaller.Operator, new JsonObject
        {
            ["defaultLevel"] = level.ToString(),
            ["subjectOverrides"] = overrides
        });
        Assert.True(response.IsSuccessStatusCode, $"Saving the diagnostics settings returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var accessor = Shell.ServiceProvider.GetRequiredService<IRuntimeDiagnosticsSettingsAccessor>();
        Assert.All(Enum.GetValues<RuntimePayloadCaptureSubject>(), subject => Assert.Equal(level, accessor.GetEffectiveLevel(subject)));
    }

    // ---- Surfaces ------------------------------------------------------------------------------------------------

    /// <summary>
    /// Every cell of every table of every SQLite database under <paramref name="directory"/>, read as the stores wrote it
    /// and, for a compressed payload frame, as the EF stores decode it. Unlike a scan of the files, it reads a cell whole
    /// even when SQLite split it across overflow pages.
    /// </summary>
    public static async Task<IReadOnlyList<CanaryContent>> ReadDatabaseCellsAsync(string directory, IReadOnlySet<string>? excludedTables = null) =>
        await ReadDatabaseCellsAsync(directory, table => excludedTables?.Contains(table) != true);

    /// <summary>Every cell of the tables <paramref name="includeTable"/> selects, read as <see cref="ReadDatabaseCellsAsync(string, IReadOnlySet{string}?)"/> reads them.</summary>
    public static async Task<IReadOnlyList<CanaryContent>> ReadDatabaseCellsAsync(string directory, Func<string, bool> includeTable)
    {
        var cells = new List<CanaryContent>();
        foreach (var database in Directory.EnumerateFiles(directory, "*.db"))
        {
            await using var connection = new SqliteConnection($"Data Source={database};Mode=ReadOnly;Pooling=False");
            await connection.OpenAsync();
            foreach (var table in (await ReadStringsAsync(connection, "SELECT name FROM sqlite_master WHERE type = 'table'")).Where(includeTable))
            {
                await using var command = connection.CreateCommand();
                command.CommandText = $"SELECT * FROM \"{table}\"";
                await using var reader = await command.ExecuteReaderAsync();
                var row = 0;
                while (await reader.ReadAsync())
                {
                    for (var column = 0; column < reader.FieldCount; column++)
                    {
                        var location = $"{Path.GetFileName(database)}:{table}[{row}].{reader.GetName(column)}";
                        switch (reader.GetValue(column))
                        {
                            case string text:
                                cells.Add(CanaryContent.Text(location, text));
                                if (EfPayloadCodec.IsFrame(text))
                                    cells.Add(CanaryContent.Text($"{location}(decoded)", EfPayloadCodec.Decode(text)!));
                                break;
                            case byte[] bytes:
                                cells.Add(new CanaryContent(location, bytes));
                                break;
                        }
                    }

                    row++;
                }
            }
        }

        return cells;
    }

    /// <summary>The names of the tables of the SQLite database <paramref name="database"/>.</summary>
    public static async Task<IReadOnlyList<string>> ReadTableNamesOfAsync(string database)
    {
        await using var connection = new SqliteConnection($"Data Source={database};Mode=ReadOnly;Pooling=False");
        await connection.OpenAsync();
        return await ReadStringsAsync(connection, "SELECT name FROM sqlite_master WHERE type = 'table'");
    }

    /// <summary>Runs the Writer's export pass, as its startup task does, so the export tree reflects the catalog now.</summary>
    public async Task ExportToGitAsync()
    {
        await using var scope = Shell.ServiceProvider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IGitWorkflowExporter>().ExportAsync(CancellationToken.None);
    }

    /// <summary>The export branch's history with every patch, as text: git stores blobs compressed, so its object files show nothing.</summary>
    public async Task<string> ReadGitHistoryAsync() => await RunGitAsync(_root.FullName, GitClonePath, "log", "-p", "--all", "--no-color");

    /// <summary>The export tree's files, outside the clone's own <c>.git</c> folder.</summary>
    public IReadOnlyList<string> GitTreeFiles() =>
        Directory.EnumerateFiles(GitClonePath, "*", SearchOption.AllDirectories)
            .Where(file => !Path.GetRelativePath(GitClonePath, file).StartsWith(".git", StringComparison.Ordinal))
            .ToArray();

    /// <summary>The evidence records of a run, as the evidence store holds them; none in a host that composes no evidence store.</summary>
    public IReadOnlyList<ExecutionEvidenceRecord> EvidenceRecords(string workflowExecutionId) =>
        Shell.ServiceProvider.GetService<IExecutionEvidenceStore>()?.List(workflowExecutionId, afterSequence: 0).Records ?? [];

    /// <summary>The inspection projections of a run's activity executions, read through the inspection store.</summary>
    public async Task<IReadOnlyList<ActivityExecutionInspectionProjection>> ReadInspectionsAsync(string workflowExecutionId)
    {
        await using var scope = Shell.ServiceProvider.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IActivityExecutionInspectionStore>();
        var lookups = new List<ActivityExecutionInspectionProjection?>();
        foreach (var state in await scope.ServiceProvider.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(workflowExecutionId))
            lookups.Add(await store.FindAsync(workflowExecutionId, state.InvocationId));

        return lookups.Where(projection => projection is not null).Select(projection => projection!).ToList();
    }

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Web);

    // ---- HTTP ----------------------------------------------------------------------------------------------------

    public async Task<HttpResponseMessage> Send(HttpMethod method, string path, CanaryCaller caller, JsonNode? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add(CallerHeader, caller.ToString());
        if (body is not null)
            request.Content = JsonContent.Create(body, options: Web);
        return await Client.SendAsync(request);
    }

    public async Task<JsonNode> GetJsonAsync(string path)
    {
        using var response = await Send(HttpMethod.Get, path, CanaryCaller.Operator);
        var content = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"GET {path} returned {(int)response.StatusCode}: {content}");
        return JsonNode.Parse(content)!;
    }

    public async Task<JsonNode> PostAsync(string path, JsonNode body)
    {
        using var response = await Send(HttpMethod.Post, path, CanaryCaller.Operator, body);
        var content = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"POST {path} returned {(int)response.StatusCode}: {content}");
        return JsonNode.Parse(content)!;
    }

    public static string Required(JsonNode? node, params string[] path)
    {
        foreach (var segment in path)
            node = node?[segment];
        return (string?)node ?? throw new InvalidOperationException($"The response lacks '{string.Join('.', path)}'.");
    }

    /// <summary>Stops the host; the span listener is detached and the temporary directory deleted even when stopping the app throws.</summary>
    public async ValueTask DisposeAsync()
    {
        try
        {
            Client.Dispose();
            await _app.DisposeAsync();
        }
        finally
        {
            Spans.Dispose();
            SqliteConnection.ClearAllPools();
            DeleteQuietly(_root);
        }
    }

    // ---- Setup helpers -------------------------------------------------------------------------------------------

    private static string Sqlite(string path) => $"Data Source={path};Pooling=False";

    /// <summary>A key generated per host: plain hex, at least 32 bytes, so no key material is written into a test.</summary>
    private static string GeneratedKey() => $"{Guid.NewGuid():N}{Guid.NewGuid():N}";

    /// <summary>A bare repository whose <c>main</c> branch has one commit, so the Writer's clone can check it out.</summary>
    private static async Task CreateGitRemoteAsync(string root, string remote, string seed)
    {
        Directory.CreateDirectory(remote);
        Directory.CreateDirectory(seed);
        await RunGitAsync(root, remote, "init", "--bare", "-b", GitBranch);
        await RunGitAsync(root, seed, "init", "-b", GitBranch);
        await File.WriteAllTextAsync(Path.Join(seed, "README.md"), "canary");
        await RunGitAsync(root, seed, "add", "README.md");
        await RunGitAsync(root, seed, "-c", "user.email=canary@elsa.local", "-c", "user.name=Canary", "commit", "-m", "init");
        await RunGitAsync(root, seed, "remote", "add", "origin", remote);
        await RunGitAsync(root, seed, "push", "origin", GitBranch);
    }

    private static async Task<string> RunGitAsync(string root, string workingDirectory, params string[] arguments)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = workingDirectory, RedirectStandardOutput = true, RedirectStandardError = true };
        ApplyEmptyGitConfig(start.Environment, EmptyGitConfigPath(root));
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed ({process.ExitCode}): {await error}");
        return await output;
    }

    private static async Task<IReadOnlyList<string>> ReadStringsAsync(SqliteConnection connection, string query)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = query;
        await using var reader = await command.ExecuteReaderAsync();
        var values = new List<string>();
        while (await reader.ReadAsync())
            values.Add(reader.GetString(0));
        return values;
    }

    private static void DeleteQuietly(DirectoryInfo directory)
    {
        try
        {
            directory.Delete(recursive: true);
        }
        catch (IOException)
        {
            // A git or SQLite process the host started can still hold a file for a moment; the temp folder is cleaned later.
        }
        catch (UnauthorizedAccessException)
        {
            // Git writes its object files read-only on some platforms; the temp folder is cleaned later.
        }
    }

    /// <summary>The empty file a host points git's global and system config at, so each OS reads an empty config.</summary>
    private static string EmptyGitConfigPath(string root) => Path.Join(root, "git-empty.config");

    /// <summary>
    /// Points git's global and system config at <paramref name="emptyConfig"/> (a path, so it works on every OS, unlike
    /// <c>/dev/null</c>), as the GitOps tests isolate from the developer's config: a global <c>commit.gpgsign = true</c>
    /// would make each commit ask a GPG agent to sign. The export commits carry their identity as <c>-c user.*</c> arguments
    /// (<c>GitExportIdentity.CommitArgs</c>) and the seed commit does too, so nothing needs a configured identity.
    /// </summary>
    private static void ApplyEmptyGitConfig(IDictionary<string, string?> environment, string emptyConfig)
    {
        environment["GIT_CONFIG_GLOBAL"] = emptyConfig;
        environment["GIT_CONFIG_SYSTEM"] = emptyConfig;
        environment["GIT_CONFIG_NOSYSTEM"] = "1";
    }

    /// <summary>
    /// The git client of one host: every <see cref="IGitClient"/> member starts its git process with git's global and
    /// system config pointed at an empty file. <c>RunAsync</c> goes through the feature's own client with that environment;
    /// the two synchronous members have no environment overload, so they start the process here through
    /// <see cref="GitClient.CreateStartInfo"/> (which adds <c>GIT_TERMINAL_PROMPT=0</c>) and mirror
    /// <see cref="GitClient.RunOrDefault"/> and <see cref="GitClient.IsGitRepository"/>: trimmed output on exit code 0, an
    /// empty string on any other exit or a failed start, and the same <c>rev-parse</c> test for a repository.
    /// </summary>
    private sealed class ConfigIsolatedGitClient(IGitClient inner, string emptyConfig) : IGitClient
    {
        public Task<string> RunAsync(string workingDirectory, CancellationToken cancellationToken, params string[] arguments) =>
            RunAsync(workingDirectory, new Dictionary<string, string>(), cancellationToken, arguments);

        public Task<string> RunAsync(string workingDirectory, IReadOnlyDictionary<string, string> environment, CancellationToken cancellationToken, params string[] arguments) =>
            inner.RunAsync(workingDirectory, Isolate(environment), cancellationToken, arguments);

        public string RunOrDefault(string workingDirectory, params string[] arguments)
        {
            try
            {
                using var process = Process.Start(GitClient.CreateStartInfo("git", workingDirectory, arguments, Isolate(new Dictionary<string, string>())))
                    ?? throw new InvalidOperationException("Could not start 'git'.");
                var output = process.StandardOutput.ReadToEnd();
                _ = process.StandardError.ReadToEnd();
                process.WaitForExit();
                return process.ExitCode == 0 ? output.Trim() : "";
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or IOException)
            {
                return "";
            }
        }

        public bool IsGitRepository(string repositoryPath) =>
            RunOrDefault(repositoryPath, "rev-parse", "--is-inside-work-tree", "--show-prefix") == "true";

        /// <summary>The isolation variables, under the caller's own, which win.</summary>
        private Dictionary<string, string> Isolate(IReadOnlyDictionary<string, string> environment)
        {
            var isolated = new Dictionary<string, string?>();
            ApplyEmptyGitConfig(isolated, emptyConfig);
            foreach (var (name, value) in environment)
                isolated[name] = value;
            return isolated.ToDictionary(pair => pair.Key, pair => pair.Value!);
        }
    }

    /// <summary>A lock provider for one process, which is all one canary host needs.</summary>
    private sealed class ProcessLockProvider : IDistributedLockProvider
    {
        public IDistributedSynchronizationHandle? TryAcquireLock(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => new Handle();
        public ValueTask<IDistributedSynchronizationHandle?> TryAcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => ValueTask.FromResult<IDistributedSynchronizationHandle?>(new Handle());
        public ValueTask<IDistributedSynchronizationHandle> AcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => ValueTask.FromResult<IDistributedSynchronizationHandle>(new Handle());

        private sealed class Handle : IDistributedSynchronizationHandle
        {
            public CancellationToken HandleLostToken => CancellationToken.None;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// Authenticates every request as an operator of the host's tenant: with every permission, or, for
    /// <see cref="CanaryCaller.StructureOnly"/>, with the grants to read a run and inspect its structure and none over
    /// its captured values.
    /// </summary>
    private sealed class CanaryAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        private static readonly string[] StructureOnlyPermissions =
        [
            Elsa.Workflows.Runtime.Api.Authorization.WorkflowRuntimePermissions.WorkflowRuntimeRead,
            Elsa.Workflows.Runtime.Api.Authorization.WorkflowRuntimePermissions.WorkflowPublishingRead,
            Elsa.Workflows.Runtime.Api.Services.HttpContextActivityExecutionInspectionAuthorizationContext.StructurePermission
        ];

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var caller = Enum.Parse<CanaryCaller>(Request.Headers[CallerHeader].ToString());
            var permissions = caller == CanaryCaller.Operator ? [PermissionKey.Wildcard] : StructureOnlyPermissions;
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, $"canary-{caller}"),
                new(IdentityClaimTypes.Normalized, "v1"),
                new(IdentityClaimTypes.TenantId, TenantId)
            };
            claims.AddRange(permissions.Select(permission => new Claim(IdentityClaimTypes.Permission, permission)));
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
        }
    }
}
