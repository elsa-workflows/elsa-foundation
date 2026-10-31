using System.Text.Json;
using Acme.Specialists.Runtime;
using Elsa.Activities.Runtime;
using Elsa.Activities.Runtime.Contracts;
using Elsa.Workflows.Runtime.Api;
using Elsa.Serialization.SystemText;
using Elsa.Activities.Primitives;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Primitives.Models;
using Elsa.Activities.Runtime.Core.Models;
#if BPMN
using Elsa.Activities.Bpmn;
using Elsa.Activities.Bpmn.Models;
using BpmnProcess = Elsa.Activities.Bpmn.Activities.BpmnProcess;
#endif
using Elsa.Activities.Runtime.Tasks;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Serialization.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

var mode = args.Length > 1 ? args[1] : "native";
#if BPMN
const bool packageClosureDesignFree = false;
#else
const bool packageClosureDesignFree = true;
#endif
var template = JsonSerializer.Deserialize<ExecutableActivityTemplate>(await File.ReadAllTextAsync(Path.Combine(args[0], "template.json")))!;
if (template.Root.Descriptor.Payload.GetProperty("AgentVersionId").GetString() != "copywriter-v1") throw new InvalidOperationException("Wrong version");
var checks = new List<string>();
void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); checks.Add(name); }
var client = new FixtureClient { InvalidOutput = mode == "invalid-output" };
var services = new ServiceCollection();
new WorkflowsRuntimeApiFeature().ConfigureServices(services);
new ActivitiesRuntimeFeature().ConfigureServices(services);
new SerializationFeature().ConfigureServices(services);
new ActivitiesPrimitivesFeature().ConfigureServices(services);
#if BPMN
new ActivitiesBpmnFeature().ConfigureServices(services);
#endif
services.AddSingleton<IActivityActivationStrategy>(new SpecialistActivationStrategy(client));
services.AddPersistenceModeOptions(new PersistenceModeOptions { Mode = PersistenceMode.Durable });
services.AddPersistenceModes();
await using var provider = services.BuildServiceProvider();
var published = template.Root;
var inputType = new ValueTypeDescriptor("String");
var policy = ValueProtectionPolicy.InstanceInline;
var node = new ExecutableNode(published.ExecutableNodeId, published.AuthoredActivityId, published.ActivityType, published.ActivityTypeVersion,
    published.Descriptor, new Dictionary<string, RuntimeInputBinding>
    {
        ["Brief"] = new("Brief", inputType, policy, RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(inputType, JsonSerializer.SerializeToElement("moss gardens"), policy))
    }, published.OutputCaptures, published.Metadata, activityContract: published.ActivityContract);
#if BPMN
var root = BpmnRoot(node);
#else
var root = node;
#endif
await new RegisterActivityTypesStartupTask(provider.GetRequiredService<IWellKnownTypeRegistry>(),
    provider.GetServices<CShells.Features.IFeatureAssemblyProvider>(), provider, NullLogger<RegisterActivityTypesStartupTask>.Instance).ExecuteAsync(CancellationToken.None);
var identity = new WorkflowExecutableIdentity("editorial-" + mode, "editorial", "editorial-v1", "1.0.0", template.TemplateHash);
var now = DateTimeOffset.UtcNow;
var executable = new WorkflowExecutable(identity, root, new Dictionary<string, WorkflowExecutableResumeTarget>(), now,
    new Dictionary<string, string>(), IncidentStrategyBuiltIns.FaultReference, template.RuntimeRequirements, template.StorageDriverRequirements);
await provider.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(executable);
var executionId = mode + "-specialist-run";
var actor = await provider.GetRequiredService<IWorkflowExecutionActorProvider>().GetAgentAsync(new(
    executionId, WorkflowExecutionActorActivationReason.Start, now, "specialist-research", WorkflowExecutionActorCapabilities.InProcessMailbox));
var payload = new WorkflowExecutionStartCommandPayload(identity, identity.ArtifactId);
var metadata = new Dictionary<string, string> { ["runtime.artifactId"] = identity.ArtifactId };
var command = new WorkflowExecutionCommand("start-native", executionId, WorkflowExecutionCommandKind.Start, now, JsonSerializer.SerializeToElement(payload), metadata);
var envelope = new WorkflowExecutionCommandEnvelope("native-envelope", executionId, command, "native-start", WorkflowExecutionCommandDeliveryMode.AtLeastOnce, now, 1, metadata);
var dispatch = await actor.EnqueueAsync(envelope);
var workflow = await provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(executionId);
var activities = await provider.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(executionId);
if (mode == "invalid-output")
{
    var activity = activities.Single();
    if (workflow?.Status != WorkflowExecutionStatus.Faulted || activity.Completion is not null
        || activity.Fault?.Message != "specialist.output.invalid" || client.Calls != 1)
        throw new InvalidOperationException("Wrong-cause invalid-output failure: " + JsonSerializer.Serialize(new { workflow, activities }));
    throw new InvalidOperationException("EXPECTED_CONTROL:specialist.output.invalid:no-accepted-result");
}
if (workflow?.Status != WorkflowExecutionStatus.Completed)
    throw new InvalidOperationException(JsonSerializer.Serialize(new { mode, dispatch, workflow, activities }));
