using System.Text.Json;
using Elsa.Activities.Http.Activities;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Expressions.Core.Models;
using Elsa.Http.Core;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Values;
using ParallelActivity = Elsa.Activities.Parallel.Activities.Parallel;
using SequenceActivity = Elsa.Activities.Sequence.Activities.Sequence;

namespace Elsa.Activities.Http.IntegrationTests;

/// <summary>
/// The executables the EF command budgets run, built by hand to mirror what publication compiles: each contract is
/// scanned from the CLR activity type, so side-effect profiles match production, and un-authored inputs take their
/// contract defaults. Other tests on the durable SQLite host build their executables with the same builders.
/// </summary>
internal static class ReferenceWorkflows
{
    public const string HttpPath = "budget/http-reference";
    public const string EventName = "budget-resume";
    public const string EventNodeId = "wait-for-event";

    private static readonly ValueProtectionPolicy Inline = ValueProtectionPolicy.InstanceInline;

    /// <summary>A Sequence of ten WriteLine activities.</summary>
    public static WorkflowExecutable WriteLineSequence() => WriteLineSequence("writeline-sequence", lines: 10);

    /// <summary>A Sequence of <paramref name="lines"/> WriteLine activities.</summary>
    public static WorkflowExecutable WriteLineSequence(string artifactId, int lines) =>
        Executable(artifactId, Sequence("sequence", WriteLines(lines)));

    /// <summary>
    /// A Sequence of a synchronous POST HttpEndpoint at <paramref name="path"/> that starts the workflow,
    /// <paramref name="lines"/> WriteLine activities, and a WriteHttpResponse that answers the request after them.
    /// </summary>
    public static WorkflowExecutable HttpTriggeredWriteLineSequence(string artifactId, string path, int lines)
    {
        var endpoint = SyncPostEndpoint("http-in", path);

        return Executable(
            artifactId,
            Sequence("root", [endpoint, .. WriteLines(lines), DoneResponse()]),
            [],
            ResumeTarget(endpoint.ExecutableNodeId, HttpEndpoint.ResumeTargetId));
    }

    /// <summary>
    /// The four-activity HTTP workflow: a Sequence of a synchronous POST HttpEndpoint that captures its request into
    /// workflow variables, a Set that computes a name from the parsed body in JavaScript, and a WriteHttpResponse that
    /// returns it.
    /// </summary>
    public static WorkflowExecutable HttpReference()
    {
        var endpoint = SyncPostEndpoint(
            "http-in",
            HttpPath,
            [Capture("Request", "request"), Capture("RouteData", "route"), Capture("ParsedContent", "content")]);
        var setReferenceText = SetVariable(
            "set-reference-text",
            "referenceText",
            JavaScript(WorkflowIntrinsicInputKeys.Value, "String", "getVariable('content').firstName + ' ' + getVariable('content').lastName"));
        var response = Clr(
            "write-response",
            typeof(WriteHttpResponse),
            [(nameof(WriteHttpResponse.StatusCode), 200), (nameof(WriteHttpResponse.ContentType), "text/plain")],
            [JavaScript(nameof(WriteHttpResponse.Body), "String", "getVariable('referenceText')")]);

        return Executable(
            "http-reference",
            Sequence("root", endpoint, setReferenceText, response),
            [Variable("request", "Object"), Variable("route", "Object"), Variable("content", "Object"), Variable("referenceText", "String")],
            ResumeTarget(endpoint.ExecutableNodeId, HttpEndpoint.ResumeTargetId));
    }

    /// <summary>A Parallel fork/join whose second branch suspends on an Event bookmark until it is resumed.</summary>
    public static WorkflowExecutable ForkJoinResume() =>
        Executable(
            "fork-join-resume",
            Parallel("fork-join", ("write", Clr("write-in-branch", typeof(WriteLine), [("text", "forked")])), ("wait", EventWait())),
            [],
            EventResumeTarget());

    /// <summary>An Event activity at <see cref="EventNodeId"/> that suspends on a <see cref="EventName"/> bookmark.</summary>
    internal static ExecutableNode EventWait() =>
        Clr(EventNodeId, typeof(Event), [(nameof(Event.EventName), EventName), (nameof(Event.CanStartWorkflow), false)]);

    /// <summary>The resume target of <see cref="EventWait"/>.</summary>
    internal static (string Id, WorkflowExecutableResumeTarget Target) EventResumeTarget() => ResumeTarget(EventNodeId, Event.ResumeTargetId);

