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

namespace Elsa.Samples.Nuplane.Renewals;

/// <summary>The body of <c>POST /demo/renewals/with-premium</c>.</summary>
public sealed record RegisterPremiumRenewalRequest(string? PolicyReference, decimal? ProposedPremium);

/// <summary>
/// The feature only release 1.1.0 has. Its operations ask the shared dormancy check before reading or writing, so a host
/// with the additive migration pending answers 409 while the base registration and list endpoints keep working.
/// </summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Samples")]
[ShellFeature(
    name: RenewalsModule.ProposedPremiumFeature,
    DisplayName = "Renewals with proposed premium",
    Description = "Registers and lists renewals with an optional proposed premium at /demo/renewals/with-premium. Dormant until schema version 2.0.0 is finalized.",
    DependsOn = new object[] { RenewalsModule.RenewalsFeature })]
[RequiresSchemaVersion(RenewalsModule.Family, RenewalsModule.ProposedPremiumVersion)]
public sealed class RenewalPremiumFeature : IWebShellFeature
{
    public void ConfigureServices(IServiceCollection services) => services.AddScoped<RenewalPremium>();

    public void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment)
    {
        static Task<IResult> Serve(Func<Task<IResult>> operation) => RenewalsResults.ServeAsync(RenewalsModule.ProposedPremiumFeature, operation);

        endpoints.MapGet(RenewalsModule.RenewalsPath + "/with-premium", (RenewalPremium premium, CancellationToken cancellationToken) =>
            Serve(async () => Results.Ok(await premium.ListAsync(cancellationToken))));

        endpoints.MapPost(RenewalsModule.RenewalsPath + "/with-premium", (RegisterPremiumRenewalRequest request, RenewalPremium premium, CancellationToken cancellationToken) =>
            string.IsNullOrWhiteSpace(request.PolicyReference)
                ? Task.FromResult(Results.BadRequest("A renewal needs a policy reference."))
                : Serve(async () => Results.Ok(await premium.RegisterAsync(request.PolicyReference.Trim(), request.ProposedPremium, cancellationToken))));
    }
}

/// <summary>Premium-aware registration and listing. Every operation checks dormancy before it touches the store.</summary>
public sealed class RenewalPremium(ISchemaDormancyCheck check, RenewalStore store)
{
    public async Task<IReadOnlyList<RenewalWithPremium>> ListAsync(CancellationToken cancellationToken = default)
    {
        await EnsureAvailableAsync(cancellationToken);
        return await store.ListWithPremiumAsync(cancellationToken);
    }

    public async Task<RenewalWithPremium> RegisterAsync(string policyReference, decimal? proposedPremium, CancellationToken cancellationToken = default)
    {
        await EnsureAvailableAsync(cancellationToken);
        return await store.AddAsync(policyReference, proposedPremium, cancellationToken);
    }

    private async Task EnsureAvailableAsync(CancellationToken cancellationToken) =>
        await check.EnsureAvailableAsync(SchemaVersionRequirement.DeclaredBy(typeof(RenewalPremiumFeature)), RenewalsModule.ProposedPremiumFeature, cancellationToken);
}
