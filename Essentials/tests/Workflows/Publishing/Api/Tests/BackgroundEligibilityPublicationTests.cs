using System.Text.Json;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;
using ArgumentValue = Elsa.Expressions.Core.Models.ArgumentValue;
using VariableDefinition = Elsa.Expressions.Core.Models.VariableDefinition;
using WorkflowArgumentState = Elsa.Workflows.Design.Core.Models.ArgumentState;

namespace Elsa.Workflows.Publishing.Api.Tests;

/// <summary>
/// Spec 1230: publication refuses a background node through the one eligibility component, naming the criterion.
/// C1, C3, C4 and C5 are covered with the other background placement tests in <see cref="WorkflowExecutableCompilerTests"/>.
/// </summary>
public sealed class BackgroundEligibilityPublicationTests
{
    [Fact]
    public async Task C2_refuses_a_stateful_activity_in_the_background()
    {
        var refusal = await AssertRefusedAsync(Background(typeof(BackgroundSafeStatefulActivity)), typeof(BackgroundSafeStatefulActivity));

        Assert.Contains("VF-BG-001 (C2):", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("stateful activities are not supported", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task C9_refuses_a_variable_reader_in_the_background_and_admits_it_inline()
    {
        var refusal = await AssertRefusedAsync(Background(typeof(BackgroundSafeVariableReader)), typeof(BackgroundSafeVariableReader));

        Assert.Contains(
            $"VF-BG-001 (C9): Background execution is not eligible for activity node '{SecretBindingCompilerFixture.NodeId}': the activity reads variables by name; " +
            "variables reach a background step only through its inputs and output captures.",
            refusal.Message, StringComparison.Ordinal);

        var inline = await SecretBindingCompilerFixture.CompileAsync(
            SecretBindingCompilerFixture.Node(typeof(BackgroundSafeVariableReader)), [typeof(BackgroundSafeVariableReader)]);
        Assert.Equal(ActivityExecutionPlacement.Inline, inline.RootActivity.ExecutionPlacement);
    }

    [Fact]
    public async Task C10_refuses_a_sensitive_input_that_is_not_a_secret_reference()
    {
        var sensitive = new WorkflowArgumentState("token", new ArgumentValue(JsonSerializer.SerializeToElement("p@ss"), "Literal"), null, null, null, IsSensitive: true);

        var refusal = await AssertRefusedAsync(Background(typeof(BackgroundSafeTokenActivity), sensitive), typeof(BackgroundSafeTokenActivity));

        Assert.Contains("VF-BG-001 (C10):", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("input 'token' is sensitive or encryption-required and would lose its protection", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task C10_refuses_a_stream_input_bound_to_a_variable()
    {
        var input = new WorkflowArgumentState("content", new ArgumentValue(JsonSerializer.SerializeToElement(new { referenceKey = "content" }), "Variable"), null, null, null, null);
        var variables = new[] { new VariableDefinition("content", "Content", new TypeReference("Stream"), null, null) };

        var refusal = await Assert.ThrowsAnyAsync<Exception>(() => SecretBindingCompilerFixture.CompileAsync(
            Background(typeof(BackgroundSafeStreamInputActivity), input), [typeof(BackgroundSafeStreamInputActivity)], variables: variables));

        Assert.Contains("VF-BG-001 (C10):", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("input 'content'", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task C10_admits_an_optional_input_the_author_left_unbound()
    {
        // The compiler pins an unbound optional input as an absent literal; nothing travels to the worker.
        var executable = await SecretBindingCompilerFixture.CompileAsync(
            Background(typeof(BackgroundSafeStreamInputActivity)), [typeof(BackgroundSafeStreamInputActivity)]);

        Assert.Equal(ActivityExecutionPlacement.Background, executable.RootActivity.ExecutionPlacement);
        Assert.Equal(ValuePresence.Absent, executable.RootActivity.InputBindings["content"].Literal!.Presence);
    }

    [Fact]
    public async Task C10_admits_a_secret_reference_and_an_Any_input()
    {
        var secret = await SecretBindingCompilerFixture.CompileAsync(
            Background(typeof(BackgroundSafeTokenActivity), SecretBindingCompilerFixture.Secret("token")), [typeof(BackgroundSafeTokenActivity)]);
        Assert.Equal(ActivityExecutionPlacement.Background, secret.RootActivity.ExecutionPlacement);
        Assert.Equal(RuntimeInputBindingSource.SecretRead, secret.RootActivity.InputBindings["token"].Source);

        var json = new WorkflowArgumentState("document", new ArgumentValue(JsonSerializer.SerializeToElement(new { a = 1 }), "Literal"), null, null, null, null);
        var any = await SecretBindingCompilerFixture.CompileAsync(
            Background(typeof(BackgroundSafeAnyInputActivity), json), [typeof(BackgroundSafeAnyInputActivity)]);
        Assert.Equal(ActivityExecutionPlacement.Background, any.RootActivity.ExecutionPlacement);
    }

    private static ActivityNode Background(Type activityType, params WorkflowArgumentState[] inputs) =>
        SecretBindingCompilerFixture.Node(activityType, inputs) with { ExecutionPlacement = ActivityExecutionPlacement.Background };

    private static Task<Elsa.Workflows.Publishing.Core.Models.WorkflowExecutableCompilationException> AssertRefusedAsync(ActivityNode root, Type activityType) =>
        SecretBindingCompilerFixture.AssertRefusedAsync(root, [activityType]);

    [BackgroundExecutionSafe]
    public sealed class BackgroundSafeStatefulActivity : Activity<int>, IStatefulActivity
    {
        protected override ValueTask<ActivityTransition<int>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition.Complete(1));

        public ValueTask<ActivityTransition> ResumeAsync(ActivityResumeRequest request) => throw new NotSupportedException();
    }

    [BackgroundExecutionSafe]
    public sealed class BackgroundSafeVariableReader : Activity<int>, IRuntimeScopedVariableReader
    {
        protected override ValueTask<ActivityTransition<int>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition.Complete(1));
    }

    [BackgroundExecutionSafe]
    public sealed class BackgroundSafeTokenActivity : Activity<int>
    {
        [ActivityInput(Key = "token")]
        public string? Token { get; set; }

        protected override ValueTask<ActivityTransition<int>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition.Complete(1));
    }

    [BackgroundExecutionSafe]
    public sealed class BackgroundSafeStreamInputActivity : Activity<int>
    {
        [ActivityInput(Key = "content")]
        public Stream? Content { get; set; }

        protected override ValueTask<ActivityTransition<int>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition.Complete(1));
    }

    [BackgroundExecutionSafe]
    public sealed class BackgroundSafeAnyInputActivity : Activity<int>
    {
        [ActivityInput(Key = "document")]
        public JsonElement? Document { get; set; }

        protected override ValueTask<ActivityTransition<int>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition.Complete(1));
    }
}
