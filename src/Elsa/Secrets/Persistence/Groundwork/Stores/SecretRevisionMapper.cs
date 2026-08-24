using Elsa.Secrets.Core.Contracts;
using Groundwork.Store;

namespace Elsa.Secrets.Persistence.Groundwork.Stores;

internal static class SecretRevisionMapper
{
    public static string Revision(long version) => SecretRevisionStamp.FromVersion(version).ToString();

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

    public static SecretRevisionSaveResult ToResult(WriteOutcome result) =>
        result.Status switch
        {
            WriteOutcomeStatus.Inserted or WriteOutcomeStatus.Updated or WriteOutcomeStatus.Upserted
                when result.Version is { } version =>
                new SecretRevisionSaveResult(SecretRevisionSaveStatus.Saved, Revision(version)),
            WriteOutcomeStatus.NotFound =>
                new SecretRevisionSaveResult(SecretRevisionSaveStatus.NotFound),
            _ =>
                result.Detail.Status == WriteOutcomeStatus.NotFound
                    ? new SecretRevisionSaveResult(SecretRevisionSaveStatus.NotFound)
                    : new SecretRevisionSaveResult(SecretRevisionSaveStatus.Conflict)
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
