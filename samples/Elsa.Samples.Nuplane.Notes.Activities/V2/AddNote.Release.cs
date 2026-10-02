using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Samples.Nuplane.Notes.Activities;

/// <summary>Release 1.1.0: a note may also carry tags, stored in the column release 1.1.0 of the Notes module adds.</summary>
internal static class NotesActivitiesRelease
{
    public const string ActivityVersion = "1.1.0";

    /// <summary>The tags are written through the feature that serves them, and are dormant with it.</summary>
    public const string RequiredFeature = NotesModule.TagsFeature;

    public const string Description = "Adds a note with a text and, optionally, comma-separated tags.";

    public static readonly InputDefinition[] AddedInputs = [NotesActivityReconciliationSource.StringInput(nameof(AddNote.Tags), "Tags (comma-separated)")];
}

public sealed partial class AddNote
{
    [ActivityInput(Key = nameof(Tags))]
    public string? Tags { get; set; }

    /// <summary>
    /// Writes the note, then its tags. With tags, the dormancy of <see cref="NotesModule.TagsFeature"/> is checked before
    /// anything is written: while schema version 2.0.0 is not finalized the activity faults with the reason, and no note
    /// is added without the tags it was asked to carry.
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