    /// <summary>A WriteHttpResponse that answers 200 with the plain-text body <c>done</c>.</summary>
    internal static ExecutableNode DoneResponse() =>
        Clr(
            "write-response",
            typeof(WriteHttpResponse),
            [(nameof(WriteHttpResponse.StatusCode), 200), (nameof(WriteHttpResponse.ContentType), "text/plain"), (nameof(WriteHttpResponse.Body), "done")]);

    /// <summary>Builds a Parallel node with one named branch per supplied activity.</summary>
    internal static ExecutableNode Parallel(string nodeId, params (string Name, ExecutableNode Activity)[] branches) =>
        Clr(
            nodeId,
            typeof(ParallelActivity),
            [],
            childSlots: branches.Select(branch => new ExecutableChildSlot(ParallelActivity.BranchSlotName(branch.Name), [branch.Activity])).ToArray(),
            structure: new ExecutableActivityStructure(
                ParallelActivity.StructureKind,
                ParallelActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new
                {
                    branches = branches.Select(branch => new { name = branch.Name, activity = branch.Activity.ExecutableNodeId }).ToArray()
                })));

    /// <summary>Builds a test executable with the supplied root, variables, and resume targets using the fault incident strategy.</summary>
    internal static WorkflowExecutable Executable(
        string name,
        ExecutableNode root,
        IReadOnlyCollection<RuntimeVariableDeclaration>? variables = null,
        params (string Id, WorkflowExecutableResumeTarget Target)[] resumeTargets) =>
        new(
            new WorkflowExecutableIdentity($"artifact-{name}", $"definition-{name}", $"version-{name}", "1.0.0", $"sha256:{name}"),
            root,
            resumeTargets.ToDictionary(target => target.Id, target => target.Target, StringComparer.Ordinal),
            DateTimeOffset.UtcNow,
            new Dictionary<string, string>(),
            inputContract: null,
            dependencies: null,
            runtimeRequirements: null,
            storageDriverRequirements: null,
            IncidentStrategyBuiltIns.FaultReference,
            workflowVariables: variables);

    /// <summary><paramref name="lines"/> WriteLine activities, <c>write-1</c> to <c>write-{lines}</c>.</summary>
    internal static ExecutableNode[] WriteLines(int lines) =>
        Enumerable.Range(1, lines)
            .Select(line => Clr($"write-{line}", typeof(WriteLine), [("text", $"line {line}")]))
            .ToArray();

    /// <summary>A synchronous POST HttpEndpoint that can start its workflow, open to anonymous requests.</summary>
    internal static ExecutableNode SyncPostEndpoint(string nodeId, string path, RuntimeOutputCapture[]? outputCaptures = null) =>
        Clr(
            nodeId,
            typeof(HttpEndpoint),
            [
                (nameof(HttpEndpoint.Path), path),
                (nameof(HttpEndpoint.CanStartWorkflow), true),
                (nameof(HttpEndpoint.SupportedMethods), new[] { "POST" }),
                (nameof(HttpEndpoint.Authorize), false),
                (nameof(HttpEndpoint.ResponseMode), ResponseMode.Sync)
            ],
            outputCaptures: outputCaptures);

    /// <summary>Builds a sequence node with child slots and execution order derived from the supplied children.</summary>
    internal static ExecutableNode Sequence(string nodeId, params ExecutableNode[] children) =>
        Clr(
            nodeId,
            typeof(SequenceActivity),
            [],
            childSlots: SequenceShape.ChildSlots(children),
            structure: SequenceShape.Structure(children));

    /// <summary>Builds a CLR activity node from its scanned contract, applying authored bindings and defaults for missing inputs.</summary>
    internal static ExecutableNode Clr(
        string nodeId,
        Type activityType,
        (string Key, object Value)[] literals,
        RuntimeInputBinding[]? bindings = null,
        IReadOnlyCollection<ExecutableChildSlot>? childSlots = null,
        ExecutableActivityStructure? structure = null,
        RuntimeOutputCapture[]? outputCaptures = null)
    {
        // Publication keys the node and its contract by the activity's declared type, falling back to its CLR name.
        var activityTypeKey = ActivityTypeMetadata.GetDeclaredActivityType(activityType) ?? activityType.FullName!;
        var scanned = ClrActivityContractTestBuilder.BuildContract(activityType);
        var contract = new ActivityContract(
            activityTypeKey,
            scanned.ContractVersion,
            scanned.DescriptorKind,
            scanned.DescriptorPayload,
            scanned.Inputs.Values,
            scanned.Result,
            scanned.Outcomes,
            scanned.Activation,
            scanned.SideEffectProfile);
        var authored = literals
            .Select(literal => Literal(contract.Inputs[literal.Key], literal.Value))
            .Concat(bindings ?? [])
            .ToDictionary(binding => binding.InputName, StringComparer.Ordinal);

        return new ExecutableNode(
            executableNodeId: nodeId,
            authoredActivityId: nodeId,
            activityType: activityTypeKey,
            activityTypeVersion: contract.ContractVersion,
            descriptorType: WellKnownRuntimeActivityConsumers.ClrActivity,
            descriptorPayload: contract.DescriptorPayload,
            inputBindings: ClrActivityContractTestBuilder.CompleteInputBindings(contract, authored),
            metadata: Metadata(nodeId, ActivityTypeMetadata.IsTrigger(activityType) ? TriggerNodeMetadata.TriggerExecutionType : "Action"),
            childSlots: childSlots,
            structure: structure,
            activityContract: contract,
            outputCaptures: outputCaptures?.ToDictionary(capture => capture.OutputName, StringComparer.Ordinal));
    }

    private static ExecutableNode SetVariable(string nodeId, string variable, RuntimeInputBinding value) =>
        new(
            executableNodeId: nodeId,
            authoredActivityId: nodeId,
            activityType: "elsa.intrinsic.set",
            activityTypeVersion: "1.0.0",
            descriptorType: WellKnownRuntimeActivityConsumers.Intrinsic,
            descriptorPayload: JsonSerializer.SerializeToElement(new { kind = nameof(WorkflowIntrinsicKind.Set), schemaVersion = "1.0.0" }),
            inputBindings: new Dictionary<string, RuntimeInputBinding> { [value.InputName] = value },
            metadata: Metadata(nodeId, "Action"),
            intrinsicKind: WorkflowIntrinsicKind.Set,
            intrinsicVariable: new RuntimeVariableReference(variable, VariableReference.WorkflowScopeId));

    private static Dictionary<string, string> Metadata(string nodeId, string executionType) => new(StringComparer.Ordinal)
    {
        ["authoredNodeId"] = nodeId,
        [TriggerNodeMetadata.ExecutionTypeKey] = executionType
    };

    private static RuntimeInputBinding Literal(ActivityInputContract input, object value) =>
        new(input.Key, input.Type, Inline, RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(input.Type, JsonSerializer.SerializeToElement(value), Inline));

    private static RuntimeInputBinding JavaScript(string inputKey, string alias, string script) =>
        new(inputKey, new ValueTypeDescriptor(alias), Inline, RuntimeInputBindingSource.Expression,
            expression: new RuntimeExpressionBinding("JavaScript", script, new RuntimeValueTypeDescriptor("alias", alias, null)));

    /// <summary>A workflow variable with the empty-string default the authored reference workflow declares.</summary>
    private static RuntimeVariableDeclaration Variable(string key, string alias)
    {
        var type = new ValueTypeDescriptor(alias);
        var initial = new RuntimeInputBinding(key, type, Inline, RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(type, JsonSerializer.SerializeToElement(string.Empty), Inline));
        return new RuntimeVariableDeclaration(key, key, type, Inline, initial);
    }

    /// <summary>Captures an activity result projection into a workflow variable, as an authored Variable output does.</summary>
    private static RuntimeOutputCapture Capture(string output, string variable) =>
        new(
            output,
            $"{RuntimeWorkflowStateSeed.VariableValueIdPrefix}{variable}",
            new RuntimeValueTypeDescriptor("Object", WellKnownRuntimeDurableValueStorageDrivers.Json, null),
            DurableValueLifecycle.Instance,
            DurableValueStorage.Custom,
            captureOnSuccessfulCompletion: true,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["referenceKey"] = output,
                [RuntimeMetadataKeys.TargetVariableReferenceKey] = variable,
                [RuntimeMetadataKeys.VariableName] = variable,
                [RuntimeMetadataKeys.StorageDriverKey] = WellKnownRuntimeDurableValueStorageDrivers.Json
            });

    internal static (string Id, WorkflowExecutableResumeTarget Target) ResumeTarget(string nodeId, string localResumeTargetId)
    {
        var id = WorkflowExecutableResumeTarget.ComposeScopedId(nodeId, localResumeTargetId);
        return (id, new WorkflowExecutableResumeTarget(id, nodeId, "ResumeAsync", new Dictionary<string, string>(), localResumeTargetId));
    }
}
