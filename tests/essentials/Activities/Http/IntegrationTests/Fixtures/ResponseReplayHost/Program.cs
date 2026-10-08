using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CShells.Lifecycle;
using Elsa.Activities.Http;
using Elsa.Activities.Http.Activities;
using Elsa.Activities.Http.Middleware;
using Elsa.Activities.Primitives;
using Elsa.Activities.Runtime;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Sequence;
using Elsa.Activities.Testing;
using Elsa.Events;
using Elsa.Expressions;
using Elsa.Expressions.JavaScript;
using Elsa.Expressions.JavaScript.Jint;
using Elsa.Http.Core;
using Elsa.Http.Core.Contracts;
using Elsa.Http.Services;
using Elsa.Persistence.EntityFramework;
using Elsa.Serialization.SystemText;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Api.Coalescing;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Http.Contracts;
using Elsa.Workflows.Runtime.Http.Services;
using Elsa.Workflows.Runtime.Http.Tasks;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Reconciliation;
using Elsa.Workflows.Runtime.Reconciliation.Contracts;
using Elsa.Workflows.Runtime.Reconciliation.Core.Models;
using Elsa.Workflows.Runtime.Reconciliation.Startup;
using Elsa.Workflows.Runtime.Scheduling;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

if (args.Length == 4 && args[0] == "--publication-proof")
{
    try
    {
        var result = await ResponseReplayPublicationHost.RunAsync(args[1], args[2], args[3]);
        Console.WriteLine($"RESPONSE_REPLAY_PUBLICATION_RESULT={JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web))}");
        return 0;
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine(exception);
        return 1;
    }
}

if (args.Length == 8 && args[0] == "--crash-stage")
{
    try
    {
        await ResponseReplayPublicationHost.RunCrashStageAsync(
            args[1], args[2], args[3], args[4], args[5], args[6], args[7]);
        return 0;
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine(exception);
        return 1;
    }
}

if (args.Length == 7 && args[0] == "--resume-recovery")
{
    try
    {
        var result = await ResponseReplayPublicationHost.RunCrashResumeAsync(
            args[1], args[2], args[3], args[4], args[5], args[6]);
        Console.WriteLine($"RESPONSE_REPLAY_RECOVERY_RESULT={JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web))}");
        return 0;
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine(exception);
        return 1;
    }
}

if (args.Length == 7 && args[0] == "--measurement-unpublish-candidate")
{
    try
    {
        await ResponseReplayPublicationHost.UnpublishCandidateForMeasurementAsync(
            args[1], args[2], args[3], args[4], args[5], args[6]);
        return 0;
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine(exception);
        return 1;
    }
}

if (args.Length == 8 && args[0] == "--measurement-reconcile-external")
{
    try
    {
        await ResponseReplayPublicationHost.ReconcileExternalForMeasurementAsync(
            args[1], args[2], args[3], args[4], args[5], args[6], args[7]);
        return 0;
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine(exception);
        return 1;
    }
}

if (args.Length == 13 && args[0] == "--measure")
{
    try
    {
        var result = await ResponseReplayPublicationHost.RunMeasurementAsync(
            args[1], args[2], args[3], args[4], args[5], args[6], args[7], args[8], args[9], args[10], args[11], args[12]);
        Console.WriteLine($"RESPONSE_REPLAY_MEASUREMENT_RESULT={JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web))}");
        return 0;
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine(exception);
        return 1;
    }
}

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: ResponseReplayHost <closure-path> <database-path> | --publication-proof <closure-path> <database-path> <evidence-directory> | --crash-stage <closure-path> <database-path> <evidence-directory> <pipe-name> <artifact-id> <artifact-hash> <request-correlation-id> | --resume-recovery <closure-path> <database-path> <evidence-directory> <execution-id> <artifact-id> <artifact-hash> | --measurement-unpublish-candidate <closure-path> <database-path> <evidence-directory> <candidate-definition-id> <candidate-artifact-id> <candidate-artifact-hash> | --measurement-reconcile-external <closure-path> <database-path> <evidence-directory> <baseline-artifact-id> <baseline-artifact-hash> <candidate-artifact-id> <candidate-artifact-hash> | --measure <closure-path> <database-path> <evidence-directory> <pipe-name> <artifact-id> <artifact-hash> <profile> <request-correlation-id> <baseline-artifact-id> <baseline-artifact-hash> <candidate-artifact-id> <candidate-artifact-hash>");
    return 2;
}

