using System.Reflection;
using System.Text.Json;
using Bpmn.Model;
using Elsa.Activities.Sequence.Models;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Flowchart.Models;
using SequenceActivity = Elsa.Activities.Sequence.Activities.Sequence;
using FlowchartActivity = Elsa.Activities.Flowchart.Activities.Flowchart;
using Elsa.Activities.Bpmn.Core;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Jobs.Core;
using Elsa.Jobs.Workflows.Runtime.Activities;
using Elsa.Jobs.Workflows.Runtime.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

internal static partial class ModelProbe
{
    // Direct runtime publication through public immutable material contracts. This deliberately does not
    // claim the separate Design submit/publish journey. No execution state or bookmark is seeded here.
    private static async Task<object> CreateAsync(IServiceProvider services, ModelCommand command, CancellationToken ct)
    {
        if (command.ModelKind is not ("native" or "bpmn") || string.IsNullOrWhiteSpace(command.DefinitionId))
            throw new InvalidDataException("Choose native/bpmn and an explicit logical definition identity.");
        var policy = command.Policy ?? new JobExecutionPolicy(new(1, TimeSpan.FromMilliseconds(25), TimeSpan.FromSeconds(1), 2, 0, true),
            TimeSpan.FromMinutes(2), TimeSpan.FromDays(30));
        var pin = new JobWorkflowHandlerPin(1, Probe.Support.Handler, Probe.Support.InputSchema, Probe.Support.ResultSchema,
            "default", policy, command.AssociationMode);
        var jobClrType = command.AssociationMode == JobAssociationMode.Awaited ? typeof(RunJob) : typeof(SubmitJob);
        var jobActivityType = command.AssociationMode == JobAssociationMode.Awaited ? RunJob.ActivityType : SubmitJob.ActivityType;
        var jobResultType = command.AssociationMode == JobAssociationMode.Awaited ? typeof(RunJobResult) : typeof(SubmitJobResult);
        var input = command.InvalidInput switch
        {
            null => command.Report ?? throw new InvalidDataException("A useful report input is required."),
            "oversized" => JsonSerializer.SerializeToElement(new string('x', JobPayloadLimits.MaximumBytes + 128)),
            "depth" => JsonDocument.Parse(string.Concat(Enumerable.Repeat("{\"nested\":", 34)) + "0" + new string('}', 34)).RootElement.Clone(),
            _ => throw new InvalidDataException("Unknown pre-admission invalid-input profile.")
        };
        var selections = new (string Name, Type Type, object? Value)[]
        {
            (nameof(RunJob.Handler), typeof(JobHandlerIdentity), pin.Handler),
            (nameof(RunJob.InputSchema), typeof(JobSchema), pin.InputSchema),
            (nameof(RunJob.ResultSchema), typeof(JobSchema), pin.ResultSchema),
            (nameof(RunJob.Input), typeof(JsonElement), input),
            (nameof(RunJob.Queue), typeof(string), pin.Queue),
            (nameof(RunJob.Policy), typeof(JobExecutionPolicy), pin.Policy),
            (nameof(RunJob.NotBefore), typeof(DateTimeOffset?), null),
            (nameof(RunJob.AssociationMode), typeof(JobAssociationMode), pin.AssociationMode)
        };
        static ValueTypeDescriptor ValueType(Type type)
        {
            var reference = TypeReferenceFactory.FromClrType(type, TypeAliasConvention.CanonicalAlias);
            return new(reference.Alias, reference.CollectionKind);
        }
        var activityAlias = TypeAliasConvention.CanonicalAlias(jobClrType);
        var descriptorKind = typeof(ClrActivityDescriptor).FullName!;
        var descriptor = JsonSerializer.SerializeToElement(new ClrActivityDescriptor(activityAlias));
        var inputs = selections.Select(selection => new ActivityInputContract(selection.Name, selection.Name,
            ValueType(selection.Type), selection.Name is nameof(RunJob.Handler) or nameof(RunJob.InputSchema) or nameof(RunJob.ResultSchema) or nameof(RunJob.Policy),
            selection.Type == typeof(DateTimeOffset?), false, null, ActivityValuePolicy.Default)).ToArray();
        var contract = new ActivityContract(jobActivityType, "1.0.0", descriptorKind, descriptor, inputs,
            new(ValueType(jobResultType), true, ActivityValuePolicy.Default, []), ["Done"], new(descriptorKind, activityAlias));
        var bindings = selections.ToDictionary(selection => selection.Name, selection =>
        {
            var type = contract.Inputs[selection.Name].Type;
            return new RuntimeInputBinding(selection.Name, type, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Literal,
                literal: ValueEnvelope.Inline(type, JsonSerializer.SerializeToElement(selection.Value, Web), ValueProtectionPolicy.InstanceInline));
        });
        var jobNode = new ExecutableNode("report-job", "report-job", jobActivityType, "1.0.0",
            WellKnownRuntimeActivityConsumers.ClrActivity, descriptor, bindings,
            new Dictionary<string, string> { [RunJob.PinMetadataKey] = pin.Serialize(),
                ["witness.logical-definition"] = command.DefinitionId, ["witness.version"] = command.DefinitionVersion }, activityContract: contract);
        // Resume targets come from the actual public CLR declarations. SubmitJob has no result wait.
        var targets = new Dictionary<string, WorkflowExecutableResumeTarget>();
        if (command.AssociationMode == JobAssociationMode.Awaited) AddResumeTarget(typeof(RunJob), jobNode.ExecutableNodeId, targets);
        if (command.ModelKind == "bpmn" && command.NativeStructure != "root")
            throw new InvalidDataException("The genuine direct BPMN service profile requires its closed Jobs activity root.");
        var root = command.NativeStructure switch
        {
            "root" => jobNode,
            "sequence" => Sequence("sequence", jobNode),
            "flowchart" => Flowchart("flowchart", jobNode),
            "nested" => Sequence("outer-sequence", Flowchart("inner-flowchart", jobNode)),
            _ => throw new InvalidDataException("Unknown native executable structure.")
        };
        if (command.KeepParentActive)
        {
            if (command.ModelKind != "native" || command.AssociationMode == JobAssociationMode.Awaited || command.NativeStructure != "root")
                throw new InvalidDataException("The explicit cancellation control keeps only a native submission parent active.");
            var hold = EventWait(command.ExecutionId);
            AddResumeTarget(typeof(Event), hold.ExecutableNodeId, targets);
            root = Sequence("submission-sequence", jobNode, hold);
        }
        var inputContract = new WorkflowExecutableInputContract(1, []);
        var hasher = services.GetRequiredService<IWorkflowExecutableHasher>();
        var hash = hasher.ComputeHash(root, inputContract, [], PersistenceMode.Durable, [], IncidentStrategyBuiltIns.FaultReference);
        var identity = new WorkflowExecutableIdentity(hasher.CreateArtifactId("jobs-model-", hash), command.DefinitionId,
            command.DefinitionVersion, "1.0.0", hash);
        var executable = new WorkflowExecutable(identity, root, targets,
            DateTimeOffset.UtcNow, new Dictionary<string, string>(), inputContract, [], [], [], IncidentStrategyBuiltIns.FaultReference, PersistenceMode.Durable);
        if (!hasher.MatchesHash(executable)) throw new InvalidDataException("The published native material failed its actual content hash.");
        await services.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(executable, ct);
        if (command.ModelKind == "native")
        {
            var sourceId = "model-package-source:" + command.ExecutionId;
            var reference = new WorkflowExecutableSourceReference(sourceId, identity.ArtifactId, "WorkflowDefinitionVersion", identity.DefinitionVersionId,
                identity.ArtifactVersion, identity.DefinitionId, identity.DefinitionVersionId, identity.ArtifactVersion,
                executable.CreatedAt, executable.CreatedAt, WorkflowExecutableReferenceScope.Published,
                ActivationId: "publication:" + sourceId, SlotId: "slot:" + sourceId);
            await services.GetRequiredService<IWorkflowExecutableSourceReferenceStore>().SaveAsync(reference, ct);
            var authority = new WorkflowExecutionAuthoritySnapshot("model-package-witness", "model-package-witness");
            var started = await services.GetRequiredService<IWorkflowStartDispatcher>().DispatchAsync(new(identity.ArtifactId, authority.SystemIdentity,
                command.ExecutionId, "start:" + command.ExecutionId, null, null, null, null, null, WorkflowRunKind.PublishedRun,
                new(sourceId), WorkflowExecutableProvenanceRequirement.RequireLiveReference, null, null, null,
                new(command.Scope.PersistencePartition), authority), cancellationToken: ct);
            if (started.CommandDispatch.Status != WorkflowExecutionCommandDispatchStatus.Accepted)
                throw new InvalidDataException("The actual native start was not accepted.");
            return new { executionId = command.ExecutionId, workerExecutionId = command.ExecutionId, executable = identity, reference,
                publicationKind = "PublicRuntimeMaterial", modelKind = "elsa.native", command.NativeStructure, command.AssociationMode, command.KeepParentActive, jobNodeId = jobNode.ExecutableNodeId };
        }
        var process = new BpmnProcessDefinition(command.DefinitionId,
            Elements: [new("start", BpmnElementTypes.StartEvent), new("report", BpmnElementTypes.ServiceTask, bindingRef: "report-worker"), new("end", BpmnElementTypes.EndEvent)],
            SequenceFlows: [new("start-report", "start", "report"), new("report-end", "report", "end")]);
        var definition = new BpmnModelDefinition(3, JsonSerializer.SerializeToElement(process, Web), [],
            [new("report-worker", identity, "report.business-error", new(1, BpmnServiceFaultDisposition.FinalFault, 1))]);
        var runtime = services.GetRequiredService<IBpmnModelRuntime>();
        var caller = new BpmnModelCaller("model-package-witness", null);
        var published = await runtime.PublishAsync(definition, command.ExecutionId + ":publish", caller, ct);
        var outcome = await runtime.StartAsync(published.SourceReferenceId, command.ExecutionId + ":start", null, caller, ct);
        var inspection = await runtime.InspectAsync(outcome.ExecutionId, caller, ct);
        var service = inspection.Work.Single(item => item.Kind == "Native");
        if (service.WorkerExecutionId is null || await services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(outcome.ExecutionId, ct) is not null ||
            inspection.State.Protocol.Key != BpmnModelProtocol.Key)
            throw new InvalidDataException("The root is not a genuine direct BPMN model execution.");
        if (command.SendBpmnStart) await services.GetRequiredService<IRuntimePostCommitOutboxProcessor>().ProcessAsync(new(16, outcome.ExecutionId, BpmnStartKind), ct);
        return new { executionId = outcome.ExecutionId, workerExecutionId = service.WorkerExecutionId, executable = published.Executable,
            workerExecutable = identity, published.IncarnationId, published.SourceReferenceId, publicationKind = "PublicRuntimeModel", modelKind = BpmnModelProtocol.Key };
    }
    private static void AddResumeTarget(Type type, string nodeId, Dictionary<string, WorkflowExecutableResumeTarget> targets)
    {
        var method = type.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
            .Select(method => (Method: method, Target: method.GetCustomAttribute<ResumeTargetAttribute>()))
            .Single(item => item.Target is not null);
        var local = method.Target!.ResumeTargetId;
        var target = new WorkflowExecutableResumeTarget(WorkflowExecutableResumeTarget.ComposeScopedId(nodeId, local),
            nodeId, method.Method.Name, new Dictionary<string, string>(), local);
        targets.Add(target.ResumeTargetId, target);
    }

