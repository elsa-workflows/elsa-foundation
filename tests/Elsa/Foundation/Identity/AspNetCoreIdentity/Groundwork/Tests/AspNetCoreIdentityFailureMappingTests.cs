using Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Stores;
using Elsa.Foundation.Identity.Persistence.Groundwork.Exceptions;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests;

public sealed class AspNetCoreIdentityFailureMappingTests
{
    public static IEnumerable<object[]> FailureMappings()
    {
        yield return [new GroundworkIdentityConflictException("conflict"), "GroundworkIdentityConflict"];
        yield return [new GroundworkIdentityUncertainCommitException("uncertain"), "GroundworkIdentityUncertainCommit"];
        yield return [new GroundworkIdentityCorruptStateException("corrupt"), "GroundworkIdentityCorruptState"];
        yield return [new GroundworkIdentityReadinessException("readiness"), "GroundworkIdentityReadiness"];
        yield return [new GroundworkIdentitySerializationException("serialization"), "GroundworkIdentitySerialization"];
        yield return [new TimeoutException("timeout"), "GroundworkIdentityFailure"];
    }

    [Theory]
    [MemberData(nameof(FailureMappings))]
    public void Exception_failure_mapper_preserves_expected_public_error_code(
        Exception exception,
        string expectedCode)
    {
        var result = GroundworkIdentityFailureMapper.ToIdentityResult(exception);

        Assert.False(result.Succeeded);
        var error = Assert.Single(result.Errors);
        Assert.Equal(expectedCode, error.Code);
        Assert.Equal(exception.Message, error.Description);
    }
}
