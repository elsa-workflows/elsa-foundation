using CShells.AspNetCore.Features;
using CShells.Features;
using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Models;
using Elsa.Primitives.Exceptions;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Samples.Nuplane.Notes;

/// <summary>The body of <c>POST /demo/notes/{id}/tags</c>.</summary>
public sealed record AddTagsRequest(string[]? Tags);

/// <summary>
/// The feature only release 1.1.0 has, and the one that needs the new column: dormant until this host observes schema
/// version 2.0.0 of the family as finalized, which is when every host sharing the database can read it. It is composed
/// and its endpoints are mapped like any other; what they serve asks the shared dormancy check first, and answers 409
/// with the reason while the feature is dormant.
/// </summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Samples")]
[ShellFeature(
    name: NotesModule.TagsFeature,
    DisplayName = "Notes with tags",
    Description = "Lists notes with their tags and tags a note: GET /demo/notes/with-tags and POST /demo/notes/{id}/tags. Dormant (409) until schema version 2.0.0 of the notes family is finalized.",
    DependsOn = new object[] { NotesModule.NotesFeature })]
[RequiresSchemaVersion(NotesModule.Family, NotesModule.TagsVersion)]
public sealed class NotesWithTagsFeature : IWebShellFeature
{
    public void ConfigureServices(IServiceCollection services) => services.AddScoped<NotesWithTags>();

    public void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment)
    {
        endpoints.MapGet(NotesModule.NotesPath + "/with-tags", (NotesWithTags tags, CancellationToken cancellationToken) =>
            Serve(async () => Results.Ok(await tags.ListAsync(cancellationToken))));

        endpoints.MapPost(NotesModule.NotesPath + "/{id}/tags", (string id, AddTagsRequest request, NotesWithTags tags, CancellationToken cancellationToken) =>
            request.Tags is not { Length: > 0 }
                ? Task.FromResult(Results.BadRequest("Name at least one tag."))
                : Serve(async () => await tags.AddTagsAsync(id, request.Tags, cancellationToken) is { } note ? Results.Ok(note) : Results.NotFound()));
    }

    /// <summary>Answers 409 with the refusal's code and reason while the feature is dormant, and while a write it attempts is refused.</summary>
    private static async Task<IResult> Serve(Func<Task<IResult>> operation)
    {
        try
        {
            return await operation();
        }
        catch (SchemaDormancyRefusedException refusal)
        {
            return Results.Conflict(new { code = refusal.Code, feature = refusal.FeatureId, reason = refusal.Reason, message = refusal.Message });
        }
        catch (SchemaWriteRefusedException refusal)
        {
            return Results.Conflict(new { code = refusal.Code, feature = NotesModule.TagsFeature, reason = refusal.Message, message = refusal.Message });
        }
    }
}

/// <summary>
/// Where the feature accepts or serves data only version 2.0.0 holds: it asks the host's shared dormancy check before
/// anything is read or any row changes.
/// </summary>
/// <exception cref="SchemaDormancyRefusedException">The feature is dormant.</exception>
public sealed class NotesWithTags(ISchemaDormancyCheck check, NoteStore store)
{
    public async Task<IReadOnlyList<NoteWithTags>> ListAsync(CancellationToken cancellationToken = default)
    {
        await EnsureAvailableAsync(cancellationToken);
        return await store.ListWithTagsAsync(cancellationToken);
    }

    public async Task<NoteWithTags?> AddTagsAsync(string id, IEnumerable<string> tags, CancellationToken cancellationToken = default)
    {
        await EnsureAvailableAsync(cancellationToken);
        return await store.AddTagsAsync(id, tags, cancellationToken);
    }

    private async Task EnsureAvailableAsync(CancellationToken cancellationToken) =>
        await check.EnsureAvailableAsync(SchemaVersionRequirement.DeclaredBy(typeof(NotesWithTagsFeature)), NotesModule.TagsFeature, cancellationToken);
}
