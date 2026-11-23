using System.Text.Json;
using Elsa.Activities.ControlFlow;
using Elsa.Activities.Testing;
using Elsa.Events;
using Elsa.Expressions;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.JavaScript;
using Elsa.Expressions.JavaScript.Jint;
using Elsa.Primitives.Models;
using Elsa.Serialization.SystemText;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests.VariableLifecycle;

/// <summary>
/// Spec 1221 FR-002: a variable read through an expression enters the input read set, through a declared variable
/// parameter, through a pinned <c>variables.count</c> read and through a computed read the run-time lookup serves (spec
/// 1222 FR-010, T022), not only through a variable-read
/// binding. Both leaves read <c>count</c> before either commits (fusion off), so the second capture is a lost-update
/// warning only when its read was recorded; an unrecorded read would make it a silent blind write.
/// </summary>
public sealed class ExpressionReadSetTests
{
    private static readonly string[] Ids = ["actexec-parallel", "actexec-left", "actexec-right"];

    public static TheoryData<string> ReadPaths() => new() { "parameter", "variables", "computed" };

    [Theory]
    [MemberData(nameof(ReadPaths))]
    public async Task A_variable_read_through_an_expression_is_a_base_for_the_capture(string readPath)
    {
        await using var harness = NewHarness();
        var executable = WorkflowExecutionHarness.NewExecutable(
            VariableLifecycleTestGraph.Parallel(Leaf("node-left", readPath), Leaf("node-right", readPath)),
            VariableLifecycleTestGraph.CountDeclaration());

        var run = await harness.RunAsync(executable);

        var right = run.State("node-right");
        // Spec 1222 FR-032, T039: an input expression reads the snapshot, never the left leaf's write, which committed first.
        Assert.Equal(0, right.InputSnapshot!.Values[nameof(IncrementActivity.Count)].InlineValue!.Value.GetInt32());
        var entry = Assert.Single(right.InputSnapshot!.InputReadSet!.Entries);
        if (readPath == "computed")
        {
            // The computed read is part of the input snapshot too, with the version it read (FR-010).
            var computed = Assert.Single(right.InputSnapshot.ComputedVariableReads!);
            Assert.Equal(("count", VariableLifecycleTestGraph.CountKey, 0L, true), (computed.Name, computed.VariableKey, computed.Version, computed.Present));
        }
        Assert.Equal(VariableLifecycleTestGraph.CountKey, entry.VariableKey);
        Assert.Equal(0, entry.Version);
        var record = Assert.Single(right.VariableWrites!);
        Assert.Equal(VariableCommitOutcome.LostUpdateWarning, record.Outcome);
        Assert.Equal("node-left", record.OverwrittenWriter!.ExecutableNodeId);
    }

    private static ExecutableNode Leaf(string nodeId, string readPath)
    {
        var node = VariableLifecycleTestGraph.Increment(nodeId, typeof(IncrementActivity));
        var type = VariableLifecycleTestGraph.Int32Type;
        var expression = readPath == "parameter"
            ? new RuntimeExpressionBinding(
                "JavaScript",
                "args.current",
                new RuntimeValueTypeDescriptor("alias", type.Alias, null),
                parameters: new Dictionary<string, ExpressionParameterBinding>
                {
                    ["current"] = new VariableExpressionParameterBinding(VariableReference.WorkflowScopeId, VariableLifecycleTestGraph.CountKey)
                },
                options: JsonSerializer.SerializeToElement(new { }),
                capabilityProfile: ExpressionCapabilityProfiles.BindingReadonlyV1)
            : readPath == "variables"
                ? new RuntimeExpressionBinding(
                    "JavaScript",
                    "variables.count",
                    new RuntimeValueTypeDescriptor("alias", type.Alias, null),
                    parameters: new Dictionary<string, ExpressionParameterBinding>
                    {
                        ["variables.count"] = new VariableExpressionParameterBinding(VariableReference.WorkflowScopeId, VariableLifecycleTestGraph.CountKey, ExpressionParameterSurfaces.Variables)
                    },
                    options: JsonSerializer.SerializeToElement(new { }),
                    capabilityProfile: ExpressionCapabilityProfiles.BindingReadonlyV1)
                : new RuntimeExpressionBinding(
                    "JavaScript",
                    "variables[['co', 'unt'].join('')]",
                    new RuntimeValueTypeDescriptor("alias", type.Alias, null),
                    parameters: new Dictionary<string, ExpressionParameterBinding>(),
                    options: JsonSerializer.SerializeToElement(new { }),
                    capabilityProfile: ExpressionCapabilityProfiles.BindingReadonlyV1,
                    variableReadFallback: new VariableReadFallback(
                        [new VariableReadCandidate("count", VariableReference.WorkflowScopeId, VariableLifecycleTestGraph.CountKey)]));
        var binding = new RuntimeInputBinding(
            nameof(IncrementActivity.Count),
            type,
            ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.Expression,
            expression: expression);

        return new ExecutableNode(
            node.ExecutableNodeId, node.AuthoredActivityId, node.ActivityType, node.ActivityTypeVersion, node.DescriptorType,
            node.DescriptorPayload, new Dictionary<string, RuntimeInputBinding> { [binding.InputKey] = binding }, node.Metadata,
            activityContract: node.ActivityContract, outputCaptures: node.OutputCaptures);
    }

    private static WorkflowExecutionHarness NewHarness()
    {
        var harness = WorkflowExecutionHarness.Create()
            .WithFeature(services => new ActivitiesControlFlowFeature().ConfigureServices(services))
            .ConfigureServices(services =>
            {
                services.AddSingleton(new RuntimeReplaySafeFusionOptions { Enabled = false });
                services.AddLogging();
                services.AddMemoryCache();
                services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
                new EventsFeature().ConfigureServices(services);
                new SerializationFeature().ConfigureServices(services);
                new ExpressionsFeature().ConfigureServices(services);
                new JavaScriptFeature().ConfigureServices(services);
                new JintFeature().ConfigureServices(services);
            })
            .Build(Ids);

        using var scope = harness.Services.CreateScope();
        foreach (var task in scope.ServiceProvider.GetServices<IStartupTask>())
            task.ExecuteAsync(CancellationToken.None).GetAwaiter().GetResult();
        return harness;
    }
}
