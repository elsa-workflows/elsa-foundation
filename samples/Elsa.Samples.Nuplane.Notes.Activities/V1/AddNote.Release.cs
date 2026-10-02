using Elsa.Activities.Design.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Samples.Nuplane.Notes.Activities;

/// <summary>Release 1.0.0: a note has a text, and nothing else.</summary>
internal static class NotesActivitiesRelease
{
    public const string ActivityVersion = "1.0.0";

    public const string RequiredFeature = "Notes";

    public const string Description = "Adds a note with a text.";

    public static readonly InputDefinition[] AddedInputs = [];
}

public sealed partial class AddNote
{
    private async Task WriteAsync(IServiceProvider services, string text, CancellationToken cancellationToken) =>
        await services.GetRequiredService<NoteStore>().AddAsync(text, cancellationToken);
}
