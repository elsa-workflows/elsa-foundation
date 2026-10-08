using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using CShells.AspNetCore.Configuration;
using CShells.AspNetCore.Extensions;
using CShells.AspNetCore.Routing;
using CShells.DependencyInjection;
using CShells.Features;
using CShells.Lifecycle;
using Elsa.Activities.Design.Api;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore;
using Elsa.Activities.Design.Reconciliation;
using Elsa.Activities.Design.Reconciliation.Clr;
using Elsa.Activities.Http;
using Elsa.Activities.Primitives;
using Elsa.Activities.Runtime;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Sequence;
using Elsa.Api.Capabilities;
using Elsa.Caching.Memory;
using Elsa.Events;
using Elsa.Expressions;
using Elsa.Expressions.JavaScript;
using Elsa.Expressions.JavaScript.Jint;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Extensions;
using Elsa.Http;
using Elsa.Http.Core.Contracts;
using Elsa.Http.Core.Models;
using Elsa.Locking.FileSystem;
using Elsa.Mediator;
using Elsa.Primitives;
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
using Elsa.Workflows.Runtime.Api.Coalescing;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Http;
using Elsa.Workflows.Runtime.Http.Contracts;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Reconciliation;
using Elsa.Workflows.Runtime.Reconciliation.Core.Contracts;
using Elsa.Workflows.Runtime.Reconciliation.Contracts;
using Elsa.Workflows.Runtime.Reconciliation.Core.Models;
using Elsa.Workflows.Runtime.Scheduling;
using Elsa.Workflows.Runtime.Resumption;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