Check(true, mode + " workflow completed");
Check(client.Calls == 1 && client.LastBrief == "moss gardens", "actual runtime hydrated authored input and called MAF once");
Check(client.ModelId == "fixture-model" && client.Instructions == "Write the supplied brief.", "exact published model and instructions reached MAF client");
Check(activities.Single(x => x.Execution.ExecutableNodeId == published.ExecutableNodeId).Completion?.Result.InlineValue?.GetProperty("article").GetString() == "draft about moss gardens", "validated JSON result committed by Elsa");
Check(client.Schema is { } schema && JsonElement.DeepEquals(schema, published.Descriptor.Payload.GetProperty("OutputSchema")), "exact published schema reaches MAF client");
await actor.EnqueueAsync(envelope);
Check(client.Calls == 1, "duplicate start did not repeat completed model invocation");
await File.WriteAllTextAsync(Path.Combine(args[0], "consumer-" + mode + ".json"), JsonSerializer.Serialize(new { mode, checks, passed = checks.Count, packageClosureDesignFree, liveModel = false }));
Console.WriteLine(JsonSerializer.Serialize(new { mode, template.TemplateId, checks, passed = checks.Count, packageClosureDesignFree, liveModel = false }));

#if BPMN
static ExecutableNode BpmnRoot(ExecutableNode child)
{
    var type = typeof(BpmnProcess);
    var alias = TypeAliasConvention.CanonicalAlias(type);
    var descriptor = JsonSerializer.SerializeToElement(new ClrActivityDescriptor(alias));
    var boolean = new ValueTypeDescriptor("Boolean");
    var input = new Elsa.Activities.Runtime.Core.Models.ActivityInputContract("CanStartWorkflow", "CanStartWorkflow", boolean, false, false, true,
        JsonSerializer.SerializeToElement(true), ActivityValuePolicy.Default);
    var contract = new Elsa.Activities.Runtime.Core.Models.ActivityContract(alias, "1.0.0", typeof(ClrActivityDescriptor).FullName!, descriptor, [input],
        new ActivityResultContract(new ValueTypeDescriptor(TypeAliasConvention.CanonicalAlias(typeof(ActivityUnit))), true, ActivityValuePolicy.Default, []),
        ["Done"], new ActivityActivationRequirement(typeof(ClrActivityDescriptor).FullName!, alias));
    var structure = new BpmnStructure([
        new("start", BpmnElementTypes.StartEvent), new("write", BpmnElementTypes.ServiceTask, childNodeId: child.ExecutableNodeId),
        new("end", BpmnElementTypes.EndEvent)], [new("first", "start", "write"), new("last", "write", "end")]);
    return new ExecutableNode("bpmn-process", "bpmn-process", alias, "1.0.0", new RuntimeActivityDescriptor(WellKnownRuntimeActivityConsumers.ClrActivity, "1", descriptor),
        new Dictionary<string, RuntimeInputBinding> { [input.Key] = new(input.Key, boolean, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(boolean, JsonSerializer.SerializeToElement(true), ValueProtectionPolicy.InstanceInline)) },
        new Dictionary<string, RuntimeOutputCapture>(), new Dictionary<string, string>(),
        [new ExecutableChildSlot(BpmnProcess.ActivitiesSlotName, [child])],
        new ExecutableActivityStructure(BpmnProcess.StructureKind, BpmnProcess.StructureSchemaVersion, JsonSerializer.SerializeToElement(structure, new JsonSerializerOptions(JsonSerializerDefaults.Web))), contract);
}
#endif

sealed class FixtureClient : IChatClient
{
    public bool InvalidOutput { get; init; }
    public int Calls { get; private set; }
    public string? LastBrief { get; private set; }
    public string? Instructions { get; private set; }
    public string? ModelId { get; private set; }
    public JsonElement? Schema { get; private set; }
    public void Dispose() {}
    public object? GetService(Type serviceType, object? serviceKey = null) => null;
    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Calls++;
        var history = messages.ToArray();
        LastBrief = history.Last(x => x.Role == ChatRole.User).Text;
        Instructions = options?.Instructions;
        ModelId = options?.ModelId;
        Schema = (options?.ResponseFormat as ChatResponseFormatJson)?.Schema;
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, InvalidOutput ? "{\"wrong\":true}" : "{\"article\":\"draft about moss gardens\"}")));
    }
    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}
