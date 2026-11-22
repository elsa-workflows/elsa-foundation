using Elsa.Secrets.Nuplane.Extensions;
using Elsa.Secrets.Nuplane.Tests.Support;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nuplane;
using Nuplane.Feeds.Configuration;
using Nuplane.Reconciliation;
using Nuplane.Reconciliation.Models;
using Xunit;

namespace Elsa.Secrets.Nuplane.Tests;

/// <summary>
/// The whole path, against a feed that serves nothing without basic authentication: a host that composes
/// Nuplane and the Secrets read path together restores from it, a host that claims no <c>elsa</c> provider
/// refuses it by name without contacting it, and neither leaves the value anywhere.
/// </summary>
/// <remarks>
/// <para>
/// One container for Nuplane and for Secrets, because that is the requirement rather than a convenience:
/// Nuplane resolves <c>IEnumerable&lt;ISecretReferenceProvider&gt;</c> out of the container that composed
/// <c>AddNuplane</c>, so a provider registered anywhere else is never consulted. See
/// <c>docs/foundation-host-feeds.md</c>, "Feed credentials", for what that means for a host that composes
/// Nuplane on its own container and Secrets on a shell.
/// </para>
/// <para>
/// The feed's include pattern is a range rather than a single pin, so the cycle goes through version
/// enumeration — NuGet's own client, authenticating separately — before it acquires anything. A single pin
/// would skip that call and leave half the handshake untested.
/// </para>
/// </remarks>
public sealed class SecretsCredentialedFeedTests : IAsyncDisposable
{
    private const string PackageId = "Acme.Widgets";
    private const string Version = "1.4.2";
    private const string FeedName = "private-feed";
    private const string SecretName = "feed-token";

    /// <summary>
    /// A real, resolvable value — the point of this sentinel. The Cli's env-backed sentinel test can only
    /// use the variable *name*, because a restore composes no host DI and the value never resolves; here the
    /// provider hands Nuplane the value and it still has to appear on no stream.
    /// </summary>
    private const string Sentinel = "SENTINEL-ELSA-FEED-TOKEN-4b8e21";

    /// <summary>
    /// The stored secret, in the <c>user:password</c> shape Nuplane splits at the first colon. Chosen over a
    /// bare token so the test names both halves itself: a bare token travels under a placeholder user name
    /// that is Nuplane's own internal constant, which this assembly cannot see and should not restate.
    /// </summary>
    private const string SecretValue = $"{FeedUserName}:{Sentinel}";

    private const string FeedUserName = "deploy";

    private readonly string root = Directory.CreateTempSubdirectory("elsa-secrets-nuplane-feed-").FullName;
    private readonly CapturingLoggerProvider logs = new();
    private readonly TestNuGetFeedServer server =
        new(PackageId, Version, userName: FeedUserName, password: Sentinel);

    private readonly List<ServiceProvider> composed = [];

    private string InstallRoot => Path.Join(root, "packages");

    private string StateFile => Path.Join(root, "state", "store-state.json");

    public async ValueTask DisposeAsync()
    {
        foreach (var provider in composed)
            await provider.DisposeAsync();
        await server.DisposeAsync();
        Directory.Delete(root, recursive: true);
    }

    /// <summary>The acceptance case: the credential lives in the Secrets module and the feed authenticates.</summary>
    [Fact]
    public async Task A_feed_whose_credential_is_an_elsa_secret_is_restored_from_with_authentication()
    {
        var host = Compose(withCredentialProvider: true);
        await host.StoreAsync(SecretName, SecretValue);

        var run = await ReconcileAsync(host);

        Assert.False(run.IsDegraded, $"the cycle degraded: {string.Join(",", run.FailedPackages)}");
        Assert.Empty(run.FailedPackages);
        Assert.Equal(PackageId, Assert.Single(await NuplaneStore.ReadActivePackagesAsync(StateFile)).PackageId);
        // The server answers nothing at all without the header, so one counted download is itself proof the
        // secret reached the wire. Not asserted: that no request was ever refused — NuGet's own client waits
        // for the 401 challenge before attaching the credentials configured on its package source, so the
        // version-enumeration call legitimately gets one first.
        Assert.Equal(1, server.PackageDownloads);
        Assert.True(server.AuthorizedRequests > 0, "the feed was never contacted with credentials");
    }

