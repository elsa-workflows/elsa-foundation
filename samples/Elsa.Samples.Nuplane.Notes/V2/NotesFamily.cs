using Elsa.Persistence.EntityFramework;
using Elsa.Samples.Nuplane.Notes;

// Release 1.1.0: the family is at 2.0.0 and its chain reaches back to 1.0.0 through one upcaster, so this build reads rows
// of both versions and a host still on release 1.0.0 reads only the older. Its rewriter brings the rows of 1.0.0 up to 2.0.0
// in the background once that version is finalized, so the family can be recorded complete at it.
[assembly: EfSchemaFamily(NotesModule.Family, NotesModule.Name, NotesModule.CurrentVersion, Upcasters = [typeof(NotesOneToTwo)], Rewriter = typeof(NotesRewriter))]

// The tags are a document: a read upcasts them through the chain, and a write that changes them restamps the row.
[assembly: EfSchemaContent(NotesModule.Family, typeof(NoteRecord), nameof(NoteRecord.TagsJson))]

namespace Elsa.Samples.Nuplane.Notes;

public static partial class NotesModule
{
    /// <summary>The version release 1.1.0 adds: notes may also carry tags, in a column the first release has no idea of.</summary>
    public const string TagsVersion = "2.0.0";

    /// <summary>The version this build reads and, once every host sharing the database can read it, writes.</summary>
    public const string CurrentVersion = TagsVersion;

    /// <summary>The feature that serves tags: dormant until <see cref="TagsVersion"/> is finalized.</summary>
    public const string TagsFeature = "NotesWithTags";
}
