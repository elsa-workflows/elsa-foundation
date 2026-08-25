using Elsa.Foundation.Identity.Abstractions.Iam;
using Groundwork.Store;

namespace Elsa.Foundation.Identity.Persistence.Groundwork.Stores;

internal static class GroundworkIamRevisionMapper
{
    public static string Revision(GroundworkIdentityRow row) =>
        IdentityRevisionStamp.FromVersion(row.Version).ToString();

    public static bool TryExpectedVersion(string? revision, out long? expectedVersion)
    {
        if (revision is null)
        {
            expectedVersion = 0;
            return true;
        }

        if (IdentityRevisionStamp.TryGetVersion(revision, out var version))
        {
            expectedVersion = version;
            return true;
        }

        expectedVersion = null;
        return false;
    }

    public static IamRevisionSaveResult ToResult(GroundworkIdentityWriteResult result) =>
        result.Status switch
        {
            _ when result.Succeeded && result.Version is { } version =>
                new IamRevisionSaveResult(
                    IamRevisionSaveStatus.Saved,
                    IdentityRevisionStamp.FromVersion(version).ToString()),
            _ when result.Succeeded =>
                new IamRevisionSaveResult(IamRevisionSaveStatus.Saved),
            WriteOutcomeStatus.NotFound =>
                new IamRevisionSaveResult(IamRevisionSaveStatus.NotFound),
            _ =>
                new IamRevisionSaveResult(IamRevisionSaveStatus.Conflict)
        };

    public static IamRevisionSaveResult InvalidRevision() =>
        new(IamRevisionSaveStatus.Conflict);
}
