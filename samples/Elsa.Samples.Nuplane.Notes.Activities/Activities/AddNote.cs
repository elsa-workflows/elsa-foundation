using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;

// One namespace for the package, whatever the folder, as in the Notes sample: the activity's full name is its type key and
// its alias, which every workflow that uses it stores.
namespace Elsa.Samples.Nuplane.Notes.Activities;

/// <summary>
/// Adds a note through the Notes module's store, as <c>POST /demo/notes</c> does. What a release adds (V2/: tags) is in
/// <see cref="WriteAsync"/>, one implementation per release folder.
/// </summary>
/// <remarks>
/// <para>
/// It is constructed in a scope of its own for each execution, so <paramref name="services"/> resolves the scoped
/// <see cref="NoteStore"/> of that scope.
/// </para>
/// <para>
/// A workflow node pinned to version 1.0.0 keeps running after release 1.1.0 is installed in place, but on release 1.1.0's
/// class: the alias is the CLR type name, and one type is registered per alias. Release 1.1.0 therefore stays
/// input-compatible with 1.0.0: <see cref="Text"/> keeps its name and meaning, and the tags it adds are optional. A release
/// that renamed or removed an input would break every workflow pinned to an earlier version, and nothing warns of that yet.
/// </para>
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
