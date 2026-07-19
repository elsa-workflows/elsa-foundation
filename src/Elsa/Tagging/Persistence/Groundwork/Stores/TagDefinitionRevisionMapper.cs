using Elsa.Tagging.Core.Contracts;
using Groundwork.Documents.Store;

namespace Elsa.Tagging.Persistence.Groundwork.Stores;

internal static class TagDefinitionRevisionMapper
{
    public static string Revision(DocumentEnvelope envelope) => "gw:" + envelope.Version.ToString("D20");

    public static bool TryExpectedVersion(string revision, out long expectedVersion)
    {
        expectedVersion = 0;
        return revision.StartsWith("gw:", StringComparison.Ordinal) && revision.Length == 23 && long.TryParse(revision.AsSpan(3), out expectedVersion);
    }

    public static TagDefinitionSaveResult ToResult(DocumentStoreWriteResult result) => result.Status switch
    {
        DocumentStoreWriteStatus.Saved when result.Document is not null => new(TagDefinitionSaveStatus.Saved, Revision(result.Document)),
        DocumentStoreWriteStatus.Saved => new(TagDefinitionSaveStatus.Saved),
        DocumentStoreWriteStatus.NotFound => new(TagDefinitionSaveStatus.NotFound),
        _ => new(TagDefinitionSaveStatus.Conflict)
    };
}