internal static class ResponseReplayPublicationHost
{
    private const string ShellName = "default";
    private const string TestAuthenticationScheme = "ResponseReplayProof";
    private const string ArtifactReconciliationSourceId = "response-replay-pre-candidate-external";
    private const string ResponseNodeId = "write-response";
    private const string HttpNodeId = "http-in";
    private const string SetNodeId = "set-reference-text";
    private const string HttpResponsePath = "/workflows/http/";
    private const string ShellWebRoutingPathKey = "CShells:Shells:default:Configuration:WebRouting:Path";
    private static readonly HashSet<string> TransportHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Connection", "Content-Length", "Content-Type", "Date", "Keep-Alive", "Proxy-Authenticate",
        "Proxy-Authorization", "TE", "Trailer", "Transfer-Encoding", "Upgrade", "Via", "Server"
    };

    public static async Task<PublicationProofResult> RunAsync(string closurePath, string databasePath, string evidenceDirectory)
    {
        Directory.CreateDirectory(evidenceDirectory);
        var closureBytes = await File.ReadAllBytesAsync(closurePath);
        var baselineHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(closureBytes)).ToLowerInvariant();
        var baseline = ReadBaseline(closureBytes);
        await using var app = await StartHostAsync(databasePath, closurePath, evidenceDirectory);
        var shell = await app.Services.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName);
        var rootSchemeProvider = app.Services.GetRequiredService<IAuthenticationSchemeProvider>();
        Ensure(rootSchemeProvider is CShells.AspNetCore.Authentication.ShellAuthenticationSchemeProvider,
            $"The root authentication middleware resolved '{rootSchemeProvider.GetType().FullName}' instead of the shell-aware scheme provider.");
        var shellDefaultScheme = await shell.ServiceProvider.GetRequiredService<IAuthenticationSchemeProvider>()
            .GetDefaultAuthenticateSchemeAsync();
        Ensure(shellDefaultScheme?.Name == TestAuthenticationScheme,
            $"The composed shell default authentication scheme was '{shellDefaultScheme?.Name ?? "<none>"}', expected '{TestAuthenticationScheme}'.");
        var blueprint = await app.Services.GetRequiredService<IShellBlueprintProvider>()
            .GetAsync(ShellName, CancellationToken.None)
            ?? throw new InvalidOperationException($"The configured shell blueprint '{ShellName}' was not available.");
        var shellSettings = await blueprint.Blueprint.ComposeAsync(CancellationToken.None);
        var configuredShellPath = CShells.ShellSettingsExtensions.GetConfiguration(shellSettings, "WebRouting:Path");
        Ensure(configuredShellPath == string.Empty,
            $"The composed '{ShellName}' shell did not retain its explicit empty root WebRouting:Path (actual='{configuredShellPath ?? "<null>"}').");
        var shellRouteIndex = app.Services.GetRequiredService<IShellRouteIndex>();
        var rootRouteSelection = await shellRouteIndex.TryMatchAsync(
            new ShellRouteCriteria("workflows", false, null, null, null, null, null),
            CancellationToken.None);
        Ensure(rootRouteSelection?.ShellId.Name == ShellName,
            $"The production shell route index did not select '{ShellName}' for the configured root-path fallback (selected='{rootRouteSelection?.ShellId.Name ?? "<none>"}').");
        using var client = app.GetTestClient();

        // Ensure the immutable closure is imported through the production reconciler. The feature is selected in
        // this shell; a fresh process/database should import it exactly once.
        using (var scope = shell.ServiceProvider.CreateScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<IWorkflowArtifactReconciler>()
                .ReconcileAsync(CancellationToken.None);
            Ensure(result.Entries.Count == 1, $"Expected one captured closure import, got {result.Entries.Count}.");
            Ensure(result.Entries.Single().ArtifactId == baseline.ArtifactId,
                "The production reconciler selected a different baseline artifact.");
            Ensure(result.Entries.Single().Outcome is WorkflowArtifactImportOutcome.Imported or WorkflowArtifactImportOutcome.AlreadyCurrent,
                $"The production reconciler did not retain the baseline artifact: {result.Entries.Single().Outcome}.");
            await shell.ServiceProvider.GetServices<Elsa.Tasks.Core.IStartupTask>()
                .OfType<Elsa.Workflows.Runtime.Http.Tasks.UpdateRouteTableStartupTask>()
                .Single()
                .ExecuteAsync(CancellationToken.None);
        }

        using (var scope = shell.ServiceProvider.CreateScope())
        {
            var resolvedRoutes = await scope.ServiceProvider.GetRequiredService<IHttpEndpointRoutesResolver>()
                .ResolveRoutesAsync(CancellationToken.None);
            Ensure(resolvedRoutes.Any(route => string.Equals(route.Route, baseline.RoutePath, StringComparison.Ordinal) &&
                                                route.Methods.Contains("POST", StringComparer.Ordinal)),
                $"The production route resolver did not return the active baseline route '{baseline.RoutePath}' for POST.");
        }

        var routeTable = shell.ServiceProvider.GetRequiredService<IRouteTable>();
        var servingRoute = routeTable
            .SingleOrDefault(route => string.Equals(route.Route, baseline.RoutePath, StringComparison.Ordinal));
        Ensure(servingRoute is not null && servingRoute.Methods.Contains("POST", StringComparer.Ordinal),
            $"The startup route-table projection did not contain the active baseline route '{baseline.RoutePath}' for POST.");
        HttpRouteMatch baselineRouteMatch;
        var baselineClaimantCount = 0;
        using (var snapshot = (routeTable as IRouteTableSnapshotProvider
                   ?? throw new InvalidOperationException("The production route table does not expose its snapshot-resolution seam."))
                   .AcquireSnapshot())
        {
            baselineRouteMatch = snapshot.ResolveRoute(
                baseline.RoutePath.Trim('/'),
                "POST",
                shell.ServiceProvider.GetRequiredService<IRouteMatcher>()
            ) ?? throw new InvalidOperationException(
                $"The production snapshot matcher did not resolve endpoint path '{baseline.RoutePath.Trim('/')}' for POST.");
            Ensure(baselineRouteMatch.Template == baseline.RoutePath.Trim('/'),
                $"The production snapshot matcher did not resolve endpoint path '{baseline.RoutePath.Trim('/')}' for POST.");

            var baselineClaimants = await shell.ServiceProvider.GetRequiredService<IWorkflowTriggerBindingStore>()
                .ListAllByStimulusAsync(
                    Elsa.Activities.Http.Activities.HttpEndpointStimulus.StimulusType,
                    Elsa.Activities.Http.Activities.HttpEndpointStimulus.Hash(baselineRouteMatch.Template, "POST"),
                    CancellationToken.None);
            baselineClaimantCount = baselineClaimants.Count;
            Ensure(baselineClaimants.Any(binding => binding.ArtifactId == baseline.ArtifactId),
                "The real trigger-binding lookup did not return the imported baseline for the resolved POST route.");
        }

        var httpEndpointBasePath = shell.ServiceProvider
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<Elsa.Activities.Http.Options.HttpEndpointOptions>>()
            .Value.BasePath;
        Ensure(string.Equals(httpEndpointBasePath.TrimEnd('/'), HttpResponsePath.TrimEnd('/'), StringComparison.OrdinalIgnoreCase),
            $"The composed HttpEndpoint BasePath was '{httpEndpointBasePath}', expected '{HttpResponsePath.TrimEnd('/')}'.");
        var baselineHttp = await PostJsonAsync(client,
            $"{httpEndpointBasePath.TrimEnd('/')}/{baseline.RoutePath.Trim('/')}",
            new { firstName = "Alice", lastName = "Smith" });
        Ensure(baselineHttp.StatusCode == HttpStatusCode.OK,
            $"Baseline External route returned HTTP {(int)baselineHttp.StatusCode} with content type '{baselineHttp.ContentType}' and body '{baselineHttp.Body}'. " +
            $"BasePath='{httpEndpointBasePath}', endpointPath='{baseline.RoutePath.Trim('/')}', productionSnapshotMatch='{baselineRouteMatch.Template}', claimants={baselineClaimantCount}.");
        Ensure(baselineHttp.Body == "Alice Smith", $"Baseline External route returned '{baselineHttp.Body}'.");

        using var runtimeScope = shell.ServiceProvider.CreateScope();
        var provider = runtimeScope.ServiceProvider;
        var executableStore = provider.GetRequiredService<IWorkflowExecutableStore>();
        var baselineExecutable = await executableStore.FindAsync(baseline.ArtifactId, CancellationToken.None)
            ?? throw new InvalidOperationException("The imported External artifact is absent from the candidate runtime store.");
        Ensure(baselineExecutable.Identity.ArtifactHash == baseline.ArtifactHash,
            "The retained baseline artifact hash changed during import or execution.");
        Ensure(baselineExecutable.Nodes.Single(node => node.AuthoredActivityId == ResponseNodeId)
                   .ActivityContract?.SideEffectProfile == SideEffectProfile.External,
            "The retained published baseline profile did not remain External on the candidate runtime.");

        var baselineExecution = (await provider.GetRequiredService<IWorkflowExecutionStateStore>().ListAllAsync())
            .Single(state => state.PinnedExecutable.ArtifactId == baseline.ArtifactId);
        Ensure(baselineExecution.Status == WorkflowExecutionStatus.Completed,
            $"The baseline HTTP run ended in {baselineExecution.Status}.");
        var baselineInstruction = await ReadResponseInstructionAsync(provider, baselineExecution.WorkflowExecutionId, ResponseNodeId);
        Ensure(baselineInstruction.GetProperty("statusCode").GetInt32() == (int)baselineHttp.StatusCode,
            "The baseline's committed status instruction differs from the synchronous HTTP result.");
        Ensure(baselineInstruction.GetProperty("body").GetString() == baselineHttp.Body,
            "The baseline's committed body instruction differs from the synchronous HTTP result.");
        Ensure(baselineInstruction.GetProperty("contentType").GetString() == "text/plain" && baselineHttp.ContentType == "text/plain",
            "The baseline's committed or delivered content type differs from the authored value.");
        var baselineCommittedHeaders = ReadInstructionHeaders(baselineInstruction);
        Ensure(HeadersEqual(baselineHttp.AuthoredHeaders, baselineCommittedHeaders),
            "The baseline's delivered authored headers differ from its committed response instruction.");
        var baselineFrame = baselineExecution.RootVariableFrame
            ?? throw new InvalidOperationException("The completed baseline has no durable root variable frame.");
        Ensure(ReadStringVariable(baselineFrame, "content", "firstName") == "Alice" &&
               ReadStringVariable(baselineFrame, "content", "lastName") == "Smith" &&
               ReadStringVariable(baselineFrame, "referenceText") == "Alice Smith",
            "The baseline did not retain the parsed request fields and computed referenceText in its committed variable frame.");

        // The two published artifacts use the same route. Retract the imported source-owned baseline through the
        // normal coordinator so the candidate publication can take the exclusive route without rewriting bindings.
        var baselineSlot = (await provider.GetRequiredService<IWorkflowActivationAuthority>()
                .ListByDefinitionAsync(baseline.DefinitionId, CancellationToken.None))
            .Single(slot => slot.ActiveActivationId is not null);
        var baselineOwner = baselineSlot.Source
            ?? throw new InvalidOperationException("The baseline activation slot has no recorded owner.");
        var deactivation = await provider.GetRequiredService<IWorkflowActivationCoordinator>().DeactivateAsync(
            new WorkflowDeactivationCommand(
                baselineExecutable,
                baselineSlot.SlotName,
                baselineOwner,
                baselineSlot.Revision),
            CancellationToken.None);
        Ensure(deactivation.Succeeded && deactivation.Outcome == WorkflowActivationOutcome.Deactivated,
            $"The baseline route did not deactivate through the coordinator: {deactivation.Outcome} ({deactivation.Diagnostic}).");
        Ensure((await executableStore.FindAsync(baseline.ArtifactId, CancellationToken.None))?.Identity.ArtifactHash == baseline.ArtifactHash,
            "Deactivation removed or changed the captured baseline artifact.");

        var versionIds = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["sequence"] = await GetCurrentActivityVersionAsync(client, "Elsa.Activities.Sequence.Activities.Sequence"),
            ["httpEndpoint"] = await GetCurrentActivityVersionAsync(client, typeof(Elsa.Activities.Http.Activities.HttpEndpoint).FullName!),
            ["writeResponse"] = await GetCurrentActivityVersionAsync(client, "Elsa.Activities.Http.Activities.WriteHttpResponse")
        };
        var submitted = await SubmitWorkflowAsync(client,
            $"ResponseReplayCandidate-{Guid.NewGuid():N}",
            BuildHttpWorkflowState(versionIds, baseline.RoutePath));
        var candidatePublication = await PublishWorkflowAsync(client, submitted.VersionId);
        var candidate = await executableStore.FindAsync(candidatePublication.ArtifactId, CancellationToken.None)
            ?? throw new InvalidOperationException("The normal publishing API returned an artifact that is absent from the executable store.");
        Ensure(candidate.Identity.ArtifactHash == candidatePublication.ArtifactHash,
            "The candidate API hash differs from the normal compiled executable hash.");
        Ensure(candidate.Identity.DefinitionVersionId == submitted.VersionId,
            "The candidate executable is not pinned to the submitted workflow version.");
        var candidateResponseNode = candidate.Nodes.Single(node => node.AuthoredActivityId == ResponseNodeId);
        var candidateResponseContract = candidateResponseNode.ActivityContract
            ?? throw new InvalidOperationException("The candidate response node has no pinned activity contract.");
        Ensure(candidateResponseContract.SideEffectProfile == SideEffectProfile.ReplaySafe,
            "The normal publisher did not compile the candidate WriteHttpResponse profile as ReplaySafe.");
        Ensure(candidateResponseContract.Inputs.TryGetValue("StatusCode", out var statusCodeContract) &&
               statusCodeContract.HasDefault && statusCodeContract.DefaultValue is { } statusDefault &&
               statusDefault.ValueKind == JsonValueKind.Number && statusDefault.GetInt32() == 200,
            "The normal activity catalog/compiler path did not preserve WriteHttpResponse's declared StatusCode default.");
        Ensure(candidate.Nodes.Single(node => node.AuthoredActivityId == HttpNodeId)
                   .ActivityContract?.SideEffectProfile == SideEffectProfile.External,
            "The candidate compiler changed HttpEndpoint's External profile.");
        Ensure(candidate.Nodes.Single(node => node.AuthoredActivityId == HttpNodeId).ActivityType ==
               Elsa.Activities.Http.Activities.HttpEndpoint.ActivityType,
            "The candidate compiler did not preserve HttpEndpoint's declared runtime activity type key.");

        var activeHttpBindings = await ReadActiveHttpBindingsAsync(provider.GetRequiredService<IWorkflowTriggerBindingStore>());
        var candidateRouteBindings = activeHttpBindings
            .Where(binding => string.Equals(binding.Metadata.GetValueOrDefault("http:template"), baseline.RoutePath, StringComparison.Ordinal))
            .ToArray();
        Ensure(candidateRouteBindings.Length == 1, $"Expected one active serving binding for the shared route, got {candidateRouteBindings.Length}.");
        Ensure(candidateRouteBindings[0].ArtifactId == candidatePublication.ArtifactId,
            "The only active shared-route binding does not point to the newly published candidate.");
        using (var snapshot = (routeTable as IRouteTableSnapshotProvider
                   ?? throw new InvalidOperationException("The production route table does not expose its snapshot-resolution seam."))
                   .AcquireSnapshot())
        {
            var matchedRoute = snapshot.ResolveRoute(
                baseline.RoutePath.Trim('/'),
                "POST",
                shell.ServiceProvider.GetRequiredService<IRouteMatcher>()
            ) ?? throw new InvalidOperationException(
                $"The production snapshot matcher did not resolve candidate endpoint path '{baseline.RoutePath.Trim('/')}' for POST.");
            Ensure(matchedRoute.Template == baseline.RoutePath.Trim('/'),
                $"The production snapshot matcher did not resolve candidate endpoint path '{baseline.RoutePath.Trim('/')}' for POST.");
        }

        var candidateHttp = await PostJsonAsync(client,
            $"{httpEndpointBasePath.TrimEnd('/')}/{baseline.RoutePath.Trim('/')}",
            new { firstName = "Alice", lastName = "Smith" });
        Ensure(candidateHttp.StatusCode == HttpStatusCode.OK, $"Candidate HTTP route returned HTTP {(int)candidateHttp.StatusCode}.");
        Ensure(candidateHttp.Body == "Alice Smith", $"Candidate HTTP route returned '{candidateHttp.Body}'.");
        var candidateExecutions = await provider.GetRequiredService<IWorkflowExecutionStateStore>().ListAllAsync();
        var candidateHttpExecution = candidateExecutions.Single(state => state.PinnedExecutable.ArtifactId == candidatePublication.ArtifactId);
        Ensure(candidateHttpExecution.Status == WorkflowExecutionStatus.Completed,
            $"The candidate HTTP run ended in {candidateHttpExecution.Status}.");
        var candidateHttpInstruction = await ReadResponseInstructionAsync(provider, candidateHttpExecution.WorkflowExecutionId, ResponseNodeId);
        Ensure(candidateHttpInstruction.GetProperty("statusCode").GetInt32() == (int)candidateHttp.StatusCode,
            "The candidate's committed status instruction differs from the synchronous HTTP result.");
        Ensure(candidateHttpInstruction.GetProperty("body").GetString() == candidateHttp.Body,
            "The candidate's committed body instruction differs from the synchronous HTTP result.");
        Ensure(candidateHttpInstruction.GetProperty("contentType").GetString() == "text/plain" && candidateHttp.ContentType == "text/plain",
            "The candidate's committed or delivered content type differs from the authored value.");
        var candidateCommittedHeaders = ReadInstructionHeaders(candidateHttpInstruction);
        Ensure(HeadersEqual(candidateHttp.AuthoredHeaders, candidateCommittedHeaders),
            "The candidate's delivered authored headers differ from its committed response instruction.");
        var candidateFrame = candidateHttpExecution.RootVariableFrame
            ?? throw new InvalidOperationException("The completed candidate HTTP run has no durable root variable frame.");
        Ensure(ReadStringVariable(candidateFrame, "content", "firstName") == "Alice" &&
               ReadStringVariable(candidateFrame, "content", "lastName") == "Smith" &&
               ReadStringVariable(candidateFrame, "referenceText") == "Alice Smith",
            "The candidate did not retain the parsed request fields and computed referenceText in its committed variable frame.");

        // The REST companion has no HttpEndpoint. Its WorkflowRequest.content is an ordinary declared workflow input,
        // and the execute endpoint's 200 response is only the admission/dispatch receipt. The durable response
        // instruction and terminal state are read separately below.
        var companion = await SubmitWorkflowAsync(client,
            $"ResponseReplayRestCompanion-{Guid.NewGuid():N}",
            BuildRestCompanionState(versionIds));
        var companionPublication = await PublishWorkflowAsync(client, companion.VersionId);
        var restStart = await PostJsonAsync(client,
            $"/runtime/workflows/executables/{Uri.EscapeDataString(companionPublication.ArtifactId)}/execute",
            new
            {
                sourceReferenceId = companionPublication.SourceReferenceId,
                inputs = new { content = new { firstName = "Alice", lastName = "Smith" } }
            });
        Ensure(restStart.StatusCode == HttpStatusCode.OK, $"REST execute API returned HTTP {(int)restStart.StatusCode}.");
        using var startDocument = JsonDocument.Parse(restStart.Body);
        var restExecutionId = startDocument.RootElement.GetProperty("workflowExecutionId").GetString()
            ?? throw new InvalidOperationException("The runtime execute API did not return a workflow execution id.");
        var restExecution = await WaitForExecutionAsync(provider.GetRequiredService<IWorkflowExecutionStateStore>(), restExecutionId);
        Ensure(restExecution.PinnedExecutable.ArtifactId == companionPublication.ArtifactId,
            "The REST companion execution did not pin its published artifact.");
        Ensure(restExecution.Status == WorkflowExecutionStatus.Completed,
            $"The REST companion execution ended in {restExecution.Status}.");
        var restInstruction = await ReadResponseInstructionAsync(provider, restExecutionId, ResponseNodeId);
        Ensure(restInstruction.GetProperty("body").GetString() == "Alice Smith",
            "WorkflowRequest.content did not materialize and run through the REST companion's committed response instruction.");
        Ensure(restInstruction.GetProperty("contentType").GetString() == "text/plain" &&
               restInstruction.GetProperty("statusCode").GetInt32() == 200 &&
               !restInstruction.GetProperty("headers").EnumerateObject().Any(),
            "The REST companion's committed response instruction changed its authored status, content type, or empty headers.");
        var restFrame = restExecution.RootVariableFrame
            ?? throw new InvalidOperationException("The completed REST companion has no durable root variable frame.");
        Ensure(ReadStringVariable(restFrame, "content", "firstName") == "Alice" &&
               ReadStringVariable(restFrame, "content", "lastName") == "Smith" &&
               ReadStringVariable(restFrame, "referenceText") == "Alice Smith",
            "WorkflowRequest.content or the deterministic Set computation was not retained in the REST companion's committed variable frame.");

        var exportPath = Path.Combine(evidenceDirectory, "candidate-published-closure.json");
        using (var exported = await client.GetAsync(
                   $"/publishing/workflows/{Uri.EscapeDataString(submitted.VersionId)}/executable-export"))
        {
            Ensure(exported.IsSuccessStatusCode, $"Candidate closure export returned HTTP {(int)exported.StatusCode}.");
            var exportedBytes = await exported.Content.ReadAsByteArrayAsync();
            await File.WriteAllBytesAsync(exportPath, exportedBytes);
        }
        var candidateClosureHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            await File.ReadAllBytesAsync(exportPath))).ToLowerInvariant();

        return new PublicationProofResult(
            baselineHash,
            baseline.ArtifactId,
            baseline.ArtifactHash,
            baseline.DefinitionId,
            baseline.DefinitionVersionId,
            "External",
            baselineExecution.WorkflowExecutionId,
            baselineHttp.StatusCode,
            baselineHttp.Body,
            baselineHttp.ContentType,
            ReadStringVariable(baselineFrame, "content", "firstName"),
            ReadStringVariable(baselineFrame, "content", "lastName"),
            ReadStringVariable(baselineFrame, "referenceText"),
            baselineInstruction.GetProperty("headers").EnumerateObject().Count(),
            baselineHttp.AuthoredHeaders.Count,
            candidatePublication.ArtifactId,
            candidatePublication.ArtifactHash,
            candidatePublication.DefinitionId,
            submitted.VersionId,
            candidatePublication.SourceReferenceId,
            candidateResponseContract.SideEffectProfile.ToString(),
            candidate.Identity.ArtifactHash,
            candidateResponseContract.SchemaFingerprint,
            candidateRouteBindings.Length,
            candidateRouteBindings[0].ArtifactId,
            candidateHttpExecution.WorkflowExecutionId,
            candidateHttp.StatusCode,
            candidateHttp.Body,
            candidateHttp.ContentType,
            candidateHttpInstruction.GetProperty("statusCode").GetInt32(),
            candidateHttpInstruction.GetProperty("body").GetString()!,
            candidateHttpInstruction.GetProperty("contentType").GetString()!,
            candidateHttpInstruction.GetProperty("headers").EnumerateObject().Count(),
            candidateHttp.AuthoredHeaders.Count,
            ReadStringVariable(candidateFrame, "content", "firstName"),
            ReadStringVariable(candidateFrame, "content", "lastName"),
            ReadStringVariable(candidateFrame, "referenceText"),
            candidateHttpExecution.Status.ToString(),
            Path.GetFileName(exportPath),
            candidateClosureHash,
            companionPublication.ArtifactId,
            restStart.StatusCode,
            restExecutionId,
            restExecution.Status.ToString(),
            restInstruction.GetProperty("statusCode").GetInt32(),
            restInstruction.GetProperty("body").GetString()!,
            restInstruction.GetProperty("contentType").GetString()!,
            restInstruction.GetProperty("headers").EnumerateObject().Count(),
            ReadStringVariable(restFrame, "content", "firstName"),
            ReadStringVariable(restFrame, "content", "lastName"),
            ReadStringVariable(restFrame, "referenceText"));
    }

    private static async Task<WebApplication> StartHostAsync(string databasePath, string closurePath, string evidenceDirectory)
    {
        var connectionString = $"Data Source={databasePath};Pooling=False";
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [ShellWebRoutingPathKey] = "",
            ["CShells:Shells:default:Features:JsonWorkflowArtifactReconciliation:Options:FilePath"] = closurePath,
            ["CShells:Shells:default:Features:JsonWorkflowArtifactReconciliation:Options:SourceId"] = ArtifactReconciliationSourceId,
            ["CShells:Shells:default:Features:ClrActivityReconciliation:Options:FolderPath"] = AppContext.BaseDirectory,
            ["CShells:Shells:default:Features:WorkflowsRuntimeCheckpointPersistence:Mode"] = "Coalesced",
            ["CShells:Shells:default:Features:WorkflowsRuntimeCheckpointPersistence:MaxSegmentCheckpoints"] = "50",
            ["CShells:Shells:default:Features:WorkflowsRuntimeEntityFrameworkCore:Provider"] = "Sqlite",
            ["CShells:Shells:default:Features:WorkflowsRuntimeEntityFrameworkCore:ConnectionString"] = connectionString,
            ["CShells:Shells:default:Features:WorkflowsRuntimeEntityFrameworkCore:RecoveryContinuationSigningKey"] = "response-replay-publication-recovery-signing-key-32-bytes",
            ["CShells:Shells:default:Features:WorkflowsRuntimeEntityFrameworkCore:HierarchyCursorSigningKey"] = "response-replay-publication-hierarchy-signing-key-32-bytes",
            ["CShells:Shells:default:Features:WorkflowsDesignEntityFrameworkCore:Provider"] = "Sqlite",
            ["CShells:Shells:default:Features:WorkflowsDesignEntityFrameworkCore:ConnectionString"] = connectionString,
            ["CShells:Shells:default:Features:ActivitiesDesignEntityFrameworkCore:Provider"] = "Sqlite",
            ["CShells:Shells:default:Features:ActivitiesDesignEntityFrameworkCore:ConnectionString"] = connectionString,
            ["CShells:Shells:default:Features:WorkflowsPublishingEntityFrameworkCore:Provider"] = "Sqlite",
            ["CShells:Shells:default:Features:WorkflowsPublishingEntityFrameworkCore:ConnectionString"] = connectionString,
            ["CShells:Shells:default:Features:FileSystemDistributedLocking:LocksFolderPath"] = Path.Combine(evidenceDirectory, "locks")
        };

        var featureIds = new[]
        {
            "Primitives", "Serialization", "Tasks", "MemoryCache", "Mediator", "Events", "Expressions",
            "ApiCapabilities", "JavaScriptExpressions", "JavaScriptJintEngine", "ActivitiesRuntime", "ActivitiesPrimitives", "ActivitiesSequence",
            "ActivitiesHttp", "Http", "WorkflowsRuntimeHttp", "WorkflowsRuntimeApi", "WorkflowsRuntimeTriggers",
            "WorkflowsRuntimeResumption", "WorkflowsRuntimeRecurringTriggers", "WorkflowsRuntimeEntityFrameworkCore",
            "WorkflowsRuntimeCheckpointPersistence", "JsonWorkflowArtifactReconciliation", "ActivitiesDesignApi",
            "ActivitiesDesignEntityFrameworkCore", "ActivitiesDesignReconciliation", "ClrActivityReconciliation",
            "WorkflowDesignValidations", "WorkflowsDesignApi", "WorkflowsDesignEntityFrameworkCore", "WorkflowsPublishing",
            "WorkflowsPublishingApi", "WorkflowsPublishingEntityFrameworkCore", "FileSystemDistributedLocking",
            "ResponseReplayTestAuthentication"
        };
        foreach (var featureId in featureIds)
            values[$"CShells:Shells:default:Features:{featureId}"] = null;

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(ResponseReplayPublicationHost).Assembly.FullName
        });
        builder.Configuration.AddInMemoryCollection(values);
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Logging.AddConsole();
        builder.Services.AddCShellsAspNetCore(shells => shells
            .WithHostAssemblies()
            .WithAssemblies(
                typeof(PrimitivesFeature).Assembly,
                typeof(SerializationFeature).Assembly,
                typeof(TasksFeature).Assembly,
                typeof(MemoryCacheFeature).Assembly,
                typeof(MediatorFeature).Assembly,
                typeof(EventsFeature).Assembly,
                typeof(ExpressionsFeature).Assembly,
                typeof(ApiCapabilitiesFeature).Assembly,
                typeof(JavaScriptFeature).Assembly,
                typeof(JintFeature).Assembly,
                typeof(ActivitiesRuntimeFeature).Assembly,
                typeof(ActivitiesPrimitivesFeature).Assembly,
                typeof(ActivitiesSequenceFeature).Assembly,
                typeof(ActivitiesHttpFeature).Assembly,
                typeof(HttpFeature).Assembly,
                typeof(FileSystemLockingFeature).Assembly,
                typeof(WorkflowsRuntimeHttpFeature).Assembly,
                typeof(WorkflowsRuntimeApiFeature).Assembly,
                typeof(WorkflowsRuntimeTriggersFeature).Assembly,
                typeof(WorkflowsRuntimeResumptionFeature).Assembly,
                typeof(WorkflowsRuntimeRecurringTriggersFeature).Assembly,
                typeof(RuntimeEntityFrameworkCoreFeature).Assembly,
                typeof(WorkflowsRuntimeCheckpointPersistenceFeature).Assembly,
                typeof(JsonWorkflowArtifactReconciliationFeature).Assembly,
                typeof(ActivitiesDesignApiFeature).Assembly,
                typeof(ActivitiesDesignEntityFrameworkCoreFeature).Assembly,
                typeof(ActivitiesDesignReconciliationFeature).Assembly,
                typeof(ClrActivityReconciliationFeature).Assembly,
                typeof(WorkflowDesignValidationsFeature).Assembly,
                typeof(WorkflowsDesignApiFeature).Assembly,
                typeof(WorkflowsDesignEntityFrameworkCoreFeature).Assembly,
                typeof(WorkflowsPublishingFeature).Assembly,
                typeof(WorkflowsPublishingApiFeature).Assembly,
                typeof(PublishingEntityFrameworkCoreFeature).Assembly)
            .WithConfigurationProvider(builder.Configuration)
            .WithWebRouting(options =>
            {
                options.EnablePathRouting = true;
                options.LogMatches = true;
                options.NoMatchLogCandidateCap = 4;
            })
            .WithAuthenticationAndAuthorization());

        // Match Elsa.Workbench: register the normal root auth services after CShells installs its
        // shell-aware scheme provider, which the root authentication middleware uses per request.
        builder.Services.AddAuthentication();
        builder.Services.AddAuthorization();

        var app = builder.Build();
        app.MapShells();
        app.UseAuthentication();
        app.UseAuthorization();
        await app.StartAsync();
        return app;
    }

    private static async Task<string> GetCurrentActivityVersionAsync(HttpClient client, string activityTypeKey)
    {
        using var response = await client.GetAsync("/design/activities/catalog");
        Ensure(response.IsSuccessStatusCode, $"Activity catalog lookup for '{activityTypeKey}' returned HTTP {(int)response.StatusCode}.");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var item = document.RootElement.GetProperty("activities").EnumerateArray().SingleOrDefault(element =>
            string.Equals(element.GetProperty("activityTypeKey").GetString(), activityTypeKey, StringComparison.Ordinal));
        Ensure(item.ValueKind == JsonValueKind.Object,
            $"The authoring catalog did not contain activity type '{activityTypeKey}'. Returned keys: " +
            string.Join(", ", document.RootElement.GetProperty("activities").EnumerateArray()
                .Select(element => element.GetProperty("activityTypeKey").GetString())));
        return item.GetProperty("activityVersionId").GetString()
            ?? throw new InvalidOperationException($"Activity '{activityTypeKey}' has no current catalog version.");
    }

    private static async Task<SubmittedDefinition> SubmitWorkflowAsync(HttpClient client, string name, object state)
    {
        using var response = await client.PostAsJsonAsync("/design/workflows/definitions/submit", new
        {
            name,
            description = "Response replay safety candidate authored through the normal design API",
            state
        });
        Ensure(response.IsSuccessStatusCode, $"Workflow design submission returned HTTP {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return new SubmittedDefinition(
            document.RootElement.GetProperty("definition").GetProperty("id").GetString()!,
            document.RootElement.GetProperty("draftId").GetString()!,
            document.RootElement.GetProperty("version").GetProperty("id").GetString()!);
    }

    private static async Task<PublishedDefinition> PublishWorkflowAsync(HttpClient client, string versionId)
    {
        using var response = await client.PostAsJsonAsync(
            $"/publishing/workflows/{Uri.EscapeDataString(versionId)}/publish",
            new { });
        Ensure(response.IsSuccessStatusCode, $"Workflow publishing returned HTTP {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return new PublishedDefinition(
            document.RootElement.GetProperty("artifactId").GetString()!,
            document.RootElement.GetProperty("artifactHash").GetString()!,
            document.RootElement.GetProperty("definitionId").GetString()!,
            document.RootElement.GetProperty("versionId").GetString()!,
            document.RootElement.GetProperty("sourceReferenceId").GetString()!);
    }

    private static object BuildHttpWorkflowState(IReadOnlyDictionary<string, string> versions, string routePath)
    {
        var httpEndpoint = new
        {
            nodeId = HttpNodeId,
            activityVersionId = versions["httpEndpoint"],
            inputs = new object[]
            {
                Literal("Path", routePath),
                Literal("CanStartWorkflow", true),
                Literal("SupportedMethods", new[] { "POST" }),
                Literal("ResponseMode", "Sync")
            },
            outputs = new object[]
            {
                VariableOutput("Request", "request"),
                VariableOutput("RouteData", "route"),
                VariableOutput("ParsedContent", "content")
            }
        };
        var setReferenceText = BuildSetReferenceTextNode();
        var response = BuildWriteHttpResponseNode(versions["writeResponse"]);
        return new
        {
            variables = new object[]
            {
                Variable("request", "Object"),
                Variable("route", "Object"),
                Variable("content", "Object"),
                Variable("referenceText", "String")
            },
            inputs = Array.Empty<object>(),
            outputs = Array.Empty<object>(),
            workflowActivityOptions = (object?)null,
            strategyOptions = (object?)null,
            rootActivity = new
            {
                nodeId = "root",
                activityVersionId = versions["sequence"],
                inputs = Array.Empty<object>(),
                outputs = Array.Empty<object>(),
                structure = new
                {
                    kind = "elsa.sequence.structure",
                    schemaVersion = "1.0.0",
                    payload = new { activities = new object[] { httpEndpoint, setReferenceText, response } }
                }
            }
        };
    }

    private static object BuildRestCompanionState(IReadOnlyDictionary<string, string> versions)
    {
        return new
        {
            variables = new object[] { Variable("content", "Object"), Variable("referenceText", "String") },
            inputs = new object[]
            {
                new
                {
                    referenceKey = "content",
                    name = "content",
                    type = new { alias = "Object", collectionKind = "single" },
                    storageDriverType = (string?)null,
                    displayName = "content",
                    category = (string?)null,
                    isNullable = true
                }
            },
            outputs = Array.Empty<object>(),
            workflowActivityOptions = (object?)null,
            strategyOptions = (object?)null,
            rootActivity = new
            {
                nodeId = "root",
                activityVersionId = versions["sequence"],
                outputs = Array.Empty<object>(),
                inputs = Array.Empty<object>(),
                structure = new
                {
                    kind = "elsa.sequence.structure",
                    schemaVersion = "1.0.0",
                    payload = new
                    {
                        activities = new object[]
                        {
                            BuildSetWorkflowRequestContentNode(),
                            BuildSetReferenceTextNode(),
                            BuildWriteHttpResponseNode(versions["writeResponse"])
                        }
                    }
                }
            }
        };
    }

    private static object BuildSetWorkflowRequestContentNode() => BuildSetNode(
        "project-content",
        "content",
        "Object",
        new
        {
            value = new { memberKey = "content" },
            expressionType = "WorkflowRequest"
        });

    private static object BuildSetReferenceTextNode() => BuildSetNode(
        SetNodeId,
        "referenceText",
        "String",
        new
        {
            value = "getVariable('content').firstName + ' ' + getVariable('content').lastName",
            expressionType = "JavaScript"
        });

    private static object BuildSetNode(string nodeId, string variableKey, string valueAlias, object value) => new
    {
        nodeId,
        activityVersionId = "elsa.intrinsic.set@1",
        outputs = Array.Empty<object>(),
        intrinsic = new
        {
            kind = "set",
            valueType = new { alias = valueAlias, collectionKind = "single" },
            variable = new { referenceKey = variableKey }
        },
        inputs = new object[]
        {
            Argument("value", value)
        }
    };

    private static object BuildWriteHttpResponseNode(string activityVersionId) => new
    {
        nodeId = ResponseNodeId,
        activityVersionId,
        outputs = Array.Empty<object>(),
        inputs = new object[]
        {
            Literal("StatusCode", 200),
            Argument("Body", new { value = "getVariable('referenceText')", expressionType = "JavaScript" }),
            Literal("ContentType", "text/plain")
        }
    };

    private static object Variable(string key, string alias) => new
    {
        referenceKey = key,
        name = key,
        type = new { alias, collectionKind = "single" },
        storageDriverType = (string?)null,
        @default = new { value = "", expressionType = "Literal" }
    };

    private static object Literal(string key, object? value) => Argument(key, new { value, expressionType = "Literal" });

    private static object VariableOutput(string key, string variableKey) => Argument(key,
        new { value = new { referenceKey = variableKey }, expressionType = "Variable" });

    private static object Argument(string key, object value) => new
    {
        referenceKey = key,
        value,
        autoEvaluate = (bool?)null,
        evaluatorType = (string?)null,
        storageDriverType = (string?)null,
        isSensitive = (bool?)null
    };

    private static async Task<ActiveResponse> PostJsonAsync(HttpClient client, string path, object body)
    {
        using var response = await client.PostAsJsonAsync(path, body);
        var authoredHeaders = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in response.Headers.Concat(response.Content.Headers))
            if (!TransportHeaders.Contains(header.Key))
                authoredHeaders[header.Key] = header.Value.ToArray();

        return new ActiveResponse(
            response.StatusCode,
            await response.Content.ReadAsStringAsync(),
            response.Content.Headers.ContentType?.MediaType ?? "",
            authoredHeaders);
    }

    private static IReadOnlyDictionary<string, string[]> ReadInstructionHeaders(JsonElement instruction) =>
        instruction.GetProperty("headers").EnumerateObject().ToDictionary(
            property => property.Name,
            property => property.Value.EnumerateArray().Select(value => value.GetString() ?? "").ToArray(),
            StringComparer.OrdinalIgnoreCase);

    private static bool HeadersEqual(
        IReadOnlyDictionary<string, string[]> delivered,
        IReadOnlyDictionary<string, string[]> committed) =>
        delivered.Count == committed.Count &&
        delivered.All(pair => committed.TryGetValue(pair.Key, out var values) && pair.Value.SequenceEqual(values, StringComparer.Ordinal));

    private static string ReadStringVariable(VariableFrameState frame, string variableKey, string? memberName = null)
    {
        var value = frame.Values[variableKey].InlineValue
            ?? throw new InvalidOperationException($"Durable variable '{variableKey}' has no inline value.");
        return memberName is null
            ? value.GetString() ?? throw new InvalidOperationException($"Durable variable '{variableKey}' is not a string.")
            : value.GetProperty(memberName).GetString() ?? throw new InvalidOperationException($"Durable variable '{variableKey}.{memberName}' is not a string.");
    }

    private static async Task<WorkflowExecutionState> WaitForExecutionAsync(
        IWorkflowExecutionStateStore store,
        string executionId)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        do
        {
            var execution = (await store.ListAllAsync()).SingleOrDefault(state => state.WorkflowExecutionId == executionId);
            if (execution?.Status is WorkflowExecutionStatus.Completed or WorkflowExecutionStatus.Faulted or WorkflowExecutionStatus.Cancelled)
                return execution;
            await Task.Delay(100);
        } while (DateTimeOffset.UtcNow < deadline);

        throw new TimeoutException($"Execution '{executionId}' did not reach a terminal state within 30 seconds.");
    }

    private static async Task<JsonElement> ReadResponseInstructionAsync(IServiceProvider provider, string executionId, string nodeId)
    {
        var activities = await provider.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(executionId);
        var completion = activities.Single(state =>
            state.Execution.AuthoredActivityId == nodeId && state.Completion is not null).Completion!;
        return completion.Result.InlineValue?.Clone()
            ?? throw new InvalidOperationException($"Response node '{nodeId}' has no committed inline instruction.");
    }

    private static async Task<IReadOnlyList<WorkflowTriggerBinding>> ReadActiveHttpBindingsAsync(IWorkflowTriggerBindingStore store)
    {
        var bindings = new List<WorkflowTriggerBinding>();
        string? continuation = null;
        do
        {
            var page = await store.ListByStimulusTypeAsync(
                new WorkflowTriggerBindingTypePageQuery("HttpEndpoint", 100, continuation),
                CancellationToken.None);
            bindings.AddRange(page.Items);
            continuation = page.NextContinuationToken;
        } while (continuation is not null);
        return bindings;
    }

    private static BaselineIdentity ReadBaseline(byte[] closureBytes)
    {
        using var document = JsonDocument.Parse(closureBytes);
        var root = document.RootElement;
        var artifactId = root.GetProperty("rootArtifactId").GetString()!;
        var artifact = root.GetProperty("artifacts").EnumerateArray().Single(item =>
            item.GetProperty("identity").GetProperty("artifactId").GetString() == artifactId);
        var identity = artifact.GetProperty("identity");
        var nodes = artifact.GetProperty("rootActivity").GetProperty("childSlots")[0]
            .GetProperty("activities").EnumerateArray().ToArray();
        var http = nodes.Single(node => node.GetProperty("authoredActivityId").GetString() == HttpNodeId);
        return new BaselineIdentity(
            artifactId,
            identity.GetProperty("artifactHash").GetString()!,
            identity.GetProperty("definitionId").GetString()!,
            identity.GetProperty("definitionVersionId").GetString()!,
            http.GetProperty("inputBindings").GetProperty("Path").GetProperty("literalValue").GetString()!);
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed record BaselineIdentity(string ArtifactId, string ArtifactHash, string DefinitionId, string DefinitionVersionId, string RoutePath);
    private sealed record SubmittedDefinition(string DefinitionId, string DraftId, string VersionId);
    private sealed record PublishedDefinition(string ArtifactId, string ArtifactHash, string DefinitionId, string VersionId, string SourceReferenceId);
    private sealed record ActiveResponse(
        HttpStatusCode StatusCode,
        string Body,
        string ContentType,
        IReadOnlyDictionary<string, string[]> AuthoredHeaders);

}

