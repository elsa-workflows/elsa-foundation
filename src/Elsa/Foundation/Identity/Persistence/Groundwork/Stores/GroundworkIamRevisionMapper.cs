using Elsa.Foundation.Identity.Abstractions.Iam;
using Groundwork.Documents.Store;

namespace Elsa.Foundation.Identity.Persistence.Groundwork.Stores;

internal static class GroundworkIamRevisionMapper
{
    public static string Revision(DocumentEnvelope envelope) =>
        IdentityRevisionStamp.FromVersion(envelope.Version).ToString();

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

    public static IamRevisionSaveResult ToResult(DocumentStoreWriteResult result) =>
        result.Status switch
        {
            DocumentStoreWriteStatus.Saved when result.Document is not null =>
                new IamRevisionSaveResult(IamRevisionSaveStatus.Saved, Revision(result.Document)),
            DocumentStoreWriteStatus.Saved =>
                new IamRevisionSaveResult(IamRevisionSaveStatus.Saved),
            DocumentStoreWriteStatus.NotFound =>
                new IamRevisionSaveResult(IamRevisionSaveStatus.NotFound),
            _ =>
                new IamRevisionSaveResult(IamRevisionSaveStatus.Conflict)
        };

    public static IamRevisionSaveResult InvalidRevision() =>
        new(IamRevisionSaveStatus.Conflict);
}