    /// <summary>
    /// A host that composes no <c>elsa</c> provider refuses the feed by name, exactly as it refuses a
    /// reference to any other unregistered provider — and contacts it for nothing at all, not even to serve
    /// a package it could have served anonymously.
    /// </summary>
    [Fact]
    public async Task A_host_without_the_elsa_provider_refuses_the_feed_without_contacting_it()
    {
        var host = Compose(withCredentialProvider: false);
        await host.StoreAsync(SecretName, SecretValue);

        var run = await ReconcileAsync(host);

        Assert.True(run.IsDegraded, "a feed whose credential cannot be resolved must degrade the cycle");
        Assert.Equal(PackageId, Assert.Single(run.FailedPackages));
        Assert.Equal(0, server.Requests);
        Assert.Contains(logs.Entries, entry => entry.Contains(FeedName, StringComparison.Ordinal));
    }

    /// <summary>
    /// The same host with the provider composed but nothing stored under the name: the secret is absent
    /// rather than the provider, which Nuplane treats identically — the feed is refused, not contacted
    /// unauthenticated.
    /// </summary>
    [Fact]
    public async Task A_reference_to_a_secret_that_does_not_exist_refuses_the_feed_without_contacting_it()
    {
        var host = Compose(withCredentialProvider: true);

        var run = await ReconcileAsync(host);

        Assert.True(run.IsDegraded, "a feed whose credential cannot be resolved must degrade the cycle");
        Assert.Equal(PackageId, Assert.Single(run.FailedPackages));
        Assert.Equal(0, server.Requests);
    }

    /// <summary>
    /// The never-echo-a-value guarantee, with a value that really resolves: a successful cycle authenticates
    /// with the secret, and a refused one reports the feed, and neither puts the value into a log, a run
    /// result, or the state file the cycle writes.
    /// </summary>
    [Fact]
    public async Task A_resolvable_credential_value_appears_on_no_stream()
    {
        var resolving = Compose(withCredentialProvider: true);
        await resolving.StoreAsync(SecretName, SecretValue);
        var reported = new List<string> { Describe(await ReconcileAsync(resolving)) };

        // And once more with the secret revoked, so the refusal path is exercised while the value is still
        // sitting in the store: the message names the feed, never what it could not use.
        await resolving.RevokeAsync(SecretName);
        reported.Add(Describe(await ReconcileAsync(resolving)));

        Assert.DoesNotContain(Sentinel, logs.AllText, StringComparison.Ordinal);
        Assert.DoesNotContain(Sentinel, string.Join(Environment.NewLine, reported), StringComparison.Ordinal);
        Assert.DoesNotContain(Sentinel, await File.ReadAllTextAsync(StateFile), StringComparison.Ordinal);
        Assert.NotEmpty(logs.Entries);
    }

    private static string Describe(ReconciliationRunResult result) =>
        $"{result} {result.IsDegraded} {result.SkipReason} {result.ChangeSet} {string.Join(",", result.FailedPackages)}";

    private static Task<ReconciliationRunResult> ReconcileAsync(IServiceProvider host) =>
        host.GetRequiredService<IReconciliationService>().TriggerAsync(ReconciliationTrigger.Manual(), CancellationToken.None);

    private ServiceProvider Compose(bool withCredentialProvider)
    {
        var services = new ServiceCollection().AddSecretsReadPath();
        services.AddLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Trace);
            logging.AddProvider(logs);
        });
        services.AddNuplane(Configuration(), nuplane =>
        {
            AllowHttpLoopbackFeeds(nuplane.Services);
            if (withCredentialProvider)
                nuplane.Services.AddSecretsFeedCredentials();
        });

        var host = services.BuildServiceProvider();
        composed.Add(host);
        return host;
    }

    private IConfiguration Configuration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"Setup:Feeds:{FeedName}:ServiceIndex"] = server.ServiceIndexUri.AbsoluteUri,
                [$"Setup:Feeds:{FeedName}:Credentials"] = $"secrets://elsa/{SecretName}",
                [$"Setup:Feeds:{FeedName}:IncludePatterns:0"] = $"{PackageId} [1.0.0,2.0.0)",
                ["FeedResolution:PackageInstallRoot"] = InstallRoot,
                ["StoreRegistry:StateFilePath"] = StateFile
            })
            .Build();

    /// <summary>
    /// Drops the validator that requires every non-<c>file://</c> feed to be an HTTPS service index, so the
    /// feed can be the in-process <c>HttpListener</c>, which serves HTTP only.
    /// </summary>
    /// <remarks>
    /// Matched by implementation-type name because the validator is internal to Nuplane. <c>Single</c> rather
    /// than a filtered removal on purpose: if Nuplane renames or splits it, this throws rather than quietly
    /// leaving the HTTPS rule in place and reporting a configuration refusal as a credential failure.
    /// </remarks>
    private static void AllowHttpLoopbackFeeds(IServiceCollection services) =>
        services.Remove(services.Single(descriptor =>
            descriptor.ServiceType == typeof(IValidateOptions<FeedResolutionOptions>)
            && descriptor.ImplementationType?.Name == "FeedCredentialCompositeValidator"));
}
