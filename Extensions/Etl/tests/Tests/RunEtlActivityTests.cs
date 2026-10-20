using System.Text.Json;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Workflows.Runtime.Activities;
using Elsa.Etl.Workflows.Runtime.Models;
using Elsa.Etl.Workflows.Runtime.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Etl.Tests;

public sealed class RunEtlActivityTests
{
    [Fact]
    public async Task Activation_uses_the_executable_pin_and_trusted_scope_to_suspend_with_one_typed_wait()
    {
        await using var fixture = await EtlNativeTestFixture.CreateAsync(new(null, "partition-\uD800"));
        await fixture.SetExecutableAsync();
        await using var scope = await fixture.OpenNativeScopeAsync();
        var activity = NewActivity(fixture, scope.ServiceProvider.GetRequiredService<IPersistenceAccessContextAccessor>());

        var transition = await ((IActivity)activity).ExecuteAsync(Context(fixture));

        var suspension = Assert.IsAssignableFrom<IStatefulActivitySuspensionTransition<RunEtlState>>(transition);
        var request = EtlNativeBoundary.ReadState(suspension.State);
        Assert.Equal(fixture.Invocation.Scope, request.Scope);
        Assert.Equal(fixture.Invocation.RunId, request.RunId);
        Assert.Equal(fixture.Invocation.ResultAddress.ResumeTargetId, request.ResumeTargetId);
        Assert.Equal(fixture.Invocation.Artifact.ArtifactId, request.Artifact.ArtifactId);
        var registration = Assert.Single(suspension.Registrations);
        Assert.Equal(EtlNativeBoundary.ResumeTargetKey, registration.ResumeTargetKey);
        Assert.Equal(fixture.Invocation.RunId, registration.StimulusHash);
        Assert.Equal(EtlNativeBoundary.CompletionType, registration.PayloadType);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("foreign-node")]
    [InlineData("wrong-handler")]
    public async Task Activation_refuses_an_absent_or_incorrectly_owned_resume_target(string change)
    {
        await using var fixture = await EtlNativeTestFixture.CreateAsync();
        await fixture.SetExecutableAsync(resumeTargets: target => change == "missing"
            ? new Dictionary<string, WorkflowExecutableResumeTarget>()
            : new Dictionary<string, WorkflowExecutableResumeTarget>
            {
                [change == "foreign-node" ? EtlNativeBoundary.ResumeTargetKey : target.ResumeTargetId] = target with
                {
                    ResumeTargetId = change == "foreign-node" ? EtlNativeBoundary.ResumeTargetKey : target.ResumeTargetId,
                    ExecutableNodeId = change == "foreign-node" ? "other-node" : target.ExecutableNodeId,
                    HandlerKey = change == "wrong-handler" ? "OtherHandler" : target.HandlerKey
                }
            });
        await using var scope = await fixture.OpenNativeScopeAsync();
        var activity = NewActivity(fixture, scope.ServiceProvider.GetRequiredService<IPersistenceAccessContextAccessor>());

        await Assert.ThrowsAsync<InvalidDataException>(async () => await ((IActivity)activity).ExecuteAsync(Context(fixture)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Activation_refuses_a_pin_or_hydrated_value_that_is_not_the_hashed_literal(bool replacedPin)
    {
        await using var fixture = await EtlNativeTestFixture.CreateAsync();
        var selected = new EtlPublishedSelection(fixture.Invocation.Artifact.DefinitionId,
            fixture.Invocation.Artifact.DefinitionVersion, new string('b', 64)).ToToken();
        await fixture.SetExecutableAsync(literalToken: replacedPin ? selected : null);
        await using var scope = await fixture.OpenNativeScopeAsync();
        var activity = NewActivity(fixture, scope.ServiceProvider.GetRequiredService<IPersistenceAccessContextAccessor>());
        activity.Pipeline = selected;

        await Assert.ThrowsAsync<InvalidDataException>(async () => await ((IActivity)activity).ExecuteAsync(Context(fixture)));
    }

    [Fact]
    public async Task A_durable_cancelled_result_uses_the_normal_native_cancellation_transition()
    {
        await using var fixture = await EtlNativeTestFixture.CreateAsync();
        await using var scope = await fixture.OpenNativeScopeAsync();
        var activity = NewActivity(fixture, scope.ServiceProvider.GetRequiredService<IPersistenceAccessContextAccessor>());

        var transition = await ((IStatefulActivity<RunEtlResult, RunEtlState, RunEtlCompletion>)activity)
            .ResumeAsync(ResumeContext(fixture));

        Assert.IsAssignableFrom<IActivityCancellationTransition>(transition);
    }

    [Theory]
    [InlineData("registration")]
    [InlineData("node")]
    [InlineData("provider")]
    [InlineData("counts")]
    public async Task Resume_refuses_changed_result_or_address_before_returning_an_outcome(string change)
    {
        await using var fixture = await EtlNativeTestFixture.CreateAsync();
        await using var scope = await fixture.OpenNativeScopeAsync();
        var activity = NewActivity(fixture, scope.ServiceProvider.GetRequiredService<IPersistenceAccessContextAccessor>());
        var invocation = change switch
        {
            "node" => fixture.Invocation with { ResultAddress = fixture.Invocation.ResultAddress with { NodeId = "other-node" } },
            "provider" => fixture.Invocation with { ResultAddress = fixture.Invocation.ResultAddress with { ProviderId = "other-provider" } },
            _ => fixture.Invocation
        };
        var result = change == "counts" ? fixture.Delivery.Result with { AcceptedRecords = 1 } : fixture.Delivery.Result;
        var context = ResumeContext(fixture, invocation, result, change == "registration" ? "other-registration" : null);

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await ((IStatefulActivity<RunEtlResult, RunEtlState, RunEtlCompletion>)activity).ResumeAsync(context));
    }

    private static RunEtl NewActivity(EtlNativeTestFixture fixture, IPersistenceAccessContextAccessor access) =>
        new(fixture.Workflows, fixture.Executables, fixture.Activities, fixture.Store, access)
        {
            Pipeline = new EtlPublishedSelection(fixture.Invocation.Artifact.DefinitionId,
                fixture.Invocation.Artifact.DefinitionVersion, fixture.Invocation.Artifact.ContentHash).ToToken()
        };

    private static ActivityExecutionContext Context(EtlNativeTestFixture fixture)
    {
        var address = fixture.Invocation.ResultAddress;
        return new(address.WorkflowExecutionId, address.ActivityExecutionId, "fresh-activation", address.NodeId, CancellationToken.None);
    }

    private static ActivityResumeContext<RunEtlState, RunEtlCompletion> ResumeContext(EtlNativeTestFixture fixture,
        EtlInvocation? invocation = null, EtlTerminalResult? result = null, string? registrationId = null)
    {
        var stored = EtlNativeCheckpointTests.Activity(fixture.Scope);
        var state = stored.PrivateState!.Value.InlineValue!.Value.Deserialize<RunEtlState>()!;
        var completion = new RunEtlCompletion(1, EtlRuntimeJson.WriteElement(invocation ?? fixture.Invocation),
            EtlRuntimeJson.WriteElement(result ?? fixture.Delivery.Result));
        return new(Context(fixture), state, completion, registrationId ?? fixture.Invocation.ResultAddress.RegistrationId,
            fixture.Delivery.DeliveryId);
    }
}
