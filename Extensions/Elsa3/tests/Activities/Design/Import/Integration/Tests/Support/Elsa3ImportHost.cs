using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Cluster.Readability;
using CShells;
using CShells.AspNetCore.Extensions;
using CShells.DependencyInjection;
using CShells.Lifecycle;
using Elsa.Activities.ControlFlow;
using Elsa.Activities.Design.Api;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore;
using Elsa.Activities.Design.Reconciliation;
using Elsa.Activities.Design.Reconciliation.Clr;
using Elsa.Activities.Flowchart;
using Elsa.Activities.Graph.Design;
using Elsa.Activities.Graph.Runtime;
using Elsa.Activities.Primitives;
using Elsa.Activities.Runtime;
using Elsa.Activities.Sequence;
using Elsa.Api.Capabilities;
using Elsa.Events;
using Elsa.Expressions;
using Elsa.Expressions.JavaScript;
using Elsa.Expressions.JavaScript.Jint;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Extensions;
using Elsa.Locking.Core;
using Elsa.Mediator;
using Elsa.Primitives.Hosting;
using Elsa.Serialization.SystemText;
using Elsa.Tasks;
using Elsa.Workflows.Design.Api;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Design.Validations;
using Elsa.Workflows.Publishing;
using Elsa.Workflows.Publishing.Api;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Resumption;
using Elsa.Workflows.Runtime.Scheduling;
using Elsa3.Activities.Design.Import.Models;
using Elsa3.Activities.Design.Import.Persistence.EntityFrameworkCore;
using Elsa3.Activities.Design.Import.Publishing;
using Elsa3.Mapping;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa3.Activities.Design.Import.Integration.Tests.Support;

/// <summary>A workflow version the Publishing API published: what the runtime API executes.</summary>
public sealed record PublishedWorkflow(string VersionId, string ArtifactId, string SourceReferenceId);

/// <summary>An apply's answer: its status code and its body (the receipt, or the problem).</summary>
public sealed record ApplyResponse(HttpStatusCode StatusCode, JsonNode Body)
{
    /// <summary>The problem's diagnostic codes, for a refused apply.</summary>
    public IReadOnlyList<string> DiagnosticCodes =>
        Body["diagnostics"]?.AsArray().Select(diagnostic => (string?)diagnostic?["code"]).OfType<string>().ToArray() ?? [];
}

/// <summary>One upload, its analysis and the apply of a selection from it; replaying it needs all of them.</summary>
public sealed record ImportAttempt(string Handle, string PlanId, IReadOnlyList<string> Selection, IReadOnlyList<JsonNode> Items, ApplyResponse Apply)
{
    /// <summary>The analyzed item of the source version <paramref name="sourceVersionId"/>.</summary>
    public JsonNode Item(string sourceVersionId) =>
        Items.Single(item => Elsa3ImportHost.Required(item, "sourceVersionId") == sourceVersionId);

    /// <summary>The plan's activity version id for the reusable source version <paramref name="sourceVersionId"/>.</summary>
    public string VersionId(string sourceVersionId) => Elsa3ImportHost.Required(Item(sourceVersionId), "activityDefinitionVersionId");
}

/// <summary>One entry of a receipt's <c>publications</c>, as the import API returns it.</summary>
public sealed record PublicationEntry(string VersionId, string SourceVersionId, ReusableActivityPublicationStatus Status, string? ErrorCode, string? BlockedBy, JsonNode Json)
{
    /// <summary>The receipt's publication states in its order; none when it carries none.</summary>
    public static IReadOnlyList<PublicationEntry> Of(JsonNode receipt) =>
        receipt["publications"]?.AsArray().Select(state => new PublicationEntry(
            Elsa3ImportHost.Required(state, "activityDefinitionVersionId"),
            Elsa3ImportHost.Required(state, "sourceVersionId"),
            (ReusableActivityPublicationStatus)(int)state!["status"]!,
            (string?)state["errorCode"],
            (string?)state["blockedBy"],
            state)).ToArray() ?? [];

    /// <summary>Each source version's status, by source version id.</summary>
    public static IReadOnlyDictionary<string, ReusableActivityPublicationStatus> StatusBySource(JsonNode receipt) =>
        Of(receipt).ToDictionary(entry => entry.SourceVersionId, entry => entry.Status, StringComparer.Ordinal);
}

