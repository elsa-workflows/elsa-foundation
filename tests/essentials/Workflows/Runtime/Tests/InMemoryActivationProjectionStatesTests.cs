using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Triggers;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// The lifecycle both in-memory projection stores keep beside their rows (#2193): it must read the way the EF stores
/// derive it from a projection state's revision, and refuse what they refuse.
/// </summary>
public sealed class InMemoryActivationProjectionStatesTests
{
    private const string Projection = "test";
    private readonly InMemoryActivationProjectionStates _states = new();

    [Fact]
    public void An_activation_is_missing_until_prepared_and_prepared_until_switched_on()
    {
        Assert.Equal(WorkflowActivationProjectionState.Missing, _states.Find("activation-1"));

        _states.Prepare("activation-1", Projection);
        Assert.Equal(WorkflowActivationProjectionState.Prepared, _states.Find("activation-1"));

        Assert.True(_states.Activate("activation-1", null, Projection));
        Assert.Equal(WorkflowActivationProjectionState.Active, _states.Find("activation-1"));
    }

    [Fact]
    public void Only_an_activation_that_served_reads_as_replaced()
    {
        ActivateAsFirst("activation-1");
        _states.Prepare("activation-2", Projection);

        Assert.True(_states.Activate("activation-2", "activation-1", Projection));

        Assert.Equal(WorkflowActivationProjectionState.Active, _states.Find("activation-2"));
        Assert.Equal(WorkflowActivationProjectionState.Replaced, _states.Find("activation-1"));
    }

    [Fact]
    public void A_switch_already_made_is_a_no_op_whoever_repeats_it()
    {
        ActivateAsFirst("activation-1");
        _states.Prepare("activation-2", Projection);
        _states.Activate("activation-2", "activation-1", Projection);

        Assert.False(_states.Activate("activation-2", "activation-1", Projection));
        Assert.False(_states.Activate("activation-2", null, Projection));
        Assert.False(_states.Activate("activation-2", "activation-2", Projection));
        Assert.Equal(WorkflowActivationProjectionState.Replaced, _states.Find("activation-1"));
    }

    [Fact]
    public void A_candidate_cannot_replace_an_activation_that_no_longer_serves()
    {
        ActivateAsFirst("activation-1");
        _states.Prepare("activation-2", Projection);
        _states.Activate("activation-2", "activation-1", Projection);
        _states.Prepare("activation-3", Projection);

        Assert.Throws<InvalidOperationException>(() => _states.Activate("activation-3", "activation-1", Projection));
        Assert.Throws<InvalidOperationException>(() => _states.Activate("activation-3", "activation-missing", Projection));
        Assert.Equal(WorkflowActivationProjectionState.Prepared, _states.Find("activation-3"));
        Assert.Equal(WorkflowActivationProjectionState.Replaced, _states.Find("activation-1"));
    }

    [Fact]
    public void A_serving_candidate_beside_its_serving_replaced_activation_is_refused()
    {
        ActivateAsFirst("activation-1");
        ActivateAsFirst("activation-2");

        Assert.Throws<InvalidOperationException>(() => _states.Activate("activation-2", "activation-1", Projection));
        Assert.Equal(WorkflowActivationProjectionState.Active, _states.Find("activation-1"));
    }

    [Fact]
    public void An_activation_with_no_prepared_projection_cannot_be_switched_on() =>
        Assert.Throws<InvalidOperationException>(() => _states.Activate("activation-1", null, Projection));

    [Fact]
    public void A_replaced_activation_serves_again_when_switched_back_on()
    {
        ActivateAsFirst("activation-1");
        _states.Prepare("activation-2", Projection);
        _states.Activate("activation-2", "activation-1", Projection);

        Assert.True(_states.Activate("activation-1", "activation-2", Projection));

        Assert.Equal(WorkflowActivationProjectionState.Active, _states.Find("activation-1"));
        Assert.Equal(WorkflowActivationProjectionState.Replaced, _states.Find("activation-2"));
    }

    [Fact]
    public void An_activation_that_serves_or_served_cannot_be_prepared_again_until_removed()
    {
        ActivateAsFirst("activation-1");
        _states.Prepare("activation-2", Projection);
        _states.Activate("activation-2", "activation-1", Projection);

        Assert.Throws<InvalidOperationException>(() => _states.Prepare("activation-1", Projection));
        Assert.Throws<InvalidOperationException>(() => _states.Prepare("activation-2", Projection));
        Assert.Equal(WorkflowActivationProjectionState.Replaced, _states.Find("activation-1"));

        _states.Remove("activation-1");
        Assert.Equal(WorkflowActivationProjectionState.Missing, _states.Find("activation-1"));
        _states.Prepare("activation-1", Projection);
        Assert.Equal(WorkflowActivationProjectionState.Prepared, _states.Find("activation-1"));
    }

    [Fact]
    public void Preparing_a_prepared_activation_again_keeps_it_prepared()
    {
        _states.Prepare("activation-1", Projection);
        _states.Prepare("activation-1", Projection);

        Assert.Equal(WorkflowActivationProjectionState.Prepared, _states.Find("activation-1"));
    }

    private void ActivateAsFirst(string activationId)
    {
        _states.Prepare(activationId, Projection);
        _states.Activate(activationId, null, Projection);
    }
}
