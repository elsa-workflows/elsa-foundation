using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Incidents;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// <see cref="SecretMaskedException"/> (spec 188, T074, research R9): it carries the masked message, stack trace and
/// inner exception chain of the exception it stands in for, that exception's type name and its classification, and
/// nothing else of it; the fault capture policy reports the original type name for it (T076).
/// </summary>
public sealed class SecretMaskedExceptionTests
{
    private const string Marker = "[secret:payments.api-key]";
    private readonly string _value = $"canary{Guid.NewGuid():N}";

    [Fact]
    public void The_message_the_stack_trace_and_every_inner_exception_are_masked()
    {
        var original = new InvalidOperationException(
            $"outer {_value}",
            new FormatException($"middle {_value}", new StackCarryingException($"inner {_value}", $"   at Activity.Run({_value})")));

        var masked = Mask(original);

        Assert.Equal($"outer {Marker}", masked.Message);
        var middle = Assert.IsType<SecretMaskedException>(masked.InnerException);
        Assert.Equal($"middle {Marker}", middle.Message);
        Assert.Equal(typeof(FormatException).FullName, middle.OriginalExceptionType);
        var inner = Assert.IsType<SecretMaskedException>(middle.InnerException);
        Assert.Equal($"inner {Marker}", inner.Message);
        Assert.Equal($"   at Activity.Run({Marker})", inner.StackTrace);
        Assert.Null(inner.InnerException);
        // A log line renders the exception through ToString(), which walks the whole chain.
        Assert.DoesNotContain(_value, masked.ToString(), StringComparison.Ordinal);
        Assert.Contains(Marker, masked.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_messages_of_every_aggregated_exception_are_masked()
    {
        var original = new AggregateException("both failed", new InvalidOperationException($"first {_value}"), new IOException($"second {_value}"));

        var masked = Mask(original);

        Assert.Equal($"both failed (first {Marker}) (second {Marker})", masked.Message);
        Assert.Equal(typeof(AggregateException).FullName, masked.OriginalExceptionType);
    }

    [Fact]
    public void The_original_type_name_is_kept_and_nothing_else_of_the_original_is_carried()
    {
        var original = new InvalidOperationException($"failed {_value}");
        original.Data["token"] = _value;

        var masked = Mask(original);

        Assert.Equal(typeof(InvalidOperationException).FullName, masked.OriginalExceptionType);
        Assert.Empty(masked.Data);
    }

    [Fact]
    public void A_retryable_classification_is_copied_from_the_exception_it_stands_in_for()
    {
        var masked = Mask(new RuntimeSecretResolutionException("payments.webhook-key", "StoreUnavailable", isRetryable: true));

        Assert.True(masked.IsRetryable);
        Assert.Equal("StoreUnavailable", masked.FailureCode);
    }

    [Fact]
    public void A_permanent_classification_is_copied_from_the_exception_it_stands_in_for()
    {
        var masked = Mask(new RuntimeSecretResolutionException("payments.webhook-key", "Revoked", isRetryable: false));

        Assert.False(masked.IsRetryable);
        Assert.Equal("Revoked", masked.FailureCode);
    }

    [Fact]
    public void An_unclassified_exception_stays_unretryable_and_without_a_code()
    {
        var masked = Mask(new InvalidOperationException($"failed {_value}"));

        Assert.False(masked.IsRetryable);
        Assert.Null(masked.FailureCode);
    }

    [Fact]
    public void The_fault_capture_policy_reports_the_original_type_names_and_the_masked_messages()
    {
        var policy = new DefaultRuntimeFaultCapturePolicy(Options.Create(new RuntimeFaultCaptureOptions { CaptureStackTrace = true }));
        var masked = Mask(new InvalidOperationException($"outer {_value}", new FormatException($"inner {_value}")));

        var fault = policy.Capture(masked);
        var inner = policy.CaptureInner(masked);

        Assert.Equal(typeof(InvalidOperationException).FullName, fault.ExceptionType);
        Assert.Equal($"outer {Marker}", fault.Message);
        Assert.Equal(typeof(FormatException).FullName, inner!.ExceptionType);
        Assert.Equal($"inner {Marker}", inner.Message);
    }

    [Fact]
    public void The_fault_capture_policy_falls_back_to_the_original_type_name_when_the_message_is_blank()
    {
        var policy = DefaultRuntimeFaultCapturePolicy.CreateDefault();

        var fault = policy.Capture(Mask(new StackCarryingException("   ", stackTrace: null)));

        Assert.Equal(nameof(StackCarryingException), fault.Message);
    }

    private SecretMaskedException Mask(Exception exception) =>
        new(exception, text => text.Replace(_value, Marker, StringComparison.Ordinal));

    private sealed class StackCarryingException(string message, string? stackTrace) : Exception(message)
    {
        public override string? StackTrace => stackTrace;
    }
}
