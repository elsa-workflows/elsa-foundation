using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.KernelHost.Tests;

/// <summary>
/// Compiled executables, as <c>WorkflowExecutableCompiler</c> writes them: CLR activities by their catalog type keys,
/// intrinsics by their kind, structures and descriptors in the activity modules' payload shapes, and bindings by source.
/// </summary>
internal static class Compiled
{
    public const string ArtifactId = "artifact-7";
    public const string DefinitionId = "orders";
    public const string WorkflowScope = VariableReference.WorkflowScopeId;

    public static readonly ValueProtectionPolicy Durable = ValueProtectionPolicy.InstanceInline;

    public static class Types
    {
        public const string Sequence = "Elsa.Activities.Sequence.Activities.Sequence";
        public const string If = "Elsa.Activities.If.Activities.If";
        public const string Switch = "Elsa.Activities.Switch.Activities.Switch";
        public const string For = "Elsa.Activities.For.Activities.For";
        public const string ForEach = "Elsa.Activities.ForEach.Activities.ForEach";
        public const string While = "Elsa.Activities.While.Activities.While";
        public const string Do = "Elsa.Activities.Do.Activities.Do";
        public const string Parallel = "Elsa.Activities.Parallel.Activities.Parallel";
        public const string Flowchart = "Elsa.Activities.Flowchart.Activities.Flowchart";
        public const string Complete = "Elsa.Activities.Graph.Runtime.Activities.Complete";
        public const string WriteLine = "Elsa.Activities.Primitives.Activities.WriteLine";
        public const string Event = "Elsa.Activities.Primitives.Activities.Event";
        public const string Delay = "Elsa.Activities.Scheduling.Activities.Delay";
        public const string Timer = "Elsa.Timer";
        public const string Dispatch = "Elsa.Activities.DispatchWorkflow.Runtime.Activities.DispatchWorkflow";
        public const string HttpEndpoint = "Elsa.HttpEndpoint";
        public const string SendHttpRequest = "Elsa.Activities.Http.Activities.SendHttpRequest";
        public const string WriteHttpResponse = "Elsa.Activities.Http.Activities.WriteHttpResponse";
        public const string RunJavaScript = "Elsa.RunJavaScript";
        public const string CompensationScope = "Elsa.Activities.Compensation.Activities.CompensationScope";
    }

    public static WorkflowExecutable Executable(ExecutableNode root, params RuntimeVariableDeclaration[] variables) =>
        ExecutableOf(ArtifactId, DefinitionId, root, variables);

    public static WorkflowExecutable ExecutableOf(string artifactId, string definitionId, ExecutableNode root, params RuntimeVariableDeclaration[] variables) =>
        new(new WorkflowExecutableIdentity(artifactId, definitionId, $"{definitionId}-version", "1", $"sha256:{artifactId}"), root,
            new Dictionary<string, WorkflowExecutableResumeTarget>(), DateTimeOffset.UnixEpoch, new Dictionary<string, string>(),
            inputContract: null, dependencies: null, runtimeRequirements: null, storageDriverRequirements: null,
            new IncidentStrategyReference("Fault", "1"), workflowVariables: variables);

    public static RuntimeVariableDeclaration Variable(string key, string name, object? initial = null, string alias = "Object") =>
        new(key, name, new ValueTypeDescriptor(alias), Durable, initial is null ? null : Literal(name, initial, alias));

    // ---- nodes ---------------------------------------------------------------------------------------------------------

