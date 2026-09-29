using CShells.AspNetCore.Features;
using CShells.Features;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Samples.Nuplane.Notes;

/// <summary>The body of <c>POST /demo/notes</c>.</summary>
public sealed record AddNoteRequest(string? Text);

/// <summary>
/// The base endpoints, present in both releases: add a note, list the notes. They know nothing of tags, so they work
/// against a database at any version this build reads.
/// </summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Samples")]
[ShellFeature(
    name: NotesModule.NotesFeature,
    DisplayName = "Notes",
    Description = "Adds and lists notes: POST and GET /demo/notes.",
    DependsOn = new object[] { NotesModule.EntityFrameworkCoreFeature })]
public sealed class NotesFeature : IWebShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment)
    {
        endpoints.MapPost(NotesModule.NotesPath, async (AddNoteRequest request, NoteStore store, CancellationToken cancellationToken) =>
            string.IsNullOrWhiteSpace(request.Text)
                ? Results.BadRequest("A note needs some text.")
                : Results.Ok(await store.AddAsync(request.Text.Trim(), cancellationToken)));

        endpoints.MapGet(NotesModule.NotesPath, async (NoteStore store, CancellationToken cancellationToken) =>
            Results.Ok(await store.ListAsync(cancellationToken)));
    }
}
