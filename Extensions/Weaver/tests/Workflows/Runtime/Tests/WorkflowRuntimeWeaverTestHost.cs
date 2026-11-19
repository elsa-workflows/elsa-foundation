using System.Text.Json;
using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;
using Elsa.Weaver.Extensions;
using Elsa.Events;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Weaver.Workflows.Runtime.Tests;

/// <summary>A Runtime-only Weaver host: the Weaver foundation, the Runtime-side workflow package and one fixture Executable and Run inspection each.</summary>
internal sealed class WorkflowRuntimeWeaverTestHost : IDisposable
{
    internal const string ModuleId = "Elsa.Studio.Workflows";
    internal const string Hash = "sha256:artifact-1";
    private readonly ServiceProvider root;
    private readonly IServiceScope scope;

    public WorkflowRuntimeWeaverTestHost(FixtureInspection? inspection = null, IWeaverToolAuthorizer? authorizer = null, Action<IServiceCollection>? configure = null,
        FixtureRunInspection? runs = null)
    {
        Inspection = inspection ?? new FixtureInspection(Executable());
        Runs = runs ?? new FixtureRunInspection();
        var services = new ServiceCollection();
        services.AddLogging();
        new EventsFeature().ConfigureServices(services);
        services.AddWeaver();
        new WeaverWorkflowsRuntimeFeature().ConfigureServices(services);
        services.Replace(ServiceDescriptor.Scoped<IWeaverToolAuthorizer>(_ => authorizer ?? new Authorizer()));
        services.AddSingleton<IWorkflowExecutableInspection>(Inspection);
        services.AddSingleton<IWorkflowRunInspection>(Runs);
        configure?.Invoke(services);
        root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        scope = root.CreateScope();
    }

    public FixtureInspection Inspection { get; }
    public FixtureRunInspection Runs { get; }
    public IServiceProvider Services => scope.ServiceProvider;
    public IWeaverContextProvider Source => Services.GetServices<IWeaverContextProvider>().Single(item => item.Descriptor.Id == "workflows.executable-context");
    public IWeaverTool Tool => Services.GetServices<IWeaverTool>().Single(item => item.Descriptor.Name == "workflow_read_executable");
    public IWeaverContextProvider RunSource => Services.GetServices<IWeaverContextProvider>().Single(item => item.Descriptor.Id == "workflows.run-context");
    public IWeaverTool RunTool => Services.GetServices<IWeaverTool>().Single(item => item.Descriptor.Name == "workflow_read_run");

    public async Task<WeaverSession> SessionAsync(string mode = "explain") =>
        await Services.GetRequiredService<IWeaverSessionService>().CreateAsync(new("tenant-1", "actor-1", "conversation-1", "test-provider", mode, null,
            WeaverPolicy.Default, new Dictionary<string, string>()));

    public async Task<WeaverContextContribution> CollectAsync(WeaverSession session, params WeaverContextAttachment[] callers) =>
        await Source.CollectAsync(new(session.Id, "workflow-executable", new Dictionary<string, string>()) { CallerAttachments = callers });

    public async Task<WeaverContextContribution> CollectRunAsync(WeaverSession session, params WeaverContextAttachment[] callers) =>
        await RunSource.CollectAsync(new(session.Id, "workflow-run", new Dictionary<string, string>()) { CallerAttachments = callers });

    public async Task<WeaverResult<WeaverToolPreparation>> PrepareRunAsync(WeaverSession session, IReadOnlyCollection<WeaverContextAttachment> context,
        IReadOnlyDictionary<string, object?>? arguments = null) =>
        await RunTool.PrepareAsync(new("call-1", "workflow_read_run", session.Id, session.ActorId, arguments ?? new Dictionary<string, object?>(), context)
            { TenantId = session.TenantId });

    public async Task<WeaverResult<WeaverToolPreparation>> PrepareAsync(WeaverSession session, IReadOnlyCollection<WeaverContextAttachment> context,
        IReadOnlyDictionary<string, object?>? arguments = null) =>
        await Tool.PrepareAsync(new("call-1", "workflow_read_executable", session.Id, session.ActorId, arguments ?? new Dictionary<string, object?>(), context)
            { TenantId = session.TenantId });

    public void Dispose()
    {
        scope.Dispose();
        root.Dispose();
    }

    /// <summary>The Executable Inspector's reference to one exact Executable, bound to the hash it shows.</summary>
    public static WeaverContextAttachment Reference(string artifactId = "artifact-1", string hash = Hash, object? content = null) =>
        new($"workflows.executable:{artifactId}", "workflow-executable", artifactId, "Executable", "workflows.executable", WeaverContextSensitivity.Internal, "screen",
            "Workflow Executable", JsonSerializer.SerializeToElement(content ?? new { artifactId, artifactHash = hash, sourceReferenceId = (string?)null }),
            new Dictionary<string, string>())
        {
            ModuleId = ModuleId, SchemaVersion = "1", Target = new(ModuleId, "workflow-executable", artifactId, hash), Origin = WeaverContextOrigin.ClientSnapshot
        };

