using Elsa.Activities.Design.Core.Models;

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
