using Elsa.Foundation.Identity.OpenIddict.Groundwork.Exceptions;
using Groundwork.Documents.Store;

namespace Elsa.Foundation.Identity.OpenIddict.Groundwork.Tests;

public sealed class OpenIddictGroundworkFailureMappingTests
{
    [Fact]
    public void Unsupported_generic_delegate_has_a_stable_capability_outcome()
    {
        var exception = OpenIddictGroundworkFailureMapper.UnsupportedGenericQuery("token.GetAsync");

        Assert.Equal(OpenIddictGroundworkCapabilityException.UnsupportedGenericQueryCode, exception.Code);
        Assert.Equal("token.GetAsync", exception.Operation);
        Assert.Contains("not admitted", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Failure_mapper_preserves_cancellation_and_translates_concurrency()
    {
        var cancellation = new OperationCanceledException();

        Assert.Same(cancellation, OpenIddictGroundworkFailureMapper.Translate(cancellation));
        Assert.IsType<global::OpenIddict.Abstractions.OpenIddictExceptions.ConcurrencyException>(
            OpenIddictGroundworkFailureMapper.WriteFailure(DocumentStoreWriteStatus.ConcurrencyConflict));
    }
}