    /// <summary>The run page's reference to one exact Run and, where one is selected, one activity execution of it.</summary>
    public static WeaverContextAttachment RunReference(string workflowExecutionId = "run-1", string? activityExecutionId = "act-write", object? content = null) =>
        new($"workflows.run:{workflowExecutionId}", "workflow-run", workflowExecutionId, "Run", "workflows.run", WeaverContextSensitivity.Internal, "screen",
            "Workflow Run", JsonSerializer.SerializeToElement(content ?? new { workflowExecutionId, activityExecutionId }), new Dictionary<string, string>())
        {
            ModuleId = ModuleId, SchemaVersion = "1", Target = new(ModuleId, "workflow-run", workflowExecutionId, activityExecutionId ?? workflowExecutionId),
            Origin = WeaverContextOrigin.ClientSnapshot
        };

    internal const string Canary = "CANARY-RUN-VALUE-0f4b8f9a2c6d4e1b";
    internal static readonly DateTimeOffset ReadAt = new(2026, 11, 3, 12, 30, 0, TimeSpan.Zero);

    /// <summary>
    /// A Run of artifact-1: a WriteLine whose sensitive input is withheld, then a Fault that faulted it with one blocking incident.
    /// The selected WriteLine execution holds a withheld input (whose fixture value is a canary no read may disclose), a
    /// resolved output, an input the caller may not resolve and one the runtime did not capture; its state view is unsupported.
    /// <c>act-pruned</c> is recorded by the Run (an incident names it) but no longer inspectable.
    /// </summary>
    public static WorkflowRunInspectionDetails Run(WorkflowRunInspectionRequest request, bool inProgress = false, bool canInspectValues = true, int activities = 2,
        int incidents = 1, bool resolved = true, bool canResolveValues = true, int bookmarks = 0, string? message = null, string? resultValue = null)
    {
        var release = canInspectValues && canResolveValues && resolved && request.ResolveEvidenceIds?.Contains("evidence-result") == true;
        var at = new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);
        var status = inProgress ? "Running" : "Faulted";
        var write = new WorkflowRunInspectionActivitySummary("act-write", "write", "write", "Elsa.Activities.Primitives.Activities.WriteLine", "1.0.0", "Completed",
            null, 1, at, at, at, ["Done"], 0, 0, 4);
        var summaries = Enumerable.Range(0, activities).Select(index => index switch
        {
            0 => write,
            1 => new WorkflowRunInspectionActivitySummary("act-fault", "fault", "fault", "Elsa.Activities.Primitives.Activities.Fault", "1.0.0", "Faulted", null, 2,
                at, at, null, [], 0, 1, 1),
            _ => new WorkflowRunInspectionActivitySummary($"act-{index}", $"node-{index}", $"node-{index}", "Elsa.Activities.Primitives.Activities.WriteLine", "1.0.0",
                "Completed", null, index + 1, at, at, at, ["Done"], 0, 0, 1)
        }).ToArray();
        var recorded = Enumerable.Range(0, incidents).Select(index => new WorkflowRunInspectionIncident(index == 0 ? "incident-1" : $"incident-{index + 1}",
            index == 0 ? "act-fault" : "act-pruned", "fault", "Error", "Active", "workflow.fault",
            canInspectValues ? message ?? "Qualification fault." + new string('x', index == 0 ? 0 : 400) : "Incident details are redacted.", !canInspectValues, false,
            true, at, null)).ToArray();
        WorkflowRunInspectionSelection? selection = request.ActivityExecutionId switch
        {
            null => null,
            "act-write" => new("act-write", new(write, "root", "root", 1,
                Enumerable.Range(0, bookmarks).Select(index => new WorkflowRunInspectionBookmark($"bookmark-{index:D4}-{new string('b', 40)}", "Elsa.Event", at, null)).ToArray(),
                [], [
                    Value("text", "ActivityInput", WorkflowRunValueDisclosure.Withheld, null, JsonSerializer.SerializeToElement(Canary), sensitive: true),
                    Value("result", "ActivityOutput", release ? WorkflowRunValueDisclosure.Resolved : WorkflowRunValueDisclosure.Resolvable,
                        null, release ? resultValue is null ? JsonSerializer.SerializeToElement(new { ok = true }) : JsonSerializer.SerializeToElement(resultValue) : null),
                    Value("limited", "ActivityInput", WorkflowRunValueDisclosure.Unavailable, WorkflowRunUnavailableReason.NotPermitted),
                    Value("skipped", "ActivityInput", WorkflowRunValueDisclosure.Unavailable, WorkflowRunUnavailableReason.NotCaptured)
                ], new(false, WorkflowRunUnavailableReason.Unsupported, null, null, null)), null),
            "act-pruned" => new("act-pruned", null, WorkflowRunUnavailableReason.Pruned),
            var other => new(other, null, null)
        };
        return new(new(request.WorkflowExecutionId, "artifact-1", Hash, "definition-1", "version-1", "1.0.0", status, null, "Production", !inProgress,
                at, at, at, inProgress ? null : at, null, activities, incidents, incidents, incidents, "published-1", "publication-1"),
            recorded, new(summaries.Take(request.ActivityPageSize).ToArray(), activities, activities > request.ActivityPageSize ? "next" : null), selection,
            canInspectValues, canInspectValues && canResolveValues, "Durable", "activity-level", ReadAt);
    }

    private static WorkflowRunInspectionValue Value(string key, string subject, WorkflowRunValueDisclosure disclosure, WorkflowRunUnavailableReason? reason,
        JsonElement? value = null, bool sensitive = false) =>
        new("evidence-" + key, key, subject, subject == "ActivityInput" ? key : null, sensitive ? "None" : "DiagnosticSnapshot",
            sensitive ? "notCaptured" : disclosure == WorkflowRunValueDisclosure.Unavailable && reason == WorkflowRunUnavailableReason.NotCaptured ? "notCaptured" : "diagnosticSnapshotCaptured",
            disclosure == WorkflowRunValueDisclosure.Resolved || disclosure == WorkflowRunValueDisclosure.Resolvable ? "resolutionAvailable"
                : reason == WorkflowRunUnavailableReason.NotPermitted ? "resolutionPermissionRequired" : "unavailable",
            sensitive, new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero), sensitive ? "Sensitive runtime payloads are excluded by default." : "Diagnostic snapshot captured by runtime diagnostics policy.",
            "primitive", "String", null, null, disclosure, reason, value);

    /// <summary>The Runtime's Run read, over <see cref="Run"/>; a Run outside <see cref="Known"/> reads as absent.</summary>
    internal sealed class FixtureRunInspection : IWorkflowRunInspection
    {
        public HashSet<string> Known { get; } = ["run-1"];
        public Func<WorkflowRunInspectionRequest, WorkflowRunInspectionDetails?>? Read { get; set; }
        public List<WorkflowRunInspectionRequest> Requests { get; } = [];

        public ValueTask<WorkflowRunInspectionDetails?> GetAsync(WorkflowRunInspectionRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return ValueTask.FromResult(!Known.Contains(request.WorkflowExecutionId) ? null : Read is null ? Run(request) : Read(request));
        }
    }

    /// <summary>A root flowchart with two children connected Done -> In, one sensitive binding, and two Source References.</summary>
    public static WorkflowExecutableInspectionDetails Executable(string hash = Hash, int children = 2) =>
        new("artifact-1", hash, new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero), "Elsa.Flowchart", "1.0.0", children + 1, 2,
            new("root", "root", "Elsa.Flowchart", "1.0.0", "elsa.flowchart.structure", [new("secret-key", "Literal", true)],
                [new("activities", Enumerable.Range(1, children).Select(index => new WorkflowExecutableInspectionNode($"node-{index}", $"node-{index}", "Elsa.WriteLine", "1.0.0", null,
                    [new("text", "Literal", false)], [], [])).ToArray())],
                children > 1 ? [new("node-1", "Done", "node-2", "In")] : []),
            [
                new("published-1", "Published", "WorkflowDefinitionVersion", "version-1", "1.0.0", "definition-1", "version-1", "1.0.0",
                    new DateTimeOffset(2026, 11, 3, 11, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 11, 3, 11, 0, 0, TimeSpan.Zero), null, null, true),
                new("published-2", "Published", "WorkflowDefinitionVersion", "version-1", "1.0.0", "definition-1", "version-1", "1.0.0",
                    new DateTimeOffset(2026, 11, 3, 11, 30, 0, TimeSpan.Zero), new DateTimeOffset(2026, 11, 3, 11, 30, 0, TimeSpan.Zero), null, null, true)
            ],
            [new("dependency-1", "sha256:dependency")]);

    internal sealed class FixtureInspection(WorkflowExecutableInspectionDetails? executable) : IWorkflowExecutableInspection
    {
        public WorkflowExecutableInspectionDetails? Current { get; set; } = executable;
        public int Reads { get; private set; }

        public ValueTask<WorkflowExecutableInspectionDetails?> GetAsync(string artifactId, CancellationToken cancellationToken = default)
        {
            Reads++;
            return ValueTask.FromResult(Current?.ArtifactId == artifactId ? Current : null);
        }
    }

    /// <summary>The host's permission decisions: every permission except those listed as denied.</summary>
    internal sealed class Authorizer(params string[] denied) : IWeaverToolAuthorizer
    {
        public Task<WeaverPolicyDecision> AuthorizeAsync(WeaverToolDescriptor tool, string actorId, string tenantId, WeaverResourceTarget? target,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(tool.RequiredPermissions.Any(denied.Contains)
                ? new WeaverPolicyDecision(false, [new("weaver.tool.permission_denied", "The current principal lacks a required permission for this resource.")])
                : new WeaverPolicyDecision(true, []));
    }
}
