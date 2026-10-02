using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// The runtime-owned secret resolution shapes (spec 188, T017, T030): nothing a resolver hands back can carry free text
/// into a fault, and a resolved value is never printed.
/// </summary>
public sealed class RuntimeSecretResolutionTests
{
    private const string Value = "resolved-secret-value";

    [Fact]
    public void A_success_carries_the_value_and_no_failure()
    {
        var resolution = RuntimeSecretResolution.Success(Value);

        Assert.True(resolution.Succeeded);
        Assert.Equal(Value, resolution.Value);
        Assert.Null(resolution.FailureCode);
        Assert.False(resolution.IsRetryable);
    }

    [Theory]
    [InlineData("StoreUnavailable", true)]
    [InlineData("NotFound", false)]
    public void A_failure_carries_its_code_and_classification_and_no_value(string failureCode, bool isRetryable)
    {
        var resolution = RuntimeSecretResolution.Failure(failureCode, isRetryable);

        Assert.False(resolution.Succeeded);
        Assert.Null(resolution.Value);
        Assert.Equal(failureCode, resolution.FailureCode);
        Assert.Equal(isRetryable, resolution.IsRetryable);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Store unavailable")]
    [InlineData("StoreUnavailable: connection to db-1 refused")]
    [InlineData("1NotFound")]
    public void A_failure_code_that_is_not_a_code_name_is_refused(string failureCode)
    {
        Assert.ThrowsAny<ArgumentException>(() => RuntimeSecretResolution.Failure(failureCode, isRetryable: false));
    }

    [Fact]
    public void A_success_without_a_value_is_refused()
    {
        Assert.Throws<ArgumentNullException>(() => RuntimeSecretResolution.Success(null!));
    }

    [Fact]
    public void A_resolution_never_prints_its_value()
    {
        Assert.DoesNotContain(Value, RuntimeSecretResolution.Success(Value).ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_request_requires_a_tenant_and_a_reference()
    {
        Assert.ThrowsAny<ArgumentException>(() => new RuntimeSecretResolutionRequest(" ", new RuntimeSecretReference("payments.api-key")));
        Assert.Throws<ArgumentNullException>(() => new RuntimeSecretResolutionRequest("tenant-a", null!));
    }

    [Fact]
    public void A_resolution_failure_names_the_reference_and_code_only_and_is_classified()
    {
        var exception = new RuntimeSecretResolutionException("payments.api-key", "StoreUnavailable", isRetryable: true);
        IRuntimeFaultClassification classification = exception;

        Assert.Equal("Secret 'payments.api-key' could not be resolved (StoreUnavailable).", exception.Message);
        Assert.True(classification.IsRetryable);
        Assert.Equal("StoreUnavailable", classification.FailureCode);
    }
}
