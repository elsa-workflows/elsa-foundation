using Elsa.Secrets.Core.Contracts;
using Groundwork.Documents.Store;

namespace Elsa.Secrets.Persistence.Groundwork.Stores;

internal static class SecretRevisionMapper
{
    public static string Revision(DocumentEnvelope envelope) =>
        SecretRevisionStamp.FromVersion(envelope.Version).ToString();

    public static bool TryExpectedVersion(string? revision, out long? expectedVersion)
    {
        if (revision is null)
        {
            expectedVersion = 0;
            return true;
        }

        if (SecretRevisionStamp.TryGetVersion(revision, out var version))
        {
            expectedVersion = version;
            return true;
        }

        expectedVersion = null;
        return false;
    }

    public static SecretRevisionSaveResult ToResult(DocumentStoreWriteResult result) =>
        result.Status switch
        {
            DocumentStoreWriteStatus.Saved when result.Document is not null =>
                new SecretRevisionSaveResult(SecretRevisionSaveStatus.Saved, Revision(result.Document)),
            DocumentStoreWriteStatus.Saved =>
                new SecretRevisionSaveResult(SecretRevisionSaveStatus.Saved),
            DocumentStoreWriteStatus.NotFound =>
                new SecretRevisionSaveResult(SecretRevisionSaveStatus.NotFound),
            _ =>
                new SecretRevisionSaveResult(SecretRevisionSaveStatus.Conflict)
        };

    public static SecretRevisionSaveResult InvalidRevision() =>
        new(SecretRevisionSaveStatus.Conflict);
}

internal readonly record struct SecretRevisionStamp(string Value)
{
    public static SecretRevisionStamp FromVersion(long version) => new("gw:" + version.ToString("D20"));

    public static bool TryGetVersion(string? value, out long version)
    {
        version = 0;

        return value is not null &&
               value.StartsWith("gw:", StringComparison.Ordinal) &&
               value.Length == 23 &&
               long.TryParse(value.AsSpan(3), out version);
    }

    public override string ToString() => Value;
}