/// <summary>A run's committed state, read through the runtime stores.</summary>
public sealed record RunState(
    IReadOnlyCollection<ActivityExecutionState> Activities,
    WorkflowExecutionState? Workflow,
    IReadOnlyCollection<IncidentState> Incidents)
{
    /// <summary>The executable node ids of every activity execution the run committed.</summary>
    public IReadOnlyList<string> ExecutedNodeIds => Activities.Select(activity => activity.Execution.ExecutableNodeId).ToArray();

    public string Describe() =>
        $"workflow {Workflow?.Status}, activities {string.Join(", ", Activities.Select(activity => $"{activity.Execution.ExecutableNodeId}={activity.Status}{(activity.Fault is { } fault ? $" (fault {fault.Code}: {fault.Message})" : string.Empty)}"))}" +
        (Incidents.Count == 0 ? string.Empty : $"; incidents {string.Join("; ", Incidents.Select(incident => $"{incident.FailureType} on {incident.ExecutableNodeId}: {incident.Message}"))}");
}

/// <summary>
/// The Elsa 3 import host (spec 246 R-13): one CShells web host on a test server whose one shell composes the import,
/// its EF persistence and, unless turned off, its publishing bridge, over activity and workflow design, the graph
/// activity provider, publishing and the runtime. Activity design, workflow design and the import share one SQLite file
/// (the import commits through their transaction); publishing and the runtime have their own. Every file is under one
/// temporary directory, which disposal deletes.
/// </summary>
public sealed class Elsa3ImportHost : IAsyncDisposable
{
    public const string ShellName = "elsa3-import";
    public const string TenantId = PersistenceScope.DefaultValue;
    private const string AuthenticationScheme = "Elsa3ImportTest";
    private const string PermissionsHeader = "X-Elsa3-Import-Permissions";
    private const string ImportRoute = "migration/elsa3/reusable-activities";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web) { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>The built-in activities an imported body converts to; reconciliation must have published them.</summary>
    private static readonly string[] RequiredCatalogTypes =
    [
        "Elsa.Activities.Sequence.Activities.Sequence",
        "Elsa.Activities.Flowchart.Activities.Flowchart",
        "Elsa.Activities.Graph.Runtime.Activities.Complete"
    ];

    private readonly WebApplication _app;
    private readonly DirectoryInfo _root;
    private readonly bool _withPublishing;
    private readonly bool _withPublishSeam;
    private readonly bool _withScheduling;
    private readonly bool _withHttp;
    private bool _ownsRoot = true;

    private Elsa3ImportHost(WebApplication app, IShell shell, DirectoryInfo root, WarningLog warnings,
        bool withPublishing, bool withPublishSeam, bool withScheduling, bool withHttp)
    {
        _app = app;
        _root = root;
        _withPublishing = withPublishing;
        _withPublishSeam = withPublishSeam;
        _withScheduling = withScheduling;
        _withHttp = withHttp;
        Shell = shell;
        Warnings = warnings;
        Client = app.GetTestClient();
    }

    /// <summary>
    /// Stops this host and starts a new one on the same stores (the same SQLite files), composed as this one was, with the
    /// folder import of <paramref name="reconcileFolder"/> when given. This host is disposed; use the returned one.
    /// </summary>
    /// <remarks>
    /// When the new host fails to start, nobody owns the stores any more: a test that expects that failure reads them
    /// through this host and then calls <see cref="DeleteStores"/>.
    /// </remarks>
    public async Task<Elsa3ImportHost> RestartAsync(string? reconcileFolder = null, bool strict = false, bool refuseAllPublications = false)
    {
        _ownsRoot = false;
        await DisposeAsync();
        return await StartAsync(_withPublishing, _withPublishSeam, _withScheduling, _withHttp, reconcileFolder, strict, refuseAllPublications, _root);
    }

    public IShell Shell { get; }

    /// <summary>Every warning and error the host logged, for failure messages.</summary>
    public WarningLog Warnings { get; }
    public HttpClient Client { get; }

    /// <summary>The version ids the host's publish seam refuses (<see cref="Elsa3ImportPublishSeamFeature"/>).</summary>
    public PublishRefusals Refusals => Shell.ServiceProvider.GetRequiredService<PublishRefusals>();

    /// <summary>The permissions every request carries; every permission unless a test narrows it.</summary>
    public IReadOnlyList<string> Permissions { get; set; } = [PermissionKey.Wildcard];

    /// <summary>The SQLite file activity design, workflow design and the import share.</summary>
    public string DesignDatabase => Path.Join(_root.FullName, "design.db");

    /// <summary>
    /// Starts a host, activates its shell, waits for its startup tasks (catalog reconciliation among them) and checks the
    /// catalog holds the built-in activities an imported body converts to. Without <paramref name="withPublishing"/> the
    /// shell does not compose <c>Elsa3ImportActivitiesPublishing</c>; with <paramref name="withPublishSeam"/> it composes
    /// the test's publish seam around it (<see cref="Refusals"/>); with <paramref name="withScheduling"/> it composes the
    /// scheduling activities (Delay); with <paramref name="withHttp"/> the HTTP activities, whose endpoints listen under
    /// <c>/workflows/http</c>. With <paramref name="reconcileFolder"/> the shell composes <c>Elsa3JsonReconciliation</c> on
    /// that folder (spec 235), strict when <paramref name="strict"/>, so the folder is imported while the shell activates.
    /// With <paramref name="refuseAllPublications"/> the publish seam refuses every reusable from the first publication on
    /// (it needs <paramref name="withPublishSeam"/>).
    /// </summary>
    public static async Task<Elsa3ImportHost> StartAsync(
        bool withPublishing = true,
        bool withPublishSeam = false,
        bool withScheduling = false,
        bool withHttp = false,
        string? reconcileFolder = null,
        bool strict = false,
        bool refuseAllPublications = false,
        DirectoryInfo? existingRoot = null)
    {
        var root = existingRoot ?? Directory.CreateTempSubdirectory("elsa3-import-");
        var warnings = new WarningLog();
        var refusals = new PublishRefusals();
        if (refuseAllPublications)
            refusals.RefuseAll();
        try
        {
            var settings = ShellSettings(root.FullName, withPublishing, withPublishSeam, withScheduling, withHttp);
            if (reconcileFolder is not null)
            {
                var reconciliation = $"CShells:Shells:{ShellName}:Features:Elsa3JsonReconciliation:Options:";
                settings[$"{reconciliation}SourceId"] = "elsa3-test";
                settings[$"{reconciliation}FolderPath"] = reconcileFolder;
                settings[$"{reconciliation}FailOnUnconvertible"] = strict ? "true" : "false";
            }

            var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
            builder.Logging.AddProvider(warnings);
            builder.Configuration.AddConfiguration(configuration);
            builder.Services.AddSingleton<IConfiguration>(configuration);
            builder.Services.AddSingleton<IDistributedLockProvider, ProcessLockProvider>();
            // An instance, not a type: a shell copies the host's registrations, and the tests read the one its seam uses.
            builder.Services.AddSingleton(refusals);
            // Same host-wide fleet/observed schema boundary as Workbench; the publisher must not infer finalization.
            builder.Services.AddEfSchemaReadability();
            builder.Services.AddFoundationIdentityAbstractions(options =>
                options.NormalizedAuthenticationTypes = new HashSet<string>(StringComparer.Ordinal) { AuthenticationScheme });
            builder.Services.AddAuthentication(AuthenticationScheme).AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(AuthenticationScheme, _ => { });
            builder.Services.AddAuthorization();
            builder.Services.AddCShellsAspNetCore(shells => shells
                .WithAssemblies(
                    typeof(PrimitivesFeature).Assembly,
                    typeof(SerializationFeature).Assembly,
                    typeof(MediatorFeature).Assembly,
                    typeof(EventsFeature).Assembly,
                    typeof(ExpressionsFeature).Assembly,
                    typeof(JavaScriptFeature).Assembly,
                    typeof(JintFeature).Assembly,
                    typeof(Elsa.Activities.Scripting.ActivitiesScriptingFeature).Assembly,
                    typeof(ApiCapabilitiesFeature).Assembly,
                    typeof(TasksFeature).Assembly,
                    typeof(ActivitiesRuntimeFeature).Assembly,
                    typeof(ActivitiesPrimitivesFeature).Assembly,
                    typeof(ActivitiesSequenceFeature).Assembly,
                    typeof(ActivitiesFlowchartFeature).Assembly,
                    typeof(ActivitiesControlFlowFeature).Assembly,
                    typeof(Elsa.Activities.DispatchWorkflow.Design.DispatchWorkflowDesignFeature).Assembly,
                    typeof(Elsa.Activities.DispatchWorkflow.Runtime.DispatchWorkflowRuntimeFeature).Assembly,
                    typeof(GraphActivitiesDesignFeature).Assembly,
                    typeof(GraphActivitiesRuntimeFeature).Assembly,
                    typeof(ActivitiesDesignApiFeature).Assembly,
                    typeof(ActivitiesDesignEntityFrameworkCoreFeature).Assembly,
                    typeof(ActivitiesDesignReconciliationFeature).Assembly,
                    typeof(ClrActivityReconciliationFeature).Assembly,
                    typeof(WorkflowDesignValidationsFeature).Assembly,
                    typeof(WorkflowsDesignApiFeature).Assembly,
                    typeof(WorkflowsDesignEntityFrameworkCoreFeature).Assembly,
                    typeof(WorkflowsPublishingFeature).Assembly,
                    typeof(WorkflowsPublishingApiFeature).Assembly,
                    typeof(PublishingEntityFrameworkCoreFeature).Assembly,
                    typeof(WorkflowsRuntimeApiFeature).Assembly,
                    typeof(RuntimeEntityFrameworkCoreFeature).Assembly,
                    typeof(WorkflowsRuntimeResumptionFeature).Assembly,
                    typeof(WorkflowsRuntimeRecurringTriggersFeature).Assembly,
                    typeof(WorkflowsRuntimeSchedulingFeature).Assembly,
                    typeof(Elsa.Activities.Scheduling.ActivitiesSchedulingFeature).Assembly,
                    typeof(Elsa.Http.HttpFeature).Assembly,
                    typeof(Elsa.Workflows.Runtime.Http.WorkflowsRuntimeHttpFeature).Assembly,
                    typeof(Elsa.Activities.Http.ActivitiesHttpFeature).Assembly,
                    typeof(Elsa3MappingFeature).Assembly,
                    typeof(Elsa3ImportActivitiesFeature).Assembly,
                    typeof(Elsa3ImportActivitiesEntityFrameworkCoreFeature).Assembly,
                    typeof(Elsa3ImportActivitiesPublishingFeature).Assembly,
                    typeof(Elsa3.Activities.Design.Import.Reconciliation.Elsa3JsonReconciliationFeature).Assembly,
                    typeof(Elsa3ImportPublishSeamFeature).Assembly)
                .WithConfigurationProvider(configuration));

            var app = builder.Build();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapShells();
            await app.StartAsync();
            IShell shell;
            try
            {
                shell = await app.Services.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName);
            }
            catch
            {
                // A startup task refused the shell (for example a strict folder import); release the app's stores.
                await app.DisposeAsync();
                throw;
            }

            // A restarted host takes over the stores and deletes them when it is disposed.
            var host = new Elsa3ImportHost(app, shell, root, warnings, withPublishing, withPublishSeam, withScheduling, withHttp);
            try
            {
                await host.RequireCatalogAsync();
            }
            catch
            {
                await host.DisposeAsync();
                throw;
            }

            return host;
        }
        catch
        {
            if (existingRoot is null)
                DeleteQuietly(root);
            throw;
        }
    }

    private static Dictionary<string, string?> ShellSettings(string root, bool withPublishing, bool withPublishSeam, bool withScheduling, bool withHttp)
    {
        var features = $"CShells:Shells:{ShellName}:Features:";
        var design = Sqlite(Path.Join(root, "design.db"));
        var settings = new Dictionary<string, string?>
        {
            [$"CShells:Shells:{ShellName}:Configuration:WebRouting:Path"] = "",
            [$"{features}ActivitiesDesignEntityFrameworkCore:ConnectionString"] = design,
            [$"{features}WorkflowsDesignEntityFrameworkCore:ConnectionString"] = design,
            [$"{features}Elsa3ImportActivitiesEntityFrameworkCore:ConnectionString"] = design,
            [$"{features}WorkflowsPublishingEntityFrameworkCore:ConnectionString"] = Sqlite(Path.Join(root, "publishing.db")),
            [$"{features}WorkflowsRuntimeEntityFrameworkCore:ConnectionString"] = Sqlite(Path.Join(root, "runtime.db")),
            [$"{features}WorkflowsRuntimeEntityFrameworkCore:RecoveryContinuationSigningKey"] = GeneratedKey(),
            [$"{features}WorkflowsRuntimeEntityFrameworkCore:HierarchyCursorSigningKey"] = GeneratedKey()
        };
        foreach (var feature in Features
                     .Concat(withPublishing ? ["Elsa3ImportActivitiesPublishing"] : [])
                     .Concat(withPublishSeam ? [Elsa3ImportPublishSeamFeature.Name] : [])
                     .Concat(withScheduling ? ["ActivitiesScheduling"] : [])
                     .Concat(withHttp ? ["ActivitiesHttp"] : []))
            settings.TryAdd($"{features}{feature}", null);
        return settings;
    }

    /// <summary>The features the shell enables by name. CShells adds what they depend on.</summary>
    private static readonly string[] Features =
    [
        "Primitives",
        "Serialization",
        "Mediator",
        "Events",
        "Expressions",
        "JavaScriptExpressions",
        "JavaScriptJintEngine",
        "ActivitiesScripting",
        "ApiCapabilities",
        "Tasks",
        "ActivitiesRuntime",
        "ActivitiesPrimitives",
        "ActivitiesSequence",
        "ActivitiesFlowchart",
        "ActivitiesControlFlow",
        "ActivitiesDispatchWorkflowDesign",
        "ActivitiesDispatchWorkflowRuntime",
        "ActivitiesGraphDesign",
        "ActivitiesGraphRuntime",
        "ActivitiesDesignApi",
        "ActivitiesDesignEntityFrameworkCore",
        "ActivitiesDesignReconciliation",
        "ClrActivityReconciliation",
        "WorkflowDesignValidations",
        "WorkflowsDesignApi",
        "WorkflowsDesignEntityFrameworkCore",
        "WorkflowsPublishing",
        "WorkflowsPublishingApi",
        "WorkflowsPublishingEntityFrameworkCore",
        "WorkflowsRuntimeApi",
        "WorkflowsRuntimeEntityFrameworkCore",
        "WorkflowsRuntimeResumption",
        "WorkflowsRuntimeTriggers",
        // The EF activation switch publishes through the recurring-schedule projection this feature prepares.
        "WorkflowsRuntimeRecurringTriggers",
        "Elsa3Mapping",
        "Elsa3ImportJsonActivities",
        "Elsa3ImportActivitiesEntityFrameworkCore"
    ];

    /// <summary>Fails at once, naming what is missing, when reconciliation has not published a built-in an import needs.</summary>
    private async Task RequireCatalogAsync()
    {
        var missing = new List<string>();
        foreach (var typeKey in RequiredCatalogTypes)
        {
            var catalog = await GetJsonAsync($"design/activities/definitions?search={Uri.EscapeDataString(typeKey)}");
            var item = catalog["items"]?.AsArray().SingleOrDefault(candidate => (string?)candidate?["definition"]?["activityTypeKey"] == typeKey);
            if ((string?)item?["definition"]?["headVersionId"] is null)
                missing.Add(typeKey);
        }

        Assert.True(missing.Count == 0, $"Catalog reconciliation did not publish {string.Join(", ", missing)}; the host cannot convert an imported body.");
    }

    // ---- The import API ------------------------------------------------------------------------------------------

    /// <summary>The fixtures' Elsa 3 definitions as one export: a JSON array of the named files under <c>Fixtures</c>.</summary>
    public static string Collection(params string[] fixtureNames)
    {
        var definitions = new JsonArray();
        foreach (var name in fixtureNames)
        {
            // A fixture holds one definition, or an array of the versions that belong together.
            var fixture = JsonNode.Parse(File.ReadAllText(Path.Join(AppContext.BaseDirectory, "Fixtures", $"{name}.json")));
            foreach (var definition in fixture is JsonArray versions ? versions.Select(version => version!.DeepClone()).ToArray() : [fixture!])
                definitions.Add(definition);
        }
        return definitions.ToJsonString();
    }

    /// <summary>Uploads the named fixtures as one collection and returns its handle.</summary>
    public async Task<string> UploadCollectionAsync(params string[] fixtureNames)
    {
        using var request = Request(HttpMethod.Post, $"{ImportRoute}/collections");
        request.Content = new StringContent(Collection(fixtureNames), Encoding.UTF8, "application/json");
        using var response = await Client.SendAsync(request);
        return Required(await ReadSuccessAsync(response, "upload"), "collectionHandle");
    }

    /// <summary>The collection's plan id and every source version id it analyzed.</summary>
    public async Task<(string PlanId, IReadOnlyList<JsonNode> Items)> AnalyzeAsync(string collectionHandle)
    {
        var page = await GetJsonAsync($"{ImportRoute}/collections/{Uri.EscapeDataString(collectionHandle)}/analysis?limit=500");
        return (Required(page, "planId"), page["items"]!.AsArray().Select(item => item!).ToArray());
    }

    /// <summary>Expands <paramref name="sourceVersionIds"/> to its dependency closure; returns the readiness.</summary>
    public async Task<JsonNode> SelectAsync(string collectionHandle, string planId, params string[] sourceVersionIds) =>
        await PostAsync($"{ImportRoute}/collections/{Uri.EscapeDataString(collectionHandle)}/selection", new JsonObject
        {
            ["planId"] = planId,
            ["selectedSourceVersionIds"] = new JsonArray(sourceVersionIds.Select(id => (JsonNode?)id).ToArray())
        });

    /// <summary>Applies the selection under <paramref name="idempotencyKey"/>; returns the answer whatever its status.</summary>
    public async Task<ApplyResponse> ApplyAsync(string collectionHandle, string planId, string idempotencyKey, params string[] sourceVersionIds)
    {
        using var response = await Send(HttpMethod.Post, $"{ImportRoute}/collections/{Uri.EscapeDataString(collectionHandle)}/apply", new JsonObject
        {
            ["planId"] = planId,
            ["selectedSourceVersionIds"] = new JsonArray(sourceVersionIds.Select(id => (JsonNode?)id).ToArray()),
            ["idempotencyKey"] = idempotencyKey
        });
        var content = await response.Content.ReadAsStringAsync();
        return new(response.StatusCode, Body(content));
    }

    /// <summary>Uploads, analyzes, selects and applies every source version of the named fixtures.</summary>
    public async Task<(ApplyResponse Apply, IReadOnlyList<JsonNode> Items)> ImportAsync(string idempotencyKey, params string[] fixtureNames)
    {
        var handle = await UploadCollectionAsync(fixtureNames);
        var (planId, items) = await AnalyzeAsync(handle);
        var all = items.Select(item => Required(item, "sourceVersionId")).ToArray();
        await SelectAsync(handle, planId, all);
        return (await ApplyAsync(handle, planId, idempotencyKey, all), items);
    }

    /// <summary>
    /// Uploads the named fixtures, analyzes them and applies <paramref name="sourceVersionIds"/> (every analyzed source
    /// version when none are named) under <paramref name="idempotencyKey"/>; <paramref name="beforeApply"/> runs between
    /// the analysis and the apply.
    /// </summary>
    public async Task<ImportAttempt> ImportSelectionAsync(
        string idempotencyKey,
        string[] fixtureNames,
        string[]? sourceVersionIds = null,
        Action<IReadOnlyList<JsonNode>>? beforeApply = null)
    {
        var handle = await UploadCollectionAsync(fixtureNames);
        var (planId, items) = await AnalyzeAsync(handle);
        var selection = sourceVersionIds ?? items.Select(item => Required(item, "sourceVersionId")).ToArray();
        beforeApply?.Invoke(items);
        return new(handle, planId, selection, items, await ApplyAsync(handle, planId, idempotencyKey, selection.ToArray()));
    }

    /// <summary>Applies <paramref name="attempt"/>'s selection again under <paramref name="idempotencyKey"/>.</summary>
    public Task<ApplyResponse> ReplayAsync(ImportAttempt attempt, string idempotencyKey) =>
        ApplyAsync(attempt.Handle, attempt.PlanId, idempotencyKey, attempt.Selection.ToArray());

    /// <summary>The import status under <paramref name="idempotencyKey"/>; its answer whatever its status.</summary>
    public async Task<(HttpStatusCode StatusCode, JsonNode Body)> GetImportStatusAsync(string idempotencyKey)
    {
        using var response = await Send(HttpMethod.Get, $"{ImportRoute}/imports/{Uri.EscapeDataString(idempotencyKey)}");
        return (response.StatusCode, Body(await response.Content.ReadAsStringAsync()));
    }

    // ---- Publishing and runs -------------------------------------------------------------------------------------

    /// <summary>Publishes an imported workflow version through the Publishing API.</summary>
    public async Task<PublishedWorkflow> PublishWorkflowAsync(string workflowVersionId)
    {
        var published = await PostAsync($"publishing/workflows/{Uri.EscapeDataString(workflowVersionId)}/publish", new JsonObject());
        return new(workflowVersionId, Required(published, "artifactId"), Required(published, "sourceReferenceId"));
    }

    /// <summary>
    /// The active publication of <paramref name="definitionId"/>'s default slot and its source reference, read in the
    /// host's persistence scope; null when the slot has none.
    /// </summary>
    public async Task<(PublishedWorkflow Workflow, WorkflowExecutableSourceReference Reference)?> ActivePublicationAsync(string definitionId)
    {
        await using var scope = await Shell.ServiceProvider.GetRequiredService<IPersistenceOperationScopeFactory>()
            .CreateAsync(new PersistenceScope(TenantId));
        var services = scope.ServiceProvider;
        var slot = await services.GetRequiredService<IWorkflowActivationAuthority>().FindAsync(definitionId, "default");
        if (slot?.ActiveActivationId is not { } publicationId)
            return null;
        var record = await services.GetRequiredService<Elsa.Workflows.Publishing.Core.Contracts.IPublicationRecordStore>().FindAsync(publicationId);
        if (record?.SourceReferenceId is not { } sourceReferenceId)
            return null;
        var reference = await services.GetRequiredService<IWorkflowExecutableSourceReferenceReader>().FindAsync(sourceReferenceId);
        return reference is null ? null : (new PublishedWorkflow(record.WorkflowDefinitionVersionId, record.ArtifactId, sourceReferenceId), reference);
    }

    /// <summary>Every publication record of <paramref name="definitionId"/>'s default slot, read in the host's persistence scope.</summary>
    public async Task<IReadOnlyCollection<Elsa.Workflows.Publishing.Core.Models.PublicationRecord>> PublicationRecordsAsync(string definitionId)
    {
        await using var scope = await Shell.ServiceProvider.GetRequiredService<IPersistenceOperationScopeFactory>()
            .CreateAsync(new PersistenceScope(TenantId));
        var services = scope.ServiceProvider;
        var slot = await services.GetRequiredService<IWorkflowActivationAuthority>().FindAsync(definitionId, "default");
        return slot is null ? [] : await services.GetRequiredService<Elsa.Workflows.Publishing.Core.Contracts.IPublicationRecordStore>().ListBySlotAsync(slot.SlotId);
    }

    /// <summary>Starts a published workflow with <paramref name="inputs"/> through the runtime API; returns its execution id.</summary>
    public async Task<string> ExecuteAsync(PublishedWorkflow workflow, JsonObject? inputs = null)
    {
        var started = await PostAsync($"runtime/workflows/executables/{Uri.EscapeDataString(workflow.ArtifactId)}/execute", new JsonObject
        {
            ["sourceReferenceId"] = workflow.SourceReferenceId,
            ["inputs"] = inputs ?? new JsonObject()
        });
        return Required(started, "workflowExecutionId");
    }

    /// <summary>Starts the workflow and waits for its run to settle.</summary>
    public async Task<RunState> RunAsync(PublishedWorkflow workflow, JsonObject? inputs = null) =>
        await WaitForSettledAsync(await ExecuteAsync(workflow, inputs));

    /// <summary>The committed activity states and workflow state of a run.</summary>
    public async Task<RunState> ReadRunAsync(string workflowExecutionId)
    {
        await using var scope = Shell.ServiceProvider.CreateAsyncScope();
        return new(
            await scope.ServiceProvider.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(workflowExecutionId),
            await scope.ServiceProvider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(workflowExecutionId),
            await scope.ServiceProvider.GetRequiredService<IIncidentStateStore>().ListAsync(workflowExecutionId));
    }

    /// <summary>
    /// Generous because the tests run on a shared machine whose parallel sessions compete for the CPU (AGENTS.md,
    /// "Builds on a shared machine"). No test relies on reaching it.
    /// </summary>
    public static readonly TimeSpan SettleTimeout = TimeSpan.FromSeconds(60);

    /// <summary>Polls the run's committed state until <paramref name="condition"/> holds, for a run that is still waiting.</summary>
    public async Task<RunState> WaitForAsync(string workflowExecutionId, Func<RunState, bool> condition)
    {
        var deadline = Stopwatch.StartNew();
        while (true)
        {
            var run = await ReadRunAsync(workflowExecutionId);
            if (condition(run))
                return run;
            Assert.True(deadline.Elapsed < SettleTimeout, $"Run '{workflowExecutionId}' did not reach the expected state: {run.Describe()}.");
            await Task.Delay(100);
        }
    }

    /// <summary>
    /// Polls the run's committed state until its workflow is completed, faulted or cancelled; the drain may outlive the
    /// request that started it. An activity fault or an incident ends the wait at once.
    /// </summary>
    public async Task<RunState> WaitForSettledAsync(string workflowExecutionId)
    {
        var deadline = Stopwatch.StartNew();
        while (true)
        {
            var run = await ReadRunAsync(workflowExecutionId);
            if (run.Workflow?.Status is WorkflowExecutionStatus.Completed or WorkflowExecutionStatus.Faulted or WorkflowExecutionStatus.Cancelled ||
                run.Activities.Any(activity => activity.Fault is not null) ||
                run.Incidents.Count > 0)
                // The read lists activities before the workflow, so a run that settled between the two shows a terminal
                // workflow with activities still running. Read once more now that it has settled.
                return await ReadRunAsync(workflowExecutionId);
            Assert.True(deadline.Elapsed < SettleTimeout, $"Run '{workflowExecutionId}' did not settle: {run.Describe()}.");
            await Task.Delay(100);
        }
    }

    // ---- HTTP ----------------------------------------------------------------------------------------------------

    private HttpRequestMessage Request(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add(PermissionsHeader, string.Join(' ', Permissions));
        return request;
    }

    public async Task<HttpResponseMessage> Send(HttpMethod method, string path, JsonNode? body = null)
    {
        using var request = Request(method, path);
        if (body is not null)
            request.Content = JsonContent.Create(body, options: Web);
        return await Client.SendAsync(request);
    }

    public async Task<JsonNode> GetJsonAsync(string path)
    {
        using var response = await Send(HttpMethod.Get, path);
        return await ReadSuccessAsync(response, $"GET {path}");
    }

    public async Task<JsonNode> PostAsync(string path, JsonNode body)
    {
        using var response = await Send(HttpMethod.Post, path, body);
        // A server error's cause is only in the host's log; name it in the failure.
        return await ReadSuccessAsync(response, (int)response.StatusCode >= 500
            ? $"POST {path} (log: {string.Join(" | ", Warnings.Entries.TakeLast(3))})"
            : $"POST {path}");
    }

    private static async Task<JsonNode> ReadSuccessAsync(HttpResponseMessage response, string what)
    {
        var content = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{what} returned {(int)response.StatusCode}: {content}");
        return JsonNode.Parse(content)!;
    }

    /// <summary>A response body as JSON; an empty object for an empty body, as a 403 has.</summary>
    private static JsonNode Body(string content) =>
        string.IsNullOrWhiteSpace(content) ? new JsonObject() : JsonNode.Parse(content) ?? new JsonObject();

    public static string Required(JsonNode? node, params string[] path)
    {
        foreach (var segment in path)
            node = node?[segment];
        return (string?)node ?? throw new InvalidOperationException($"The response lacks '{string.Join('.', path)}'.");
    }

    // ---- Stores --------------------------------------------------------------------------------------------------

    /// <summary>Runs <paramref name="read"/> against the shell's services in a fresh scope.</summary>
    public async Task<T> ReadAsync<T>(Func<IServiceProvider, Task<T>> read)
    {
        await using var scope = Shell.ServiceProvider.CreateAsyncScope();
        return await read(scope.ServiceProvider);
    }

    /// <summary>The number of rows of every table of the shared design database, by table.</summary>
    public async Task<IReadOnlyDictionary<string, long>> CountDesignRowsAsync()
    {
        await using var connection = new SqliteConnection($"Data Source={DesignDatabase};Mode=ReadOnly;Pooling=False");
        await connection.OpenAsync();
        var tables = new List<string>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' AND name NOT LIKE '__EF%'";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                tables.Add(reader.GetString(0));
        }

        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var table in tables)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM \"{table}\"";
            counts[table] = (long)(await command.ExecuteScalarAsync())!;
        }

        return counts;
    }

    /// <summary>Deletes the stores' folder, for a test whose restart failed (see <see cref="RestartAsync"/>).</summary>
    public void DeleteStores() => DeleteQuietly(_root);

    public async ValueTask DisposeAsync()
    {
        try
        {
            Client.Dispose();
            // Disposing the app leaves the shell's recurring tasks running against the stores. A host restarted on the
            // same stores would then have the old shell's resumption pump re-drive its executions, a second owner that
            // fences the first out (StaleToken). Stop them first.
            await Shell.ServiceProvider.GetRequiredService<Elsa.Tasks.Core.ITaskManager>().StopExecutingRegisteredTasks();
            await _app.DisposeAsync();
        }
        finally
        {
            if (_ownsRoot)
                DeleteQuietly(_root);
        }
    }

    private static string Sqlite(string path) => $"Data Source={path};Pooling=False";

    /// <summary>A key generated per host: plain hex, at least 32 bytes, so no key material is written into a test.</summary>
    private static string GeneratedKey() => $"{Guid.NewGuid():N}{Guid.NewGuid():N}";

    private static void DeleteQuietly(DirectoryInfo directory)
    {
        try
        {
            directory.Delete(recursive: true);
        }
        catch (IOException)
        {
            // A SQLite handle can outlive the host for a moment; the temp folder is cleaned later.
        }
        catch (UnauthorizedAccessException)
        {
            // As above.
        }
    }

    /// <summary>Collects every warning and error any logger of the host writes.</summary>
    public sealed class WarningLog : ILoggerProvider
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> _entries = new();

        public IReadOnlyList<string> Entries => _entries.ToArray();

        public ILogger CreateLogger(string categoryName) => new Logger(categoryName, _entries);

        public void Dispose() { }

        private sealed class Logger(string category, System.Collections.Concurrent.ConcurrentQueue<string> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            // The folder import's summary and conversion-warning lines are Information; the tests assert them (spec 235).
            public bool IsEnabled(LogLevel logLevel) =>
                logLevel >= LogLevel.Warning
                || (logLevel == LogLevel.Information && category.StartsWith("Elsa3.Activities.Design.Import.Reconciliation", StringComparison.Ordinal));

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (IsEnabled(logLevel))
                    entries.Enqueue($"{logLevel} {category}: {formatter(state, exception)}{(exception is null ? string.Empty : $" ({exception.GetType().Name}: {exception.Message}{(exception.InnerException is null ? string.Empty : $" <- {exception.InnerException.Message}")})")}");
            }
        }
    }

    /// <summary>A lock provider for one process, which is all one test host needs.</summary>
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
    /// Authenticates every request as a user of the host's tenant holding the permissions the request's header names
    /// (<see cref="Permissions"/>).
    /// </summary>
    private sealed class TestAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var permissions = Request.Headers[PermissionsHeader].ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, "elsa3-importer"),
                new(IdentityClaimTypes.Normalized, "v1"),
                new(IdentityClaimTypes.TenantId, TenantId)
            };
            claims.AddRange(permissions.Select(permission => new Claim(IdentityClaimTypes.Permission, permission)));
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
        }
    }
}
