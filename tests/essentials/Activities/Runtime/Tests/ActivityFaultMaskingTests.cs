using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Runtime.Services;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Services.Values;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// The work handlers' masking of an activity execution's failure text (spec 188, T076): an exception is replaced only
/// while a value is registered for the execution, whether or not its text holds one, and never when it is an activation
/// failure; a returned fault keeps everything but its masked message; disposing releases the execution's values.
/// </summary>
public sealed class ActivityFaultMaskingTests
{
    private const string Execution = "actexec-1";
    private readonly DefaultRuntimeSecretMask _mask = new();
    private readonly ActivityFaultMasking _masking;
    private readonly string _value = $"canary{Guid.NewGuid():N}";

    public ActivityFaultMaskingTests() => _masking = new(_mask, Execution);

    [Fact]
    public void An_exception_is_handed_on_unchanged_while_no_value_is_registered()
    {
        var exception = new InvalidOperationException($"refused {_value}");

        Assert.Same(exception, _masking.Mask(exception));
    }

    [Fact]
    public void An_exception_is_replaced_while_a_value_is_registered_even_when_its_text_holds_none()
    {
        RegisterValue();

        var masked = Assert.IsType<SecretMaskedException>(_masking.Mask(new RuntimeSecretResolutionException("payments.webhook-key", "StoreUnavailable", isRetryable: true)));

        Assert.Equal(typeof(RuntimeSecretResolutionException).FullName, masked.OriginalExceptionType);
        Assert.True(masked.IsRetryable);
        Assert.Equal("StoreUnavailable", masked.FailureCode);
    }

    [Fact]
    public void An_exception_carrying_a_registered_value_is_replaced_with_the_value_masked()
    {
        RegisterValue();

        var masked = _masking.Mask(new InvalidOperationException($"refused {_value}"));

        Assert.Equal("refused [secret:payments.api-key]", Assert.IsType<SecretMaskedException>(masked).Message);
    }

    [Fact]
    public void An_activation_failure_is_handed_on_unchanged_while_a_value_is_registered()
    {
        RegisterValue();
        var failure = new RuntimeDurableValueStorageDriverNotFoundException("missing-driver");

        Assert.Same(failure, _masking.Mask(failure));
    }

    [Fact]
    public void A_returned_fault_keeps_its_classification_and_gets_its_message_masked()
    {
        RegisterValue();

        var masked = _masking.Mask(new ActivityFault("secret.echoed", $"returned {_value}", isRetryable: true, category: "Remote", faultType: "Echo"));

        Assert.Equal("returned [secret:payments.api-key]", masked.Message);
        Assert.Equal(("secret.echoed", true, "Remote", "Echo"), (masked.Code, masked.IsRetryable, masked.Category, masked.FaultType));
    }

    [Fact]
    public void Disposing_releases_the_values_registered_for_the_execution()
    {
        RegisterValue();

        _masking.Dispose();

        Assert.False(_mask.HasRegistrations(Execution));
    }

    private void RegisterValue() => _mask.Register(Execution, "payments.api-key", _value);
}