try
{
    var result = await ResponseReplayHost.RunAsync(args[0], args[1]);
    Console.WriteLine($"RESPONSE_REPLAY_RESULT={JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web))}");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
    return 1;
}

internal static class ResponseReplayHost
{
    private const string RouteBasePath = "/workflows/http/";
    private const string SourceId = "response-replay-pre-candidate-external";

    public static async Task<BaselineExecutionResult> RunAsync(string closurePath, string databasePath)
    {
        var closureBytes = await File.ReadAllBytesAsync(closurePath);
        var closureHash = Convert.ToHexString(SHA256.HashData(closureBytes)).ToLowerInvariant();
        var expected = ReadExpectedIdentity(closureBytes);

        using var host = BuildHost(closurePath, databasePath);
        await host.StartAsync();
        WorkflowExecutable executable;
        ExecutableNode responseNode;
        {
            foreach (var initializer in host.Services.GetServices<IShellInitializer>())
                await initializer.InitializeAsync();

            using (var scope = host.Services.CreateScope())
            {
                var provider = scope.ServiceProvider;
                var startupTasks = provider.GetServices<IStartupTask>().ToArray();
                foreach (var task in startupTasks.Where(task =>
                             task is not WorkflowArtifactReconcilerStartupTask && task is not UpdateRouteTableStartupTask))
                    await task.ExecuteAsync(CancellationToken.None);

                var reconciliation = await provider.GetRequiredService<IWorkflowArtifactReconciler>()
                    .ReconcileAsync(CancellationToken.None);
                var imported = reconciliation.Entries.Single();
                Ensure(imported.Outcome == WorkflowArtifactImportOutcome.Imported,
                    $"Expected the baseline closure to import, got {imported.Outcome}: {imported.Diagnostic}");
                Ensure(imported.ArtifactId == expected.ArtifactId, "The reconciler imported an unexpected artifact.");

                executable = await provider.GetRequiredService<IWorkflowExecutableStore>()
                    .FindAsync(expected.ArtifactId, CancellationToken.None)
                    ?? throw new InvalidOperationException("The reconciled executable was not stored.");
                Ensure(executable.Identity.ArtifactHash == expected.ArtifactHash, "The stored executable hash differs from the captured closure.");
                Ensure(executable.Identity.DefinitionId == expected.DefinitionId, "The stored definition identity differs from the captured closure.");
                Ensure(executable.Identity.DefinitionVersionId == expected.DefinitionVersionId, "The stored definition version differs from the captured closure.");
                responseNode = executable.Nodes.Single(node => node.AuthoredActivityId == expected.ResponseNodeId);
                Ensure(responseNode.ActivityType == typeof(WriteHttpResponse).FullName, "The imported response node has an unexpected CLR activity type.");
                Ensure(responseNode.ActivityContract?.SideEffectProfile == SideEffectProfile.External,
                    "The imported default profile did not resolve to External.");

                // Reconciliation activates the trigger projection through the normal coordinator. Rebuild the
                // process-local route table only after that activation is durable, as a cold runtime does.
                await startupTasks
                    .OfType<UpdateRouteTableStartupTask>()
                    .Single()
                    .ExecuteAsync(CancellationToken.None);
            }

            using var client = host.GetTestClient();
            using var request = new StringContent(
                "{\"firstName\":\"Alice\",\"lastName\":\"Smith\"}",
                Encoding.UTF8,
                "application/json");
            using var response = await client.PostAsync(RouteBasePath + expected.RoutePath, request);
            var responseBody = await response.Content.ReadAsStringAsync();
            Ensure(response.StatusCode == HttpStatusCode.OK, $"Expected HTTP 200, received {(int)response.StatusCode}.");
            Ensure(responseBody == "Alice Smith", $"Unexpected synchronous response body '{responseBody}'.");

            using var resultScope = host.Services.CreateScope();
            var resultProvider = resultScope.ServiceProvider;
            var executions = await resultProvider.GetRequiredService<IWorkflowExecutionStateStore>().ListAllAsync();
            var execution = executions.Single();
            Ensure(execution.Status == WorkflowExecutionStatus.Completed, $"Expected a completed workflow, got {execution.Status}.");
            Ensure(execution.PinnedExecutable.ArtifactId == expected.ArtifactId, "The execution did not pin the captured artifact.");
            Ensure(execution.PinnedExecutable.ArtifactHash == expected.ArtifactHash, "The execution did not pin the captured artifact hash.");

            var activityStates = await resultProvider.GetRequiredService<IActivityExecutionStateStore>()
                .ListAllAsync(execution.WorkflowExecutionId);
            var responseCompletion = activityStates.Single(state =>
                state.Execution.AuthoredActivityId == expected.ResponseNodeId && state.Completion is not null).Completion!;
            var instruction = responseCompletion.Result.InlineValue
                ?? throw new InvalidOperationException("The response completion has no committed inline instruction.");
            Ensure(instruction.GetProperty("statusCode").GetInt32() == (int)HttpStatusCode.OK,
                "The committed response instruction has an unexpected status code.");
            Ensure(instruction.GetProperty("body").GetString() == "Alice Smith",
                "The committed response instruction differs from the synchronous HTTP response.");

            await host.StopAsync();
            return new BaselineExecutionResult(
                closureHash,
                executable.Identity.ArtifactId,
                executable.Identity.ArtifactHash,
                executable.Identity.DefinitionId,
                executable.Identity.DefinitionVersionId,
                responseNode.ActivityContract!.SideEffectProfile.ToString(),
                responseNode.AuthoredActivityId,
                execution.WorkflowExecutionId,
                execution.PinnedExecutable.ArtifactId,
                execution.PinnedExecutable.ArtifactHash,
                execution.Status.ToString(),
                (int)response.StatusCode,
                responseBody,
                instruction.GetProperty("body").GetString()!);
        }
    }