    private static ExecutableNode EventWait(string name)
    {
        var descriptorKind = typeof(ClrActivityDescriptor).FullName!;
        var alias = TypeAliasConvention.CanonicalAlias(typeof(Event));
        var descriptor = JsonSerializer.SerializeToElement(new ClrActivityDescriptor(alias));
        var selections = new (string Name, Type Type, object? Value)[]
        {
            (nameof(Event.EventName), typeof(string), "parent-lifetime:" + name),
            (nameof(Event.CorrelationId), typeof(string), null),
            (nameof(Event.CanStartWorkflow), typeof(bool), false)
        };
        static ValueTypeDescriptor TypeOf(Type type)
        {
            var reference = TypeReferenceFactory.FromClrType(type, TypeAliasConvention.CanonicalAlias);
            return new(reference.Alias, reference.CollectionKind);
        }
        var contract = new ActivityContract(typeof(Event).FullName!, "1.0.0", descriptorKind, descriptor,
            selections.Select(selection => new ActivityInputContract(selection.Name, selection.Name, TypeOf(selection.Type),
                selection.Name == nameof(Event.EventName), selection.Name == nameof(Event.CorrelationId), false, null, ActivityValuePolicy.Default)),
            new(TypeOf(typeof(EventResult)), true, ActivityValuePolicy.Default,
                [new("Result", "eventName", TypeOf(typeof(string)), true, ActivityValuePolicy.Default),
                 new("Payload", "payload", TypeOf(typeof(JsonElement?)), false, ActivityValuePolicy.Default)]),
            ["Done"], new(descriptorKind, alias));
        var bindings = selections.ToDictionary(selection => selection.Name, selection => new RuntimeInputBinding(selection.Name,
            contract.Inputs[selection.Name].Type, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(contract.Inputs[selection.Name].Type, JsonSerializer.SerializeToElement(selection.Value, Web), ValueProtectionPolicy.InstanceInline)));
        return new("parent-lifetime", "parent-lifetime", typeof(Event).FullName!, "1.0.0", WellKnownRuntimeActivityConsumers.ClrActivity,
            descriptor, bindings, new Dictionary<string, string>(), activityContract: contract);
    }

