using System.Reflection;
using System.Text.Json;
using Elsa.Activities.Bpmn.Activities;
using Elsa.Activities.Http.Activities;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Runtime.Core.Attributes;
using TimerActivity = Elsa.Activities.Scheduling.Activities.Timer;
using Elsa.Activities.Scripting.Activities;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;
using ArgumentValue = Elsa.Expressions.Core.Models.ArgumentValue;
using SequenceActivity = Elsa.Activities.Sequence.Activities.Sequence;
using WorkflowArgumentState = Elsa.Workflows.Design.Core.Models.ArgumentState;

namespace Elsa.Workflows.Publishing.Api.Tests;

/// <summary>
/// Spec 1230 US5: the Essentials built-ins the assessment marks publish in the background; the ones it does not mark are
/// refused naming their first failing criterion, or the missing declaration when only C7/C8 stand in the way.
/// The connector reads are covered in their own test projects.
/// </summary>
public sealed class BuiltInBackgroundAssessmentTests
{
    private static readonly Type[] Marked =
    [
        typeof(Break), typeof(Fault), typeof(WriteLine), typeof(WriteLines), typeof(RunJavaScript), typeof(BpmnDecision)
    ];

    public static TheoryData<Type> MarkedActivities => new(Marked);

    [Theory]
    [MemberData(nameof(MarkedActivities))]
    public async Task A_marked_built_in_publishes_in_the_background(Type activityType)
    {
        var executable = await SecretBindingCompilerFixture.CompileAsync(Background(activityType, RequiredInputs(activityType)), [activityType]);

        Assert.Equal(ActivityExecutionPlacement.Background, executable.RootActivity.ExecutionPlacement);
        Assert.True(executable.RootActivity.ActivityContract!.SupportsBackgroundExecution);
    }

    [Fact]
    public async Task RunJavaScript_admits_secret_bound_arguments_in_the_background()
    {
        // C10: the secret travels as a withheld reference and resolves at worker activation.
        var executable = await SecretBindingCompilerFixture.CompileAsync(
            Background(typeof(RunJavaScript), [.. RequiredInputs(typeof(RunJavaScript)), SecretBindingCompilerFixture.Secret("arguments")]),
            [typeof(RunJavaScript)]);

        Assert.Equal(RuntimeInputBindingSource.SecretRead, executable.RootActivity.InputBindings["arguments"].Source);
    }

    [Theory]
    [InlineData(typeof(SequenceActivity), "VF-BG-001 (C1):")]
    [InlineData(typeof(Inline), "VF-BG-001 (C3):")]
    [InlineData(typeof(WriteHttpResponse), "does not declare BackgroundExecutionSafe")]
    [InlineData(typeof(SendHttpRequest), "does not declare BackgroundExecutionSafe")]
    [InlineData(typeof(ReadLine), "does not declare BackgroundExecutionSafe")]
    [InlineData(typeof(PublishEvent), "does not declare BackgroundExecutionSafe")]
    [InlineData(typeof(TimerActivity), "does not declare BackgroundExecutionSafe")]
    public async Task An_unmarked_built_in_is_refused_naming_why(Type activityType, string expected)
    {
        var refusal = await Assert.ThrowsAnyAsync<Exception>(() => SecretBindingCompilerFixture.CompileAsync(
            Background(activityType, RequiredInputs(activityType)), [activityType]));

        Assert.Contains(expected, refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_marked_Essentials_built_ins_are_exactly_the_assessed_set()
    {
        var assemblies = new[] { typeof(WriteLine).Assembly, typeof(RunJavaScript).Assembly, typeof(BpmnDecision).Assembly, typeof(SendHttpRequest).Assembly, typeof(TimerActivity).Assembly };
        var marked = assemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsClass: true, IsAbstract: false } && type.IsDefined(typeof(BackgroundExecutionSafeAttribute), inherit: true))
            .Select(type => type.FullName!)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(Marked.Select(type => type.FullName!).Order(StringComparer.Ordinal).ToArray(), marked);
    }

    private static ActivityNode Background(Type activityType, IEnumerable<WorkflowArgumentState> inputs) =>
        SecretBindingCompilerFixture.Node(activityType, [.. inputs]) with { ExecutionPlacement = ActivityExecutionPlacement.Background };

    private static WorkflowArgumentState[] RequiredInputs(Type activityType) => activityType.Name switch
    {
        nameof(WriteLine) => [Literal("text", "hello")],
        nameof(RunJavaScript) => [Literal("script", "return 1;")],
        _ => []
    };

    private static WorkflowArgumentState Literal(string key, string value) =>
        new(key, new ArgumentValue(JsonSerializer.SerializeToElement(value), "Literal"), null, null, null, null);
}
