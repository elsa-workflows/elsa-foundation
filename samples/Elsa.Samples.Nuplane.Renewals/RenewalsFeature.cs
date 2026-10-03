using CShells.AspNetCore.Features;
using CShells.Features;
using Elsa.Primitives.Exceptions;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Samples.Nuplane.Renewals;

/// <summary>The body of <c>POST /demo/renewals</c>.</summary>
public sealed record RegisterRenewalRequest(string? PolicyReference);

/// <summary>
/// The base endpoints, present in both releases: register a renewal and list renewals. They know nothing of premium, so they work
/// against a database at any version this build reads.
/// </summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Samples")]
[ShellFeature(
    name: RenewalsModule.RenewalsFeature,
    DisplayName = "Renewals",
    Description = "Registers and lists renewals: POST and GET /demo/renewals.",
    DependsOn = new object[] { RenewalsModule.EntityFrameworkCoreFeature })]
public sealed class RenewalsFeature : IWebShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? _)
    {
        endpoints.MapGet(RenewalsModule.ReleasePath, () => Results.Ok(new
        {
            packageRelease = RenewalsModule.PackageRelease,
            schemaFamily = RenewalsModule.Family,
            readableVersions = RenewalsModule.Chain.ReadableVersions
        }));

        endpoints.MapPost(RenewalsModule.RenewalsPath, (RegisterRenewalRequest request, RenewalStore store, CancellationToken cancellationToken) =>
            string.IsNullOrWhiteSpace(request.PolicyReference)
                ? Task.FromResult(Results.BadRequest("A renewal needs a policy reference."))
                : RenewalsResults.ServeAsync(RenewalsModule.RenewalsFeature, async () => Results.Ok(await store.AddAsync(request.PolicyReference.Trim(), cancellationToken))));

        endpoints.MapGet(RenewalsModule.RenewalsPath, async (RenewalStore store, CancellationToken cancellationToken) =>
            Results.Ok(await store.ListAsync(cancellationToken)));
    }
}

/// <summary>What both features answer when the schema versions refuse an operation.</summary>
internal static class RenewalsResults
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