    private static ExecutableNode Sequence(string id, params ExecutableNode[] children) => StructuralNode(id, typeof(SequenceActivity),
        SequenceActivity.ActivitiesSlotName, SequenceActivity.StructureKind, SequenceActivity.StructureSchemaVersion,
        JsonSerializer.SerializeToElement(new SequenceExecutableStructure(children.Select(child => child.ExecutableNodeId).ToArray())), children);

    private static ExecutableNode Flowchart(string id, ExecutableNode child) => StructuralNode(id, typeof(FlowchartActivity),
        FlowchartActivity.ActivitiesSlotName, FlowchartActivity.StructureKind, FlowchartActivity.StructureSchemaVersion,
        JsonSerializer.SerializeToElement(new FlowchartStructure([], child.ExecutableNodeId)), [child]);

    private static ExecutableNode StructuralNode(string id, Type type, string slot, string structureKind, string structureVersion,
        JsonElement structure, ExecutableNode[] children)
    {
        var alias = TypeAliasConvention.CanonicalAlias(type);
        var descriptorKind = typeof(ClrActivityDescriptor).FullName!;
        var descriptor = JsonSerializer.SerializeToElement(new ClrActivityDescriptor(alias));
        var unit = TypeReferenceFactory.FromClrType(typeof(ActivityUnit), TypeAliasConvention.CanonicalAlias);
        var contract = new ActivityContract(type.FullName!, "1.0.0", descriptorKind, descriptor, [],
            new(new(unit.Alias, unit.CollectionKind), true, ActivityValuePolicy.Default, []), ["Done", "Break"],
            new(descriptorKind, alias), SideEffectProfile.ReplaySafe);
        return new(id, id, type.FullName!, "1.0.0", WellKnownRuntimeActivityConsumers.ClrActivity, descriptor,
            new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>(), [new(slot, children)],
            new(structureKind, structureVersion, structure), activityContract: contract);
    }
}
