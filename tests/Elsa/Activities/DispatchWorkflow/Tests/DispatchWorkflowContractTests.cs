using System.Reflection;
using Elsa.Activities.DispatchWorkflow.Design;
using Elsa.Activities.DispatchWorkflow.Runtime;
using Elsa.Activities.DispatchWorkflow.Runtime.Configuration;
using Elsa.Activities.DispatchWorkflow.Runtime.Constants;
using Elsa.Activities.DispatchWorkflow.Runtime.Models;
using Elsa.Activities.Runtime.Core;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Design.Reconciliation.Clr.Services;
using Elsa.Activities.Design.Reconciliation.Core.Models;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Activity = Elsa.Activities.DispatchWorkflow.Runtime.Activities.DispatchWorkflow;

namespace Elsa.Activities.DispatchWorkflow.Tests;

public sealed class DispatchWorkflowContractTests
{
    [Fact]
    public void Feature_types_remain_inheritable_with_virtual_configuration()
    {
        AssertFeatureCanBeSpecialized(typeof(DispatchWorkflowRuntimeFeature));
        AssertFeatureCanBeSpecialized(typeof(DispatchWorkflowDesignFeature));
    }

    [Fact]
    public void Runtime_feature_exposes_default_and_custom_positive_maximum_nesting_depth()
    {
        Assert.Equal(32, new DispatchWorkflowOptions().MaxNestingDepth);
        var services = new ServiceCollection();

        new DispatchWorkflowRuntimeFeature { MaxNestingDepth = 7 }.ConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        Assert.Equal(7, provider.GetRequiredService<IOptions<DispatchWorkflowOptions>>().Value.MaxNestingDepth);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Runtime_feature_rejects_nonpositive_maximum_nesting_depth(int maximum)
    {
        var feature = new DispatchWorkflowRuntimeFeature { MaxNestingDepth = maximum };

        Assert.Throws<ArgumentOutOfRangeException>(() => feature.ConfigureServices(new ServiceCollection()));
    }

    [Fact]
    public void Activity_exposes_stable_schema_defaults_editor_and_outcomes()
    {
        var activity = new Activity();

        Assert.Equal(DispatchWorkflowConstants.ActivityType, activity.Type);

        var inputs = typeof(Activity).GetProperties()
            .Where(property => DerivesFrom(property.PropertyType, typeof(InputArgument)))
            .ToDictionary(property => property.Name, StringComparer.Ordinal);
        var outputs = typeof(Activity).GetProperties()
            .Where(property => DerivesFrom(property.PropertyType, typeof(OutputArgument)))
            .ToDictionary(property => property.Name, StringComparer.Ordinal);

        Assert.Equal(
            new[] { nameof(Activity.WorkflowDefinitionId), nameof(Activity.Inputs), nameof(Activity.WaitForCompletion), nameof(Activity.CancelChildOnParentCancellation), nameof(Activity.CorrelationId) },
            inputs.Keys.OrderBy(InputOrder).ToArray());
        Assert.Equal(new[] { nameof(Activity.ChildWorkflowExecutionId), nameof(Activity.Result) }, outputs.Keys.Order(StringComparer.Ordinal).ToArray());

        var workflowId = inputs[nameof(Activity.WorkflowDefinitionId)].GetCustomAttribute<ActivityInputAttribute>();
        Assert.NotNull(workflowId);
        Assert.Equal(ActivityInputUIHints.Dropdown, workflowId!.UIHint);
        Assert.Equal(DispatchWorkflowConstants.WorkflowDefinitionOptionsKey, workflowId.OptionsProvider);

        Assert.Equal("false", inputs[nameof(Activity.WaitForCompletion)].GetCustomAttribute<ActivityInputAttribute>()?.DefaultValue);
        Assert.Equal("true", inputs[nameof(Activity.CancelChildOnParentCancellation)].GetCustomAttribute<ActivityInputAttribute>()?.DefaultValue);
        Assert.Equal(typeof(OutputArgument<DispatchWorkflowResult>), outputs[nameof(Activity.Result)].PropertyType);

        Assert.Equal(
            new[] { "Dispatched", "Completed", "Faulted", "Cancelled", "DispatchFailed" },
            DispatchWorkflowOutcomes.All);
    }

    [Fact]
    public void Clr_catalog_discovers_one_dispatch_workflow_contract()
    {
        var folder = Directory.CreateTempSubdirectory("dispatch-workflow-catalog-");
        try
        {
            File.Copy(typeof(Activity).Assembly.Location, Path.Combine(folder.FullName, Path.GetFileName(typeof(Activity).Assembly.Location)));
            var scanner = new ClrAssemblyScanner(
                new ActivityTypeVersionResolver(),
                new ActivityTypeCategoryResolver(),
                NullLogger<ClrAssemblyScanner>.Instance);

            var model = Assert.Single(scanner.Scan(folder.FullName), candidate => candidate.ActivityTypeKey == typeof(Activity).FullName);

            Assert.Equal(5, model.Inputs.Count());
            Assert.Equal(2, model.Outputs.Count());
            var workflowId = model.Inputs.Single(input => input.Name == nameof(Activity.WorkflowDefinitionId));
            Assert.Equal(ActivityInputUIHints.Dropdown, workflowId.UiHint);
            Assert.Equal(
                DispatchWorkflowConstants.WorkflowDefinitionOptionsKey,
                workflowId.UISpecifications?.GetProperty("optionsProvider").GetProperty("key").GetString());
            Assert.False(model.Inputs.Single(input => input.Name == nameof(Activity.WaitForCompletion)).DefaultValue?.GetBoolean());
            Assert.True(model.Inputs.Single(input => input.Name == nameof(Activity.CancelChildOnParentCancellation)).DefaultValue?.GetBoolean());
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Wait_for_completion_true_is_rejected_in_this_slice()
    {
        var activity = new Activity
        {
            WaitForCompletion = new InputArgument<bool>(new MemoryBlockReference("wait"))
        };
        var context = new StubExecutionContext(activity, new Dictionary<string, object?> { ["wait"] = true });

        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await ((IActivity)activity).ExecuteAsync(context));

        Assert.Contains("#679", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Realized_dynamic_inputs_are_rejected_before_dispatch_without_exposing_raw_values()
    {
        const string rejectedValue = "runtime-secret";
        await using var fixture = await DispatchWorkflowRuntimeTestFixture.CreateAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await fixture.StartParentAsync(
                "invalid-input",
                "parent-invalid-input",
                "correlation-parent",
                dispatchInputs: new Dictionary<string, object?> { ["unknown"] = rejectedValue }));

        Assert.Contains("not declared", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(rejectedValue, exception.Message, StringComparison.Ordinal);
        Assert.Empty(await fixture.ListDispatchesAsync("parent-invalid-input"));
    }

    [Fact]
    public async Task Attempted_depth_33_is_rejected_before_dispatch_staging_or_child_materialization()
    {
        await using var fixture = await DispatchWorkflowRuntimeTestFixture.CreateAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await fixture.StartParentAsync(
                "over-depth",
                "parent-over-depth",
                "correlation-parent",
                dispatchNestingDepth: 32));

        Assert.Contains("nesting depth 33", exception.Message, StringComparison.Ordinal);
        Assert.Contains("maximum of 32", exception.Message, StringComparison.Ordinal);
        Assert.Empty(await fixture.ListDispatchesAsync("parent-over-depth"));
        Assert.Empty(fixture.ChildProbe.Observations);
        Assert.DoesNotContain(
            fixture.Actors.Activations,
            activation => activation.WorkflowExecutionId.Contains(":dispatch:", StringComparison.Ordinal));
    }

    [Fact]
    public void Result_snapshots_json_safe_terminal_output_contract()
    {
        var metadata = new Dictionary<string, string> { ["trace"] = "abc" };
        var outputs = new[]
        {
            new DispatchWorkflowOutput("Total", System.Text.Json.JsonSerializer.SerializeToElement(42), "System.Int32", false)
        };

        var result = new DispatchWorkflowResult("child-1", WorkflowDispatchStatus.Completed, outputs, metadata);
        metadata["trace"] = "mutated";

        Assert.Equal("child-1", result.ChildWorkflowExecutionId);
        Assert.Equal(WorkflowDispatchStatus.Completed, result.Status);
        Assert.Equal("abc", result.DiagnosticMetadata["trace"]);
        Assert.Equal(42, Assert.Single(result.Outputs).Value?.GetInt32());
    }

    private static bool DerivesFrom(Type candidate, Type baseType)
    {
        for (var current = candidate; current is not null; current = current.BaseType)
            if (current == baseType)
                return true;

        return false;
    }

    private static void AssertFeatureCanBeSpecialized(Type featureType)
    {
        Assert.False(featureType.IsSealed);
        var configureServices = Assert.Single(
            featureType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly),
            method => method.Name == "ConfigureServices");
        Assert.True(configureServices.IsVirtual);
        Assert.False(configureServices.IsFinal);
    }

    private static int InputOrder(string name) => name switch
    {
        nameof(Activity.WorkflowDefinitionId) => 0,
        nameof(Activity.Inputs) => 1,
        nameof(Activity.WaitForCompletion) => 2,
        nameof(Activity.CancelChildOnParentCancellation) => 3,
        nameof(Activity.CorrelationId) => 4,
        _ => int.MaxValue
    };

    private sealed class StubExecutionContext(IActivity activity, IReadOnlyDictionary<string, object?> values) : IActivityExecutionContext
    {
        public TService GetRequiredService<TService>() where TService : notnull => throw new InvalidOperationException();
        public IExpressionExecutionContext ExpressionExecutionContext => throw new InvalidOperationException();
        public IActivity Activity { get; } = activity;
        public IActivityExecutionContext ParentActivityExecutionContext => throw new InvalidOperationException();
        public CancellationToken CancellationToken => CancellationToken.None;

        public T? Get<T>(InputArgument<T>? input) =>
            input?.MemoryBlockReference() is { } reference && values.TryGetValue(reference.Id, out var value)
                ? (T?)value
                : default;

        public void Set<T>(OutputArgument<T>? output, T? value, string? outputName = null) { }
        public IAsyncEnumerable<ActivityOutputs> GetActivityOutputs() => Empty();
        public void SetOutcomes(string[] outcomes) { }
        public IEnumerable<string> GetOutcomes() => [];
        public void CreateBookmark(ActivityBookmarkRequest request) { }
        public IReadOnlyCollection<ActivityBookmarkRequest> GetBookmarkRequests() => [];

        private static async IAsyncEnumerable<ActivityOutputs> Empty()
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
