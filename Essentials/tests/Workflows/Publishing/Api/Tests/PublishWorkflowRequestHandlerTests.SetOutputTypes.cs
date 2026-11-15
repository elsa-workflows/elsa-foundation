using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Validations.Core.Models;
using Elsa.Workflows.Publishing.Core.Requests;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Resolvers;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Services.Values;
using Elsa.Workflows.Testing;
using Xunit;
using ArgumentValue = Elsa.Expressions.Core.Models.ArgumentValue;
using VariableDefinition = Elsa.Expressions.Core.Models.VariableDefinition;
using VariableReference = Elsa.Expressions.Core.Models.VariableReference;
using WorkflowArgumentState = Elsa.Workflows.Design.Core.Models.ArgumentState;

namespace Elsa.Workflows.Publishing.Api.Tests;

/// <summary>
/// Studio authors SetOutput with the dynamic <c>Elsa.Any</c> value type. The declared workflow output it names owns
/// the assignment contract, as a variable target does for Set, so the value publishes as the output's declared type
/// and a run reports the output with that type rather than <c>Elsa.Any</c> (#4069).
/// </summary>
public sealed partial class PublishWorkflowRequestHandlerTests
{
    private static readonly OutputDefinition CountOutput =
        new("count-ref", "Count", new TypeReference("Int32"), null, "Count", null, IsNullable: false);

    [Fact]
    public async Task A_dynamic_SetOutput_value_publishes_as_the_declared_outputs_type()
    {
        var binding = await PublishSetOutputValueAsync("Count", new TypeReference("Elsa.Any"));

        Assert.Equal("Int32", binding.TargetType.Alias);
    }

    [Fact]
    public async Task An_explicitly_typed_SetOutput_value_keeps_its_authored_type()
    {
        var binding = await PublishSetOutputValueAsync("Count", new TypeReference("String"), value: "7");

        Assert.Equal("String", binding.TargetType.Alias);
    }

