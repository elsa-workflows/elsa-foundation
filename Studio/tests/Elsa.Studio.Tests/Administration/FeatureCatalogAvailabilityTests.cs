using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Elsa.Modularity.Core.Contracts;
using Elsa.Modularity.Core.Models;
using global::Nuplane.Abstractions.PackageStoreProtection;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Studio.Tests.Administration;

/// <summary>
/// While an enrolled store's reconciliation cycle holds its non-waiting root lock, Nuplane refuses the catalog's package read.
/// The Features endpoints answer that refusal with 503 and Retry-After, as the module registry does, instead of 500 (#5778).
/// </summary>
public sealed class FeatureCatalogAvailabilityTests
{
    [Fact]
    public async Task A_refused_package_read_answers_the_catalog_with_503_and_retry_after()
    {
        await using var host = await AdministrationHost.StartAsync(afterStudio: Busy);

        using var response = await host.Client.GetAsync("/modularity/features");

        await AssertUnavailableAsync(response);
    }

    [Fact]
    public async Task A_refused_package_read_answers_apply_with_503_and_retry_after()
    {
        await using var host = await AdministrationHost.StartAsync(afterStudio: Busy);

        using var response = await host.Client.PostAsJsonAsync("/modularity/features/apply",
            new JsonObject { ["revision"] = "any", ["features"] = new JsonArray() });

        await AssertUnavailableAsync(response);
    }

    private static void Busy(WebApplicationBuilder builder) => builder.Services.AddScoped<IFeatureCatalogContributor, BusyStoreContributor>();

    private static async Task AssertUnavailableAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.ServiceUnavailable, $"Answered {response.StatusCode}: {body}");
        Assert.Equal("1", response.Headers.RetryAfter?.Delta?.TotalSeconds.ToString());
        Assert.Equal("UnknownAuthority", (string?)JsonNode.Parse(body)!["reason"]);
    }

    /// <summary>Refuses as Nuplane does when the root lock is busy.</summary>
    private sealed class BusyStoreContributor : IFeatureCatalogContributor
    {
        public Task ContributeAsync(FeatureCatalogContributionContext context, CancellationToken cancellationToken = default) =>
            throw new PackageStoreAdmissionException(PackageStoreAdmissionReason.UnknownAuthority, "The required lock file 'root.lock' is busy.", null, null);
    }
}
