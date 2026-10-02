using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Samples.Nuplane.Notes.Activities;

public sealed partial class AddNote
{
    /// <summary>Optional, so a workflow pinned to version 1.0.0, which has no such input, runs on this release unchanged.</summary>
    [ActivityInput(Key = nameof(Tags))]
    public string? Tags { get; set; }

    /// <summary>
    /// Writes the note, then its tags. With tags, the dormancy of <see cref="NotesModule.TagsFeature"/> is checked before
    /// anything is written: while schema version 2.0.0 is not finalized the activity faults with the reason and writes
    /// nothing. Once it is available, the note and its tags are two saves, as <c>POST /demo/notes</c> and
    /// <c>POST /demo/notes/{id}/tags</c> are: a failure between them leaves the note added without its tags. Without tags it
    /// writes the note as release 1.0.0 does.
    /// </summary>
    private async Task WriteAsync(IServiceProvider services, string text, CancellationToken cancellationToken)
    {
        var tags = (Tags ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tags.Length > 0)
            await services.GetRequiredService<ISchemaDormancyCheck>().EnsureAvailableAsync(
                SchemaVersionRequirement.DeclaredBy(typeof(NotesWithTagsFeature)), NotesModule.TagsFeature, cancellationToken);

        var note = await services.GetRequiredService<NoteStore>().AddAsync(text, cancellationToken);
        if (tags.Length > 0)
            await services.GetRequiredService<NotesWithTags>().AddTagsAsync(note.Id, tags, cancellationToken);
    }
}