    public static ExecutableNode Node(string id, string type, RuntimeInputBinding[]? inputs = null, ExecutableActivityStructure? structure = null,
        ExecutableChildSlot[]? slots = null, RuntimeOutputCapture[]? captures = null, string descriptorType = WellKnownRuntimeActivityConsumers.ClrActivity,
        object? descriptor = null, string? displayName = null) =>
        new(id, id, type, "1.0.0", descriptorType, JsonSerializer.SerializeToElement(descriptor ?? new { }),
            (inputs ?? []).ToDictionary(input => input.InputKey, StringComparer.Ordinal),
            displayName is null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["displayName"] = displayName }, slots, structure,
            outputCaptures: (captures ?? []).ToDictionary(capture => capture.OutputName, StringComparer.Ordinal));

    public static ExecutableNode Intrinsic(string id, WorkflowIntrinsicKind kind, RuntimeVariableReference? variable, params RuntimeInputBinding[] inputs) =>
        new(id, id, $"elsa.intrinsic.{kind.ToString().ToLowerInvariant()}", "1.0.0", WellKnownRuntimeActivityConsumers.Intrinsic,
            JsonSerializer.SerializeToElement(new { kind = kind.ToString(), schemaVersion = "1.0.0" }),
            inputs.ToDictionary(input => input.InputKey, StringComparer.Ordinal), new Dictionary<string, string>(),
            intrinsicKind: kind, intrinsicVariable: variable);

    public static ExecutableNode Set(string id, string variableKey, RuntimeInputBinding value) =>
        Intrinsic(id, WorkflowIntrinsicKind.Set, new RuntimeVariableReference(variableKey, WorkflowScope), Rekey(value, WorkflowIntrinsicInputKeys.Value));

    public static ExecutableNode SetOutput(string id, string name, RuntimeInputBinding value) =>
        Intrinsic(id, WorkflowIntrinsicKind.SetOutput, null, Literal(WorkflowIntrinsicInputKeys.Name, name, "String"), Rekey(value, WorkflowIntrinsicInputKeys.Value));

    public static ExecutableNode Sequence(string id, params ExecutableNode[] children) =>
        Node(id, Types.Sequence, structure: Structure("elsa.sequence.structure", new { activities = children.Select(c => c.ExecutableNodeId), variables = Array.Empty<object>() }),
            slots: [Slot("Sequence.Activities", children)]);

    public static ExecutableNode WriteLine(string id, RuntimeInputBinding text) => Node(id, Types.WriteLine, [Rekey(text, "text")]);

    public static ExecutableActivityStructure Structure(string kind, object payload, string version = "1.0.0") =>
        new(kind, version, JsonSerializer.SerializeToElement(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    public static ExecutableChildSlot Slot(string name, params ExecutableNode[] children) => new(name, children);

    public static RuntimeOutputCapture Capture(string output, string variableName) =>
        new(output, $"variable:{variableName}", new RuntimeValueTypeDescriptor("Object", null, null), DurableValueLifecycle.Instance, DurableValueStorage.Inline, true);

    // ---- bindings ------------------------------------------------------------------------------------------------------

    public static RuntimeInputBinding Literal(string input, object? value, string alias = "Object") =>
        new(input, new ValueTypeDescriptor(alias), Durable, RuntimeInputBindingSource.Literal,
            literal: value is null ? ValueEnvelope.Null(new ValueTypeDescriptor(alias), Durable) : ValueEnvelope.Inline(new ValueTypeDescriptor(alias), JsonSerializer.SerializeToElement(value), Durable));

    public static RuntimeInputBinding VariableRead(string input, string variableKey, string scope = WorkflowScope, ValueProtectionPolicy? policy = null,
        ValueConversionPlan? plan = null) =>
        new(input, new ValueTypeDescriptor("Object"), policy ?? Durable, RuntimeInputBindingSource.VariableRead,
            variable: new RuntimeVariableReference(variableKey, scope), conversionPlan: plan);

    public static RuntimeInputBinding VariableMember(string input, string variableKey, params string[] segments) =>
        new(input, new ValueTypeDescriptor("Object"), Durable, RuntimeInputBindingSource.VariableMember, Selection(segments),
            variable: new RuntimeVariableReference(variableKey, WorkflowScope));

    public static RuntimeInputBinding Request(string input, string member) =>
        new(input, new ValueTypeDescriptor("Object"), Durable, RuntimeInputBindingSource.WorkflowRequest, workflowRequest: new RuntimeWorkflowRequestReference(member));

    public static RuntimeInputBinding RequestMember(string input, string member, params string[] segments) =>
        new(input, new ValueTypeDescriptor("Object"), Durable, RuntimeInputBindingSource.WorkflowRequestMember, Selection(segments),
            workflowRequest: new RuntimeWorkflowRequestReference(member));

    public static RuntimeInputBinding Result(string input, string producer, string projection) =>
        new(input, new ValueTypeDescriptor("Object"), Durable, RuntimeInputBindingSource.ActivityResult,
            activityResult: new RuntimeActivityResultReference(producer, projection, null));

    public static RuntimeInputBinding ResultMember(string input, string producer, string projection, params string[] segments) =>
        new(input, new ValueTypeDescriptor("Object"), Durable, RuntimeInputBindingSource.ActivityResultMember, Selection(segments),
            activityResult: new RuntimeActivityResultReference(producer, projection, null));

    public static RuntimeInputBinding Expression(string input, string language, string text, string alias,
        IReadOnlyDictionary<string, ExpressionParameterBinding>? parameters = null, IReadOnlyDictionary<string, ValueConversionPlan>? plans = null,
        object? options = null) =>
        new(input, new ValueTypeDescriptor(alias), Durable, RuntimeInputBindingSource.Expression,
            expression: new RuntimeExpressionBinding(language, text, parameters: parameters, parameterConversionPlans: plans,
                options: options is null ? null : JsonSerializer.SerializeToElement(options)));

    public static RuntimeInputBinding Secret(string input, string name) =>
        new(input, new ValueTypeDescriptor("String"), Durable, RuntimeInputBindingSource.SecretRead, secret: new RuntimeSecretReference(name));

    public static RuntimeInputBinding Rekey(RuntimeInputBinding binding, string input) =>
        new(input, binding.TargetType, binding.EffectivePolicy, binding.Source, binding.Selection, binding.Literal, binding.WorkflowRequest,
            binding.Variable, binding.ActivityResult, binding.Expression, binding.Metadata, binding.ConversionPlan, binding.ConversionRequest, binding.Secret);

    public static ValueConversionPlan NumericToString() =>
        new(ValueConversionPlan.CurrentSchemaVersion, ValueRepresentation.TypedValue, new ValueTypeDescriptor("Int32"), new ValueTypeDescriptor("String"),
            ValueConversionMode.Auto, ValueConversionOperation.NumericToString, null, null, null);

    public static ValueConversionPlan NumericWidening() =>
        new(ValueConversionPlan.CurrentSchemaVersion, ValueRepresentation.TypedValue, new ValueTypeDescriptor("Int32"), new ValueTypeDescriptor("Int64"),
            ValueConversionMode.Auto, ValueConversionOperation.NumericWidening, null, null, null);

    private static RuntimeReadSelection Selection(string[] segments) => new(RuntimeReadSelection.CurrentSchemaVersion, segments, RuntimeReadSelectionMode.DeferredFit);
}
