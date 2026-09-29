using CShells.AspNetCore.Features;
using CShells.Features;
using Elsa.Primitives.Exceptions;
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
        endpoints.MapPost(NotesModule.NotesPath, (AddNoteRequest request, NoteStore store, CancellationToken cancellationToken) =>
            string.IsNullOrWhiteSpace(request.Text)
                ? Task.FromResult(Results.BadRequest("A note needs some text."))
                : NotesResults.ServeAsync(NotesModule.NotesFeature, async () => Results.Ok(await store.AddAsync(request.Text.Trim(), cancellationToken))));

        endpoints.MapGet(NotesModule.NotesPath, async (NoteStore store, CancellationToken cancellationToken) =>
            Results.Ok(await store.ListAsync(cancellationToken)));
    }
}

/// <summary>What both features answer when the schema versions refuse an operation.</summary>
internal static class NotesResults
{
    /// <summary>
    /// Runs <paramref name="operation"/> and answers 409 with the refusal's stable code and its reason when a schema
    /// version refuses it: the feature is dormant until the version it needs is finalized, or this host can no longer write
    /// the family because a newer release finalized a version it cannot read. Any other failure is not this method's to hide.
    /// </summary>
    public static async Task<IResult> ServeAsync(string feature, Func<Task<IResult>> operation)
    {
        try
        {
            return await operation();
        }
        catch (SchemaDormancyRefusedException refusal)
        {
            return Results.Conflict(new { code = refusal.Code, feature = refusal.FeatureId ?? feature, reason = refusal.Reason, message = refusal.Message });
        }
        catch (SchemaWriteRefusedException refusal)
        {
            return Results.Conflict(new { code = refusal.Code, feature, reason = refusal.Message, message = refusal.Message });
        }
    }
}