internal sealed record PublicationProofResult(
    string BaselineClosureSha256,
    string BaselineArtifactId,
    string BaselineArtifactHash,
    string BaselineDefinitionId,
    string BaselineDefinitionVersionId,
    string BaselineProfile,
    string BaselineExecutionId,
    HttpStatusCode BaselineHttpStatus,
    string BaselineHttpBody,
    string BaselineHttpContentType,
    string BaselinePersistedFirstName,
    string BaselinePersistedLastName,
    string BaselineReferenceText,
    int BaselineCommittedHeaderCount,
    int BaselineDeliveredAuthoredHeaderCount,
    string CandidateArtifactId,
    string CandidateArtifactHash,
    string CandidateDefinitionId,
    string CandidateDefinitionVersionId,
    string CandidateSourceReferenceId,
    string CandidateProfile,
    string CandidateExecutableArtifactHash,
    string CandidateResponseContractFingerprint,
    int ActiveSharedRouteBindingCount,
    string ActiveSharedRouteArtifactId,
    string CandidateHttpExecutionId,
    HttpStatusCode CandidateHttpStatus,
    string CandidateHttpBody,
    string CandidateHttpContentType,
    int CandidateCommittedStatusCode,
    string CandidateCommittedBody,
    string CandidateCommittedContentType,
    int CandidateCommittedHeaderCount,
    int CandidateDeliveredAuthoredHeaderCount,
    string CandidatePersistedFirstName,
    string CandidatePersistedLastName,
    string CandidateReferenceText,
    string CandidateExecutionStatus,
    string CandidateClosureFile,
    string CandidateClosureSha256,
    string RestCompanionArtifactId,
    HttpStatusCode RestAdmissionStatus,
    string RestExecutionId,
    string RestExecutionStatus,
    int RestCommittedStatusCode,
    string RestCommittedBody,
    string RestCommittedContentType,
    int RestCommittedHeaderCount,
    string RestPersistedFirstName,
    string RestPersistedLastName,
    string RestReferenceText);

internal sealed class ResponseReplayAuthenticationHandler(
    Microsoft.Extensions.Options.IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    System.Text.Encodings.Web.UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(IdentityClaimTypes.Permission, PermissionKey.Wildcard),
            new Claim(IdentityClaimTypes.Normalized, "v1"),
            new Claim(ClaimTypes.Name, "response-replay-test")
        ], Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }
}

[ShellFeature(
    name: "ResponseReplayTestAuthentication",
    DisplayName = "Response Replay Test Authentication",
    Description = "Supplies an authorized principal to the response replay proof host.")]
public sealed class ResponseReplayTestAuthenticationFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddFoundationIdentityAbstractions(options =>
            options.NormalizedAuthenticationTypes = new HashSet<string>(["ResponseReplayProof"], StringComparer.Ordinal));
        services.AddAuthentication("ResponseReplayProof")
            .AddScheme<AuthenticationSchemeOptions, ResponseReplayAuthenticationHandler>("ResponseReplayProof", _ => { });
        services.AddAuthorization();
    }
}