    private static IHost BuildHost(string closurePath, string databasePath) => new HostBuilder()
        .ConfigureWebHost(webHost =>
        {
            webHost.UseTestServer();
            webHost.ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddMemoryCache();
                services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());

                new EventsFeature().ConfigureServices(services);
                new SerializationFeature().ConfigureServices(services);
                new ExpressionsFeature().ConfigureServices(services);
                new JavaScriptFeature().ConfigureServices(services);
                new JintFeature().ConfigureServices(services);
                new WorkflowsRuntimeApiFeature().ConfigureServices(services);
                new WorkflowsRuntimeTriggersFeature().ConfigureServices(services);
                new WorkflowsRuntimeRecurringTriggersFeature().ConfigureServices(services);
                new ActivitiesRuntimeFeature().ConfigureServices(services);
                new ActivitiesPrimitivesFeature().ConfigureServices(services);
                new ActivitiesSequenceFeature().ConfigureServices(services);
                new ActivitiesHttpFeature().ConfigureServices(services);

                services.AddSingleton<IRouteMatcher, TestRouteMatcher>();
                services.AddSingleton<IRouteTable, FakeRouteTable>();
                services.AddScoped<IHttpEndpointRoutesResolver, HttpEndpointRoutesResolver>();
                services.TryAddSingleton<IHttpEndpointRouteTableSynchronizer, HttpEndpointRouteTableSynchronizer>();
                services.AddScoped<IStartupTask, UpdateRouteTableStartupTask>();
                services.TryAddEnumerable(ServiceDescriptor.Singleton<IWorkflowTriggerIndexObserver, RouteTableTriggerIndexObserver>());
                services.TryAddEnumerable(ServiceDescriptor.Singleton<IWorkflowTriggerIndexValidator, HttpEndpointRoutingUniquenessValidator>());
                services.TryAddEnumerable(ServiceDescriptor.Singleton<IBookmarkLifecycleObserver, RouteTableBookmarkObserver>());
                services.AddSingleton<IHttpEndpointAuthorizationHandler, AuthenticationBasedHttpEndpointAuthorizationHandler>();
                services.AddSingleton<IHttpEndpointFaultHandler, HttpEndpointFaultHandler>();
                services.AddSingleton<IHttpRequestBodyParser, HttpRequestBodyParser>();
                services.AddAuthentication(TestAuthHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
                services.AddAuthorization();

                new JsonWorkflowArtifactReconciliationFeature
                {
                    Options = new()
                    {
                        FilePath = closurePath,
                        SourceId = SourceId
                    }
                }.ConfigureServices(services);

                services.AddRuntimeEntityFrameworkCore(new RuntimeEntityFrameworkCoreOptions
                {
                    Provider = "Sqlite",
                    ConnectionString = $"Data Source={databasePath};Pooling=False",
                    RecoveryContinuationSigningKey = "response-replay-host-recovery-signing-key-32-bytes",
                    HierarchyCursorSigningKey = "response-replay-host-hierarchy-signing-key-32-bytes"
                });
                services.AddEfModuleMigrations<RuntimeDbContext>("Sqlite");
                new WorkflowsRuntimeCheckpointPersistenceFeature
                {
                    Mode = CheckpointPersistenceMode.Coalesced,
                    MaxSegmentCheckpoints = 50
                }.PostConfigureServices(services);
            });
            webHost.Configure(app =>
            {
                app.UseAuthentication();
                app.UseMiddleware<HttpEndpointMiddleware>();
                app.Run(context =>
                {
                    context.Response.StatusCode = StatusCodes.Status418ImATeapot;
                    return Task.CompletedTask;
                });
            });
        })
        .Build();

    private static ExpectedIdentity ReadExpectedIdentity(byte[] closureBytes)
    {
        using var document = JsonDocument.Parse(closureBytes);
        var rootArtifactId = document.RootElement.GetProperty("rootArtifactId").GetString()!;
        var artifact = document.RootElement.GetProperty("artifacts").EnumerateArray()
            .Single(item => item.GetProperty("identity").GetProperty("artifactId").GetString() == rootArtifactId);
        var identity = artifact.GetProperty("identity");
        var responseNode = artifact.GetProperty("rootActivity").GetProperty("childSlots")[0]
            .GetProperty("activities").EnumerateArray()
            .Single(node => node.GetProperty("authoredActivityId").GetString() == "write-response");
        var routePath = artifact.GetProperty("rootActivity").GetProperty("childSlots")[0]
            .GetProperty("activities").EnumerateArray()
            .Single(node => node.GetProperty("authoredActivityId").GetString() == "http-in")
            .GetProperty("inputBindings").GetProperty("Path").GetProperty("literalValue").GetString()!;

        return new ExpectedIdentity(
            rootArtifactId,
            identity.GetProperty("artifactHash").GetString()!,
            identity.GetProperty("definitionId").GetString()!,
            identity.GetProperty("definitionVersionId").GetString()!,
            responseNode.GetProperty("authoredActivityId").GetString()!,
            routePath);
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed record ExpectedIdentity(
        string ArtifactId,
        string ArtifactHash,
        string DefinitionId,
        string DefinitionVersionId,
        string ResponseNodeId,
        string RoutePath);
}

internal sealed record BaselineExecutionResult(
    string ClosureSha256,
    string ArtifactId,
    string ArtifactHash,
    string DefinitionId,
    string DefinitionVersionId,
    string ResponseProfile,
    string ResponseNodeId,
    string ExecutionId,
    string PinnedArtifactId,
    string PinnedArtifactHash,
    string ExecutionStatus,
    int ResponseStatus,
    string ResponseBody,
    string CommittedResponseBody);
