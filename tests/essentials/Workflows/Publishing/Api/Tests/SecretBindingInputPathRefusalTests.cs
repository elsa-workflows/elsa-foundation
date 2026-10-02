using Elsa.Activities.Http.Activities;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Scheduling.Activities;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;
using VariableDefinition = Elsa.Expressions.Core.Models.VariableDefinition;
using VariableReference = Elsa.Expressions.Core.Models.VariableReference;
using CronActivity = Elsa.Activities.Scheduling.Activities.Cron;
using TimerActivity = Elsa.Activities.Scheduling.Activities.Timer;
using static Elsa.Workflows.Publishing.Api.Tests.SecretBindingCompilerFixture;

namespace Elsa.Workflows.Publishing.Api.Tests;

/// <summary>
/// Publication refuses a <c>Secret</c> binding (<c>VF-ACT-012</c>) on every input an activity persists, returns,
/// copies into a fault or needs as a literal at publish, before the input's conversion plan is resolved, so the
/// refusal names the reason even where the input's type would also refuse text (spec 188, T099 and T110; research
/// R3a IP14 to IP22).
/// </summary>
public sealed class SecretBindingInputPathRefusalTests
{
    private const SecretBindingRefusalReason Persisted = SecretBindingRefusalReason.PersistedByActivity;
    private const SecretBindingRefusalReason Fixed = SecretBindingRefusalReason.FixedAtPublish;
    private const SecretBindingRefusalReason Echoed = SecretBindingRefusalReason.EchoedToOutput;

    public static TheoryData<Type, string, SecretBindingRefusalReason> DeclaredRefusals => new()
    {
        { typeof(SecretDeclaringActivity), nameof(SecretDeclaringActivity.Persisted), Persisted },
        { typeof(SecretDeclaringActivity), nameof(SecretDeclaringActivity.Fixed), Fixed },
        { typeof(SecretDeclaringActivity), nameof(SecretDeclaringActivity.Echoed), Echoed },
        // IP14: copied into the activity's own persisted state.
        { typeof(PublishEvent), nameof(PublishEvent.EventName), Persisted },
        { typeof(PublishEvent), nameof(PublishEvent.CorrelationId), Persisted },
        { typeof(PublishEvent), nameof(PublishEvent.Payload), Persisted },
        { typeof(Delay), "Duration", Persisted },
        // IP15 to IP17: read as literals when the trigger is described at publish.
        { typeof(Event), nameof(Event.EventName), Fixed },
        { typeof(Event), nameof(Event.CorrelationId), Fixed },
        { typeof(Event), nameof(Event.CanStartWorkflow), Fixed },
        { typeof(CronActivity), nameof(CronActivity.Expression), Fixed },
        { typeof(TimerActivity), nameof(TimerActivity.Interval), Fixed },
        { typeof(HttpEndpoint), nameof(HttpEndpoint.Path), Fixed },
        { typeof(HttpEndpoint), nameof(HttpEndpoint.SupportedMethods), Fixed },
        { typeof(HttpEndpoint), nameof(HttpEndpoint.CanStartWorkflow), Fixed },
        { typeof(HttpEndpoint), nameof(HttpEndpoint.Authorize), Fixed },
        { typeof(HttpEndpoint), nameof(HttpEndpoint.Policy), Fixed },
        { typeof(HttpEndpoint), nameof(HttpEndpoint.RequestTimeout), Fixed },
        { typeof(HttpEndpoint), nameof(HttpEndpoint.RequestSizeLimit), Fixed },
        { typeof(HttpEndpoint), nameof(HttpEndpoint.ResponseMode), Fixed },
        // IP22: returned in the result or copied into a reported fault. Inline's input is an Object, which the
        // conversion rule would refuse too; the declaration is checked first so the author sees the real reason.
        { typeof(Inline), nameof(Inline.Expression), Echoed },
        { typeof(WriteHttpResponse), nameof(WriteHttpResponse.Body), Echoed },
        { typeof(WriteHttpResponse), nameof(WriteHttpResponse.ContentType), Echoed },
        { typeof(Fault), "code", Echoed },
        { typeof(Fault), "message", Echoed },
        { typeof(Fault), "category", Echoed },
        { typeof(Fault), "faultType", Echoed }
    };

    [Theory]
    [MemberData(nameof(DeclaredRefusals))]
    public async Task A_secret_reference_on_an_input_the_activity_declares_is_refused_with_its_reason(
        Type activityType,
        string inputKey,
        SecretBindingRefusalReason reason)
    {
        var exception = await AssertRefusedAsync(Node(activityType, Secret(inputKey)), [activityType]);

        Assert.Equal(SecretBindingDiagnostics.SecretBindingRefused(NodeId, inputKey, reason).Message, exception.Message);
        Assert.DoesNotContain(SecretName, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_input_the_activity_does_not_name_accepts_a_secret_reference()
    {
        var executable = await CompileAsync(
            Node(typeof(SecretDeclaringActivity), Secret(nameof(SecretDeclaringActivity.Unnamed))),
            [typeof(SecretDeclaringActivity)]);

        Assert.Equal(RuntimeInputBindingSource.SecretRead, executable.RootActivity.InputBindings[nameof(SecretDeclaringActivity.Unnamed)].Source);
    }

    [Fact]
    public async Task The_value_outcomes_input_refuses_a_secret_reference()
    {
        // IP20: SendHttpRequest derives its outcome ports from the authored ExpectedStatusCodes literal at publish.
        var exception = await AssertRefusedAsync(
            Node(typeof(SendHttpRequest), Secret(nameof(SendHttpRequest.ExpectedStatusCodes))),
            [typeof(SendHttpRequest)]);

        Assert.Equal(
            SecretBindingDiagnostics.ValueOutcomesInputRefused(NodeId, nameof(SendHttpRequest.ExpectedStatusCodes)).Message,
            exception.Message);
    }

    [Fact]
    public async Task A_variable_default_refuses_a_secret_reference()
    {
        // IP21: a variable's initial value is persisted in its variable frame, so it gets the secret refusal rather
        // than the generic "persistable literal initial binding" message.
        var variable = new VariableDefinition("var-token", "Token", new TypeReference("String"), StorageDriverType: null, Default: SecretValue());

        var exception = await AssertRefusedAsync(
            Node(typeof(TestWriteLineActivity)),
            [typeof(TestWriteLineActivity)],
            variables: [variable]);

        Assert.Equal(SecretBindingDiagnostics.VariableDefaultRefused(VariableReference.WorkflowScopeId, "var-token").Message, exception.Message);
    }

    [RefusesSecretBinding(nameof(Persisted), SecretBindingRefusalReason.PersistedByActivity)]
    [RefusesSecretBinding(nameof(Fixed), SecretBindingRefusalReason.FixedAtPublish)]
    [RefusesSecretBinding(nameof(Echoed), SecretBindingRefusalReason.EchoedToOutput)]
    private sealed class SecretDeclaringActivity : Activity<ActivityUnit>
    {
        [ActivityInput(Key = nameof(Persisted))]
        public string? Persisted { get; set; }

        [ActivityInput(Key = nameof(Fixed))]
        public string? Fixed { get; set; }

        [ActivityInput(Key = nameof(Echoed))]
        public string? Echoed { get; set; }

        [ActivityInput(Key = nameof(Unnamed))]
        public string? Unnamed { get; set; }

        protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value));
    }
}
