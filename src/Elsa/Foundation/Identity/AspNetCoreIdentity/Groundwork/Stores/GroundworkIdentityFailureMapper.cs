using Elsa.Foundation.Identity.Persistence.Groundwork.Exceptions;
using Microsoft.AspNetCore.Identity;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Stores;

public static class GroundworkIdentityFailureMapper
{
    public static IdentityResult ToIdentityResult(Exception exception) =>
        IdentityResult.Failed(new IdentityError
        {
            Code = exception switch
            {
                GroundworkIdentityConflictException => "GroundworkIdentityConflict",
                GroundworkIdentityUncertainCommitException => "GroundworkIdentityUncertainCommit",
                GroundworkIdentityCorruptStateException => "GroundworkIdentityCorruptState",
                GroundworkIdentityReadinessException => "GroundworkIdentityReadiness",
                GroundworkIdentitySerializationException => "GroundworkIdentitySerialization",
                _ => "GroundworkIdentityFailure"
            },
            Description = exception.Message
        });
}
