using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Samples.Nuplane.Notes.Activities;

public sealed partial class AddNote
{
    private async Task WriteAsync(IServiceProvider services, string text, CancellationToken cancellationToken) =>
        await services.GetRequiredService<NoteStore>().AddAsync(text, cancellationToken);
}
