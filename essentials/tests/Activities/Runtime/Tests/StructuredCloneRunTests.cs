using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Scripting;
using Elsa.Activities.Scripting.Activities;
using Elsa.Activities.Testing;
using Elsa.Events;
using Elsa.Expressions;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.JavaScript;
using Elsa.Expressions.JavaScript.Core.Exceptions;
using Elsa.Expressions.JavaScript.Jint;
using Elsa.Primitives.Models;
using Elsa.Serialization.SystemText;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// Issue #6215 end to end, through the real engine, the real JavaScript evaluators and Run JavaScript: a script or an
/// input expression changes the nested array and the nested object of a <c>structuredClone</c> copy, and the stored
/// variable stays unchanged, with neither <c>VF-WRITE-001</c> nor <c>VF-BIND-001</c>.
/// </summary>
public sealed class StructuredCloneRunTests
{
    private const string Order = """{"id":"A-1","customer":{"name":"Ada"},"items":[1,2]}""";
    private const string ChangedCopy = """{"id":"A-1","customer":{"name":"Bob"},"items":[1,2,3]}""";
    private const string ChangeTheCopy = "const copy = structuredClone(variables.order); copy.items.push(3); copy.customer.name = 'Bob';";

    [Fact]
    public async Task A_script_that_changes_a_copy_leaves_the_stored_variable_unchanged_without_a_warning()
    {
        await using var harness = NewHarness();
        var run = await RunAsync(harness, $"{ChangeTheCopy} return copy;", ArgumentsLiteral());

        run.AssertWorkflowCompleted();
        var state = run.AssertCompleted("node-js");
        Assert.Equal(ChangedCopy, state.Completion!.Result.InlineValue!.Value.GetProperty("value").GetRawText());
        AssertOrderUnchanged(run);
        // No VF-WRITE-001, and no other warning: the copy shares nothing with the variable.
        Assert.Null(state.Warnings);
    }

    [Fact]
    public async Task A_spread_copy_still_shares_its_nested_values_with_the_variable()
    {
        // Why the helper exists: a shallow copy's nested array is the variable's, so the change is written and warned.
        await using var harness = NewHarness();
        var run = await RunAsync(harness, "const copy = { ...variables.order }; copy.items.push(3); return 1;", ArgumentsLiteral());

        run.AssertWorkflowCompleted();
        var warning = Assert.Single(run.AssertCompleted("node-js").Warnings!);
        Assert.Equal(JavaScriptValueDiagnostics.ChangeWithoutAssignment, warning.Code);
        Assert.Contains("structuredClone(variables.order)", warning.Message, StringComparison.Ordinal);
        var frame = Assert.IsType<VariableFrameState>(run.WorkflowState?.RootVariableFrame);
        Assert.Equal("""{"id":"A-1","customer":{"name":"Ada"},"items":[1,2,3]}""", frame.Values["var-order"].InlineValue!.Value.GetRawText());
    }

    [Fact]
    public async Task An_input_expression_that_changes_a_copy_succeeds_and_leaves_the_stored_variable_unchanged()
    {
        await using var harness = NewHarness();
        var run = await RunAsync(harness, "return args.order;", ArgumentsExpression($"(() => {{ {ChangeTheCopy} return {{ order: copy }}; }})()"));

        // No VF-BIND-001: the node ran, and no incident was raised.
        run.AssertWorkflowCompleted();
        var state = run.AssertCompleted("node-js");
        Assert.Equal(ChangedCopy, state.Completion!.Result.InlineValue!.Value.GetProperty("value").GetRawText());
        Assert.Null(state.InputSnapshot!.Warnings);
        Assert.Null(state.Warnings);
        AssertOrderUnchanged(run);
        Assert.Empty(await harness.Services.GetRequiredService<IIncidentStateStore>().ListAsync(WorkflowExecutionHarness.WorkflowExecutionId));
    }

    private static void AssertOrderUnchanged(WorkflowExecutionRun run)
    {
        var frame = Assert.IsType<VariableFrameState>(run.WorkflowState?.RootVariableFrame);
        Assert.Equal(Order, frame.Values["var-order"].InlineValue!.Value.GetRawText());
        Assert.Equal(VariableVersion.Initial.Version, frame.VersionOf("var-order").Version);
    }

    private static WorkflowExecutionHarness NewHarness() => WorkflowExecutionHarness.Create()
            .WithFeature(services => new ActivitiesScriptingFeature().ConfigureServices(services))
            .ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddMemoryCache();
                services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
                new EventsFeature().ConfigureServices(services);
                new SerializationFeature().ConfigureServices(services);
                new ExpressionsFeature().ConfigureServices(services);
                new JavaScriptFeature().ConfigureServices(services);
                new JintFeature().ConfigureServices(services);
            })
            .Build("actexec-js-clone");

    private static async Task<WorkflowExecutionRun> RunAsync(WorkflowExecutionHarness harness, string script, RuntimeInputBinding arguments)
    {
        var node = new ExecutableNode(
            executableNodeId: "node-js",
            authoredActivityId: "authored-js",
            activityType: typeof(RunJavaScript).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: "test",
            descriptorPayload: JsonSerializer.SerializeToElement(new { }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                ["script"] = Literal("script", "String", JsonSerializer.SerializeToElement(script)),
                ["arguments"] = arguments
            },
            metadata: new Dictionary<string, string>());

        return await harness.RunAsync(
            WorkflowExecutionHarness.NewExecutable(node, Declare("var-order", "order", Order)),
            allowPendingWorkOnTerminalCompletion: true,
            allowAcceptedButFaulted: true);
    }

    private static RuntimeVariableDeclaration Declare(string key, string name, string json)
    {
        var type = new ValueTypeDescriptor("Any");
        var policy = ValueProtectionPolicy.InstanceInline;
        return new RuntimeVariableDeclaration(key, name, type, policy,
            new RuntimeInputBinding(key, type, policy, RuntimeInputBindingSource.Literal,
                literal: ValueEnvelope.Inline(type, JsonSerializer.Deserialize<JsonElement>(json), policy)));
    }

    private static RuntimeInputBinding ArgumentsLiteral() => Literal("arguments", "JsonElement", JsonSerializer.SerializeToElement(new { }));

    private static RuntimeInputBinding Literal(string key, string alias, JsonElement value)
    {
        var type = new ValueTypeDescriptor(alias);
        var policy = ValueProtectionPolicy.InstanceInline;
        return new RuntimeInputBinding(key, type, policy, RuntimeInputBindingSource.Literal, literal: ValueEnvelope.Inline(type, value, policy));
    }

    private static RuntimeInputBinding ArgumentsExpression(string source)
    {
        var type = new ValueTypeDescriptor("JsonElement");
        var policy = ValueProtectionPolicy.InstanceInline;
        return new RuntimeInputBinding(
            "arguments",
            type,
            policy,
            RuntimeInputBindingSource.Expression,
            expression: new RuntimeExpressionBinding(
                "JavaScript",
                source,
                new RuntimeValueTypeDescriptor("alias", "JsonElement", null),
                parameters: new Dictionary<string, ExpressionParameterBinding>
                {
                    [ExpressionParameterSurfaces.VariablesKey("order")] = new VariableExpressionParameterBinding(VariableReference.WorkflowScopeId, "var-order", ExpressionParameterSurfaces.Variables)
                },
                options: JsonSerializer.SerializeToElement(new { }),
                capabilityProfile: ExpressionCapabilityProfiles.BindingReadonlyV1));
    }
}
