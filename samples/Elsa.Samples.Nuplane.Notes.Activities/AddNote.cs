using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Samples.Nuplane.Notes.Activities;

/// <summary>
/// Adds a note through the Notes module's store, as <c>POST /demo/notes</c> does. What the release adds (V2/: tags) is in
/// <see cref="WriteAsync"/>, one implementation per release folder.
/// </summary>
/// <remarks>
/// It is constructed in a scope of its own for each execution, so <paramref name="services"/> resolves the scoped
/// <see cref="NoteStore"/> of that scope.
/// </remarks>
public sealed partial class AddNote(IServiceProvider services) : Activity<ActivityUnit>
{
    [ActivityInput(Key = nameof(Text))]
    public string? Text { get; set; }

    protected override async ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context)
    {
        if (string.IsNullOrWhiteSpace(Text))
            throw new InvalidOperationException("Add note needs some text.");

        await WriteAsync(services, Text.Trim(), context.CancellationToken);
        return ActivityTransition.Complete(ActivityUnit.Value);
    }
}
