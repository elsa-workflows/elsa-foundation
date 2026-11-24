using Elsa.Activities.Scripting.Activities;
using Elsa.Expressions;
using Elsa.Serialization.Core;
using Elsa.Serialization.Testing;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.JavaScript;
using Elsa.Expressions.JavaScript.Jint;
using Elsa.Primitives.Models;
using Elsa.Serialization.SystemText;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Activities.Behavioral;

public sealed class NullPolicyAdmissionRunTests
{
    [Theory]
    [InlineData("String", true)]
    [InlineData("Elsa.Any", true)]
    [InlineData("Int32", false)]
    [InlineData("Boolean", false)]
    public async Task Omitted_optional_inputs_reach_JavaScript_as_null_or_their_type_default(string alias, bool isNull)
    {
        using var publication = new MemberReadPublicationRunTests.Publication(false, false,
            (typeof(BooleanReader), "boolean-reader", [ReaderInput()]));
        var expression = new ExpressionDefinition("JavaScript", "args.value === null", new TypeReference("Boolean"),
            new Dictionary<string, ExpressionParameterBinding> { ["value"] = new WorkflowRequestExpressionParameterBinding("value") },
            JsonSerializer.SerializeToElement(new { }), ExpressionCapabilityProfiles.BindingReadonlyV1);
        var root = new ActivityNode("reader", "boolean-reader",
            [new("Value", new ArgumentValue(expression, "JavaScript"), null, null, null, null)], []);
        var executable = await publication.Compile(root, inputs:
            [new("value", "value", new(alias), null, "Value", null, IsNullable: false)]);
        await using var harness = Harness();
        var source = await harness.PublishAsync(executable, "s2-optional-input");
        await harness.StartPublishedAsync(source, WorkflowExecutionHarness.WorkflowExecutionId,
            tenantId: WorkflowExecutionHarness.Partition.Value);
        var run = await harness.ReadRunAsync(WorkflowExecutionHarness.WorkflowExecutionId);
        run.AssertWorkflowCompleted();
        Assert.Equal(isNull, run.AssertCompleted("reader").Completion!.Result.InlineValue!.Value.GetBoolean());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Pinned_default_on_null_reaches_the_materializer_and_records_a_step_warning(bool literal)
    {
        using var publication = new MemberReadPublicationRunTests.Publication(false, false,
            (typeof(BooleanReader), "boolean-reader", [ReaderInput()]));
        var root = new ActivityNode("reader", "boolean-reader",
            [new("Value", literal ? new ArgumentValue(JsonSerializer.SerializeToElement<object?>(null), "Literal")
                : new ArgumentValue("null", "JavaScript"), null, null, null, null)], []);
        var executable = await publication.Compile(root, strategies: new()
        {
            NullHandling = new() { DefaultOnNull = true }
        });
        await using var harness = Harness();
        var run = await harness.RunAsync(executable);
        run.AssertWorkflowCompleted();
        var step = run.AssertCompleted("reader");
        Assert.False(step.Completion!.Result.InlineValue!.Value.GetBoolean());
        Assert.Contains(step.InputSnapshot!.Warnings!, warning => warning.Code == "VF-NULL-001");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_missing_member_assigned_to_a_whole_variable_faults_or_writes_explicit_null(bool policy)
    {
        var run = await RunScript("variables.discount = args.order.discount; return 1;", "String", "discount", policy, false);
        var stored = run.WorkflowState!.RootVariableFrame!.Values["discount"];
        if (policy) { run.AssertWorkflowCompleted(); Assert.Equal(ValuePresence.ExplicitNull, stored.Presence); }
        else { Assert.Equal(ActivityExecutionStatus.Faulted, run.State("script").Status); Assert.Contains("VF-UNDEF-001", run.State("script").Fault!.Message); Assert.Equal("keep", stored.InlineValue!.Value.GetString()); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_real_script_null_write_to_Int32_uses_the_pinned_policy_and_invocation_warning(bool policy)
    {
        var run = await RunScript("variables.count = null; return 1;", "Int32", "count", false, policy);
        var stored = run.WorkflowState!.RootVariableFrame!.Values["count"];
        if (policy)
        {
            run.AssertWorkflowCompleted();
            Assert.Equal(0, stored.InlineValue!.Value.GetInt32());
            Assert.Contains(run.AssertCompleted("script").Warnings!, warning => warning.Code == "VF-NULL-001");
        }
        else { Assert.Equal(ActivityExecutionStatus.Faulted, run.State("script").Status); Assert.Contains("VF-ACT-004", run.State("script").Fault!.Message); Assert.Equal(3, stored.InlineValue!.Value.GetInt32()); }
    }

    private static async Task<WorkflowExecutionRun> RunScript(string body, string alias, string name, bool undefinedAsNull, bool defaultOnNull)
    {
        using var publication = new MemberReadPublicationRunTests.Publication(false, false,
            (typeof(RunJavaScript), "script-version", [
                new("script", "script", new("String"), null, "Script", null, false, IsRequired: true),
                new("arguments", "arguments", new("Any"), null, "Arguments", null, true)]));
        var root = new ActivityNode("script", "script-version", [
            new("script", new ArgumentValue(body, "Literal"), null, null, null, null),
            new("arguments", new ArgumentValue(JsonSerializer.SerializeToElement(new { order = new { } }), "Literal"), null, null, null, null)], []);
        var declaration = new VariableDefinition(name, name, new(alias), null,
            new ArgumentValue(alias == "String" ? JsonSerializer.SerializeToElement("keep") : JsonSerializer.SerializeToElement(3), "Literal"));
        var executable = await publication.Compile(root, variables: [declaration], strategies: new()
            { NullHandling = new() { UndefinedAsNull = undefinedAsNull, DefaultOnNull = defaultOnNull } });
        await using var harness = Harness();
        return await harness.RunAsync(executable, allowPendingWorkOnTerminalCompletion: true, allowAcceptedButFaulted: true);
    }

    private static InputDefinition ReaderInput() => new("Value", "Value", new("Boolean"), null, "Value", null, false);
    private static WorkflowExecutionHarness Harness() => WorkflowExecutionHarness.Create()
        .WithFeature(services => new SerializationFeature().ConfigureServices(services))
        .WithFeature(services => new ExpressionsFeature().ConfigureServices(services))
        .WithFeature(services => new JavaScriptFeature().ConfigureServices(services))
        .WithFeature(services => new JintFeature().ConfigureServices(services))
        .ConfigureServices(services => services.AddSingleton<IWellKnownTypeRegistry>(Registry()))
        .Build(Enumerable.Range(1, 16).Select(index => $"s2-act-{index}"));

    private static IWellKnownTypeRegistry Registry()
    {
        var registry = SerializationServices.TypeRegistry();
        registry.RegisterType(typeof(int), "Int32");
        registry.RegisterType(typeof(bool), "Boolean");
        registry.RegisterType(typeof(string), "String");
        registry.RegisterType(typeof(JsonElement), "Any");
        registry.RegisterType(typeof(BooleanReader), typeof(BooleanReader).FullName!);
        registry.RegisterType(typeof(RunJavaScript), typeof(RunJavaScript).FullName!);
        return registry;
    }

    public sealed class BooleanReader : Activity<bool>
    {
        [ActivityInput] public bool Value { get; set; }
        protected override ValueTask<ActivityTransition<bool>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition.Complete(Value));
    }
}