    [Fact]
    public async Task A_SetOutput_naming_no_declared_output_keeps_its_authored_type()
    {
        var binding = await PublishSetOutputValueAsync("Renamed", new TypeReference("Elsa.Any"));

        Assert.Equal("Elsa.Any", binding.TargetType.Alias);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_dynamic_portable_SetOutput_preserves_its_result_contract_and_converts_to_the_declared_output(bool serialized)
    {
        var expression = SetOutputExpression(new TypeReference("Elsa.Any"));
        var argument = new ArgumentValue(serialized ? JsonSerializer.SerializeToElement(expression) : expression, "JavaScript");

        var binding = await PublishSetOutputValueAsync("Count", new TypeReference("Elsa.Any"), argument: argument);

        Assert.Equal(RuntimeInputBindingSource.Expression, binding.Source);
        Assert.Equal("Elsa.Any", binding.Expression!.ResultType!.Id);
        Assert.Equal(expression.Source, binding.Expression.Expression);
        Assert.Equal(expression.CapabilityProfile, binding.Expression.CapabilityProfile);
        Assert.Equal("Int32", binding.TargetType.Alias);
        var plan = Assert.IsType<ValueConversionPlan>(binding.ConversionPlan);
        Assert.Equal("Elsa.Any", plan.SourceType.Alias);
        Assert.Equal("Int32", plan.TargetType.Alias);
        Assert.Equal(ValueConversionOperation.Profile, plan.Operation);
        Assert.Equal("elsa.json", plan.Profile!.Id);
    }

    [Theory]
    [InlineData("Int32", CollectionKind.Single)]
    [InlineData("Elsa.Any", CollectionKind.List)]
    public async Task A_portable_SetOutput_still_refuses_a_result_contract_that_differs_from_its_authored_value_type(string alias, CollectionKind collection)
    {
        var expression = SetOutputExpression(new TypeReference(alias, collection));

        var exception = await Assert.ThrowsAsync<WorkflowExecutableCompilationException>(() =>
            PublishSetOutputValueAsync("Count", new TypeReference("Elsa.Any"), argument: new ArgumentValue(expression, "JavaScript")));

        Assert.Contains("portable expression declares result type", exception.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("raw", CollectionKind.Single)]
    [InlineData("typed", CollectionKind.Single)]
    [InlineData("serialized", CollectionKind.Single)]
    [InlineData("raw", CollectionKind.Array)]
    [InlineData("typed", CollectionKind.Array)]
    [InlineData("serialized", CollectionKind.Array)]
    [InlineData("raw", CollectionKind.List)]
    [InlineData("typed", CollectionKind.List)]
    [InlineData("serialized", CollectionKind.List)]
    [InlineData("raw", CollectionKind.HashSet)]
    [InlineData("typed", CollectionKind.HashSet)]
    [InlineData("serialized", CollectionKind.HashSet)]
    [InlineData("raw", CollectionKind.Dictionary)]
    [InlineData("typed", CollectionKind.Dictionary)]
    [InlineData("serialized", CollectionKind.Dictionary)]
    public async Task A_dynamic_SetOutput_keeps_its_open_contract_for_an_Object_output(string form, CollectionKind collection)
    {
        var source = collection switch
        {
            CollectionKind.Single => "7",
            CollectionKind.Dictionary => "({ key: 7 })",
            _ => "[7]"
        };
        var expression = SetOutputExpression(new TypeReference("Elsa.Any"), source);
        var argument = new ArgumentValue(form switch
        {
            "raw" => source,
            "serialized" => JsonSerializer.SerializeToElement(expression),
            _ => (object)expression
        }, "JavaScript");

        var binding = await PublishSetOutputValueAsync("Count", new TypeReference("Elsa.Any"),
            argument: argument, declaredOutputType: new TypeReference("Object", collection));

        Assert.Equal(RuntimeInputBindingSource.Expression, binding.Source);
        Assert.Equal("Elsa.Any", binding.TargetType.Alias);
        Assert.Equal(CollectionKind.Single, binding.TargetType.CollectionKind);
        Assert.Equal("Elsa.Any", binding.Expression!.ResultType!.Id);
        Assert.Equal(source, binding.Expression.Expression);
        Assert.Null(binding.ConversionPlan);
    }

    public static IEnumerable<object[]> SetOutputWholeVariableAutoCases =>
        from alias in new[] { "Int32", "System.Text.Json.JsonElement" }
        from request in WholeVariableConversionTests.DefaultRequests
        select new object[] { alias, request };

    public static IEnumerable<object[]> SetOutputWholeVariableRefusalCases =>
        from alias in new[] { "Int32", "System.Text.Json.JsonElement", "Object" }
        from intent in WholeVariableConversionTests.ConversionIntents
        select new object[] { alias, intent };

    [Theory]
    [MemberData(nameof(SetOutputWholeVariableAutoCases))]
    public async Task A_dynamic_SetOutput_with_ordinary_Variable_Auto_publishes_and_executes_the_declared_output(string alias, string request)
    {
        var version = SetOutputVariableVersion(alias, WholeVariableConversionTests.Request(request));

        var publication = await Handler(version).Handle(new PublishWorkflow(version.Id), CancellationToken.None);

        var executable = Assert.IsType<WorkflowExecutable>(await _store.FindAsync(publication.ArtifactId));
        var binding = executable.RootActivity.InputBindings[WorkflowIntrinsicInputKeys.Value];
        Assert.Equal(RuntimeInputBindingSource.VariableRead, binding.Source);
        Assert.Equal(new ValueTypeDescriptor(alias), binding.TargetType);
        Assert.Equal(("source", "workflow"), (binding.Variable!.VariableKey, binding.Variable.DeclaringScopeId));
        Assert.Null(binding.ConversionRequest);
        Assert.Equal(ValueConversionOperation.Identity, binding.ConversionPlan!.Operation);
        var payload = alias == "Int32" ? JsonSerializer.SerializeToElement(7) : JsonSerializer.SerializeToElement(new { approved = true, count = 7 });

        var output = await ExecutePublishedVariableSetOutputAsync(executable, alias, payload);

        Assert.Equal(alias, output.Type.Id);
        Assert.Equal(payload.GetRawText(), output.InlineValue!.Value.GetRawText());
    }

    [Theory]
    [MemberData(nameof(SetOutputWholeVariableRefusalCases))]
    public async Task A_dynamic_SetOutput_cannot_drop_meaningful_Variable_conversion_intent(string alias, string intent)
    {
        var version = SetOutputVariableVersion(alias, WholeVariableConversionTests.Request(intent));
        var authored = JsonSerializer.Serialize(version.State);
        var before = await CountPublishWritesAsync();

        var exception = await Assert.ThrowsAsync<WorkflowExecutableCompilationException>(() =>
            Handler(version).Handle(new PublishWorkflow(version.Id), CancellationToken.None));

        var refusal = Assert.IsType<ValueConversionPublicationException>(exception.InnerException);
        Assert.Equal(ValueConversionRejectionReason.VariableSourceContractUnknown, refusal.ReasonCode);
        Assert.Equal(new ValueConversionBindingContext("set-output", WorkflowIntrinsicInputKeys.Value, ValueConversionBindingKind.Input), refusal.Binding);
        Assert.Null(refusal.SourceType);
        Assert.Null(refusal.SourceRepresentation);
        Assert.Equal(before, await CountPublishWritesAsync());
        Assert.Null(await _activationAuthority.FindAsync("definition-1", "default"));
        Assert.Equal(authored, JsonSerializer.Serialize(version.State));
    }

    [Theory]
    [InlineData("omitted")]
    [InlineData("Auto")]
    public async Task A_dynamic_SetOutput_Variable_preserves_the_open_Object_destination(string request)
    {
        var version = SetOutputVariableVersion("Object", WholeVariableConversionTests.Request(request));

        var publication = await Handler(version).Handle(new PublishWorkflow(version.Id), CancellationToken.None);

        var executable = Assert.IsType<WorkflowExecutable>(await _store.FindAsync(publication.ArtifactId));
        var binding = executable.RootActivity.InputBindings[WorkflowIntrinsicInputKeys.Value];
        Assert.Equal(RuntimeInputBindingSource.VariableRead, binding.Source);
        Assert.Equal("Elsa.Any", binding.TargetType.Alias);
        Assert.Null(binding.ConversionPlan);
    }

    private static WorkflowDefinitionVersion SetOutputVariableVersion(string outputAlias, AuthoredValueConversionRequest? conversion)
    {
        var node = new ActivityNode("set-output", "elsa.intrinsic.set-output@1",
        [
            new WorkflowArgumentState(WorkflowIntrinsicInputKeys.Name, new ArgumentValue(JsonSerializer.SerializeToElement("Count"), "Literal"), null, null, null, null),
            new WorkflowArgumentState(WorkflowIntrinsicInputKeys.Value, new ArgumentValue(new VariableReference("source", "workflow"), "Variable"), null, null, null, null, conversion)
        ], [])
        {
            Intrinsic = new AuthoredWorkflowIntrinsic(AuthoredWorkflowIntrinsicKind.SetOutput, new TypeReference("Elsa.Any"), null)
        };
        var version = WorkflowVersion(node);
        version.State = version.State! with
        {
            Variables = [new VariableDefinition("source", "Source", new TypeReference(outputAlias == "Object" ? "System.Text.Json.JsonElement" : outputAlias), null, null)],
            Outputs = [CountOutput with { Type = new TypeReference(outputAlias) }]
        };
        return version;
    }

    private static async Task<DurableValueState> ExecutePublishedVariableSetOutputAsync(WorkflowExecutable executable, string alias, JsonElement payload)
    {
        const string workflowId = "published-variable-output";
        const string invocationId = "set-output-invocation";
        var now = DateTimeOffset.UtcNow;
        var workflows = new InMemoryWorkflowExecutionStateStore();
        var activities = new InMemoryActivityExecutionStateStore();
        await workflows.SaveAsync(new WorkflowExecutionState(workflowId, executable.Identity, WorkflowExecutionStatus.Running,
            null, now, now, now, null, null, null, null, new Dictionary<string, string>())
        {
            RootVariableFrame = new VariableFrameFactory().CreateRoot(workflowId, "workflow",
                new Dictionary<string, ValueEnvelope> { ["source"] = ValueEnvelope.Inline(new ValueTypeDescriptor(alias), payload, ValueProtectionPolicy.InstanceInline) })
        });
        var activity = new ActivityExecutionState(
            new ActivityExecution(invocationId, workflowId, "set-output", "set-output", "elsa.intrinsic.set-output", "1.0.0"),
            ActivityExecutionStatus.Scheduled, null, now, null, null, null, null, null, null, null, [], [], 0, 0,
            new Dictionary<string, string>());
        await activities.SaveAsync(activity);
        var start = new RuntimeStartActivityCommandPayload(executable.Identity, "set-output", invocationId,
            RuntimeStartActivityCommandPayload.ScheduledActivityReason);
        var work = new RuntimeSchedulerWorkItem("start", workflowId, "command", WorkflowExecutionCommandKind.StartActivity,
            "envelope", "published-variable-output-start", now, now, 1, JsonSerializer.SerializeToElement(start),
            new Dictionary<string, string>(), new Dictionary<string, string>());
        var executor = new WorkflowIntrinsicExecutor(workflows, activities, new RuntimeInputBindingResolver(),
            new InMemoryDurableValueStateStore(), new RuntimeActivityExecutionInspectionAccumulator(new InMemoryActivityExecutionInspectionStore()),
            TimeProvider.System, inputValidator: new WorkflowExecutableInputValidator(TestWellKnownTypeRegistry.Create()));

        var commit = await executor.ExecuteAsync(work, start, executable, executable.RootActivity, activity);

        Assert.Equal(ActivityExecutionStatus.Completed, Assert.Single(commit.StateChanges.ActivityExecutions).State.Status);
        return Assert.Single(commit.StateChanges.DurableValues,
            change => change.State.Metadata.GetValueOrDefault(RuntimeMetadataKeys.OutputName) == "Count").State;
    }

    private static ExpressionDefinition SetOutputExpression(TypeReference resultType, string source = "7") =>
        new("JavaScript", source, resultType, new Dictionary<string, ExpressionParameterBinding>(),
            JsonSerializer.SerializeToElement(new { }), ExpressionCapabilityProfiles.BindingPureV1);

    private async Task<RuntimeInputBinding> PublishSetOutputValueAsync(string outputName, TypeReference authoredType,
        object? value = null, ArgumentValue? argument = null, TypeReference? declaredOutputType = null)
    {
        var setOutput = new ActivityNode("set-output", "elsa.intrinsic.set-output@1",
        [
            new WorkflowArgumentState(WorkflowIntrinsicInputKeys.Name, new ArgumentValue(JsonSerializer.SerializeToElement(outputName), "Literal"), null, null, null, null),
            new WorkflowArgumentState(WorkflowIntrinsicInputKeys.Value, argument ?? new ArgumentValue(JsonSerializer.SerializeToElement(value ?? 7), "Literal"), null, null, null, null)
        ], [])
        {
            Intrinsic = new AuthoredWorkflowIntrinsic(AuthoredWorkflowIntrinsicKind.SetOutput, authoredType, null)
        };
        var version = new WorkflowDefinitionVersion("definition-1", "1.0.0")
        {
            Id = "version-1",
            Definition = new WorkflowDefinition { Id = "definition-1", Name = "Demo" },
            State = new WorkflowDefinitionState([], SequenceNode("sequence", [setOutput]), [],
                [declaredOutputType is null ? CountOutput : CountOutput with { Type = declaredOutputType }], null)
        };

        var publication = await Handler(version, _writeLineActivity, _sequenceActivity).Handle(new PublishWorkflow(version.Id), CancellationToken.None);

        var executable = Assert.IsType<WorkflowExecutable>(await _store.FindAsync(publication.ArtifactId));
        return Assert.Single(executable.RootActivity.DescendantsAndSelf(), node => node.IntrinsicKind == WorkflowIntrinsicKind.SetOutput)
            .InputBindings[WorkflowIntrinsicInputKeys.Value];
    }
}
