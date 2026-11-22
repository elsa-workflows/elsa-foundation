using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Versioning.Calculator;
using Elsa.Versioning.Publisher.Tests.Support;

namespace Elsa.Versioning.Publisher.Tests;

public sealed class Exact20RecoveryCommandTests
{
    [Fact]
    public async Task Read_only_preflight_never_prepares_the_write_adapter_or_mutates_state()
    {
        using var fixture = await RecoveryFixture.CreateAsync();

        var report = await fixture.RunAsync(execute: false);

        Assert.True(report.Succeeded, report.Message);
        Assert.True(report.ReadOnly);
        Assert.Empty(fixture.Feed.PushedPackageIds);
        Assert.Equal(0, fixture.Feed.PrepareCount);
        Assert.Equal(0, fixture.State.WriteAttempts);
        Assert.Equal(138, fixture.Feed.ListCalls);
        Assert.Equal(30, fixture.GitHub.RunCountCalls.Count);
        Assert.Equal(
            from workflowId in new long[] { 307637788, 307637495, 368711260 }
            from status in Exact20RecoveryCommand.NonterminalStatusNames
            select (workflowId, status),
            fixture.GitHub.RunCountCalls.Take(15));
        Assert.Equal("4.0.1-preview.9", PublishedVersions.Parse(Encoding.UTF8.GetBytes(report.ProposedRecord), "read-only proposal").Find(fixture.RecoveryIds[0])!.Version.ToString());
        Assert.Equal("4.0.0-preview.9", fixture.State.CurrentRecord.Find(fixture.RecoveryIds[0])!.Version.ToString());
    }

    [Fact]
    public async Task Execution_uploads_the_hash_verified_dependency_waves_once_and_writes_exact_confirmed_rows()
    {
        using var fixture = await RecoveryFixture.CreateAsync();

        var report = await fixture.RunAsync(execute: true);

        Assert.True(report.Succeeded, report.Message);
        Assert.False(report.ReadOnly);
        Assert.Equal(20, fixture.Feed.PushedPackageIds.Count);
        Assert.Equal(1, fixture.Feed.PrepareCount);
        Assert.Equal(1, fixture.State.WriteAttempts);
        Assert.Equal(1, fixture.State.SuccessfulWrites);
        Assert.Equal(fixture.RecoveryIds, fixture.Feed.PushedPackageIds);
        Assert.Equal(fixture.Pins.SourceCommit, fixture.State.CurrentRecord.LastPublishCommit);
        foreach (var id in fixture.RecoveryIds)
        {
            var row = fixture.State.CurrentRecord.Find(id)!;
            Assert.Equal("4.0.1-preview.9", row.Version.ToString());
            Assert.Equal(fixture.Pins.SourceCommit, row.Commit);
        }
    }

    [Theory]
    [InlineData("higher-version")]
    [InlineData("candidate-present")]
    public async Task New_candidate_drift_after_first_upload_stops_before_second_put_and_records_only_first(string drift)
    {
        using var fixture = await RecoveryFixture.CreateAsync();
        var secondId = fixture.RecoveryIds[1];
        if (drift == "higher-version")
        {
            fixture.Feed.VersionsOverride = packageId => fixture.Feed.PushedPackageIds.Count > 0 && packageId == secondId
                ? ["4.0.0-preview.9", "4.0.0-preview.10"]
                : null;
        }
        else
        {
            fixture.Feed.PreflightResponse = packageId => fixture.Feed.PushedPackageIds.Count > 0 && packageId == secondId
                ? new Exact20PackageResponse(200, fixture.Feed.PinnedPackageBytes(secondId))
                : null;
        }

        var report = await fixture.RunAsync(execute: true);

        Assert.False(report.Succeeded);
        Assert.Equal([fixture.RecoveryIds[0]], report.ConfirmedPackageIds);
        Assert.Equal(19, report.RemainingPackageIds.Count);
        Assert.Equal([fixture.RecoveryIds[0]], fixture.Feed.PushedPackageIds);
        Assert.Equal(1, fixture.State.WriteAttempts);
        Assert.Equal(1, fixture.State.SuccessfulWrites);
        Assert.Equal("4.0.1-preview.9", fixture.State.CurrentRecord.Find(fixture.RecoveryIds[0])!.Version.ToString());
        Assert.Equal("4.0.0-preview.9", fixture.State.CurrentRecord.Find(secondId)!.Version.ToString());
    }

    [Fact]
    public async Task Ambiguous_put_with_exact_get_confirms_only_that_package_and_stops_the_plan()
    {
        using var fixture = await RecoveryFixture.CreateAsync();
        fixture.Feed.NextPushStatus = PushStatus.Failed;

        var report = await fixture.RunAsync(execute: true);

        Assert.False(report.Succeeded);
        Assert.Single(fixture.Feed.PushedPackageIds);
        Assert.Equal(fixture.RecoveryIds[0], fixture.Feed.PushedPackageIds[0]);
        Assert.Equal([fixture.RecoveryIds[0]], report.ConfirmedPackageIds);
        Assert.Equal(19, report.RemainingPackageIds.Count);
        Assert.Equal(1, fixture.State.SuccessfulWrites);
        Assert.Equal("4.0.1-preview.9", fixture.State.CurrentRecord.Find(fixture.RecoveryIds[0])!.Version.ToString());
        Assert.Equal("4.0.0-preview.9", fixture.State.CurrentRecord.Find(fixture.RecoveryIds[1])!.Version.ToString());
    }

    [Fact]
    public async Task Wrong_post_put_bytes_are_never_added_to_the_state_record()
    {
        using var fixture = await RecoveryFixture.CreateAsync();
        fixture.Feed.PostPushResponse = _ => new Exact20PackageResponse(200, Encoding.UTF8.GetBytes("wrong package bytes"));

        var report = await fixture.RunAsync(execute: true);

        Assert.False(report.Succeeded);
        Assert.Single(fixture.Feed.PushedPackageIds);
        Assert.Empty(report.ConfirmedPackageIds);
        Assert.Equal(0, fixture.State.WriteAttempts);
        Assert.Equal("4.0.0-preview.9", fixture.State.CurrentRecord.Find(fixture.RecoveryIds[0])!.Version.ToString());
        Assert.Contains("differ from the exact pinned artifact", report.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cancellation_after_one_exact_confirmation_retains_that_proposal_without_another_put()
    {
        using var fixture = await RecoveryFixture.CreateAsync();
        fixture.GitHub.CancelAfterFirstPush = true;

        var report = await fixture.RunAsync(execute: true);

        Assert.False(report.Succeeded);
        Assert.Single(fixture.Feed.PushedPackageIds);
        Assert.Equal([fixture.RecoveryIds[0]], report.ConfirmedPackageIds);
        Assert.Equal(20, report.RemainingPackageIds.Count + report.ConfirmedPackageIds.Count);
        Assert.Equal(0, fixture.State.WriteAttempts);
        Assert.Contains("cancelled", report.Message, StringComparison.OrdinalIgnoreCase);
        var proposal = PublishedVersions.Parse(Encoding.UTF8.GetBytes(report.ProposedRecord), "test proposal");
        Assert.Equal("4.0.1-preview.9", proposal.Find(fixture.RecoveryIds[0])!.Version.ToString());
        Assert.Equal("4.0.0-preview.9", proposal.Find(fixture.RecoveryIds[1])!.Version.ToString());
    }

    [Fact]
    public async Task A_state_tip_move_immediately_before_write_is_not_overwritten()
    {
        using var fixture = await RecoveryFixture.CreateAsync();
        fixture.State.BeforeWrite = state => state.Tip = new string('e', 40);

        var report = await fixture.RunAsync(execute: true);

        Assert.False(report.Succeeded);
        Assert.Equal(20, report.ConfirmedPackageIds.Count);
        Assert.Equal(1, fixture.State.WriteAttempts);
        Assert.Equal(0, fixture.State.SuccessfulWrites);
        Assert.Equal(new string('e', 40), fixture.State.Tip);
        Assert.Contains("compare-and-swap", report.Message, StringComparison.OrdinalIgnoreCase);
        var proposal = PublishedVersions.Parse(Encoding.UTF8.GetBytes(report.ProposedRecord), "test proposal");
        Assert.All(fixture.RecoveryIds, id => Assert.Equal("4.0.1-preview.9", proposal.Find(id)!.Version.ToString()));
    }

    [Fact]
    public async Task Accepted_state_write_with_wrong_remote_tip_is_not_reported_as_delivered()
    {
        using var fixture = await RecoveryFixture.CreateAsync();
        fixture.State.ReadbackTipOverride = new string('f', 40);

        var report = await fixture.RunAsync(execute: true);

        Assert.False(report.Succeeded);
        Assert.Null(report.WrittenStateCommit);
        Assert.Equal(20, report.ConfirmedPackageIds.Count);
        Assert.Empty(report.RemainingPackageIds);
        Assert.Equal(1, fixture.State.WriteAttempts);
        Assert.Equal(1, fixture.State.SuccessfulWrites);
        AssertCanonicalConfirmedProposal(fixture, report);
        Assert.Contains("remote tip", report.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Accepted_state_write_with_wrong_state_blob_is_not_reported_as_delivered()
    {
        using var fixture = await RecoveryFixture.CreateAsync();
        fixture.State.CorruptReadbackBlobAfterWrite = true;

        var report = await fixture.RunAsync(execute: true);

        Assert.False(report.Succeeded);
        Assert.Null(report.WrittenStateCommit);
        Assert.Equal(20, report.ConfirmedPackageIds.Count);
        Assert.Equal(1, fixture.State.WriteAttempts);
        Assert.Equal(1, fixture.State.SuccessfulWrites);
        AssertCanonicalConfirmedProposal(fixture, report);
        Assert.Contains("bytes did not read back exactly", report.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Accepted_state_write_with_lost_response_is_reported_as_unconfirmed_without_retry()
    {
        using var fixture = await RecoveryFixture.CreateAsync();
        fixture.State.ThrowAfterAccept = true;

        var report = await fixture.RunAsync(execute: true);

        Assert.False(report.Succeeded);
        Assert.Null(report.WrittenStateCommit);
        Assert.Equal(20, report.ConfirmedPackageIds.Count);
        Assert.Equal(1, fixture.State.WriteAttempts);
        Assert.Equal(1, fixture.State.SuccessfulWrites);
        AssertCanonicalConfirmedProposal(fixture, report);
        Assert.Contains("no retry was attempted", report.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Manifest_digest_mismatch_stops_before_any_feed_read_or_write()
    {
        using var fixture = await RecoveryFixture.CreateAsync();
        await File.AppendAllTextAsync(fixture.ManifestPath, " ");

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.RunAsync(execute: true));

        Assert.Equal(0, fixture.Feed.ListCalls);
        Assert.Empty(fixture.Feed.PushedPackageIds);
        Assert.Equal(0, fixture.State.WriteAttempts);
    }

    [Fact]
    public async Task State_blob_digest_mismatch_stops_before_feed_reads_or_writes()
    {
        using var fixture = await RecoveryFixture.CreateAsync();
        fixture.State.ReadbackBlobOverride = Encoding.UTF8.GetBytes("{}");

        var report = await fixture.RunAsync(execute: true);

        Assert.False(report.Succeeded);
        Assert.Equal(0, fixture.Feed.ListCalls);
        Assert.Empty(fixture.Feed.PushedPackageIds);
        Assert.Equal(0, fixture.State.WriteAttempts);
        Assert.Contains("pinned SHA-256", report.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("digest")]
    [InlineData("expired")]
    public async Task Invalid_original_artifact_metadata_stops_before_feed_reads_or_writes(string invalidPart)
    {
        using var fixture = await RecoveryFixture.CreateAsync();
        var artifact = fixture.GitHub.GetPinnedArtifact();
        fixture.GitHub.ArtifactOverride = invalidPart switch
        {
            "digest" => artifact with { Digest = "sha256:" + new string('f', 64) },
            "expired" => artifact with { Expired = true },
            _ => throw new ArgumentOutOfRangeException(nameof(invalidPart))
        };

        var report = await fixture.RunAsync(execute: true);

        Assert.False(report.Succeeded);
        Assert.Equal(0, fixture.Feed.ListCalls);
        Assert.Empty(fixture.Feed.PushedPackageIds);
        Assert.Equal(0, fixture.State.WriteAttempts);
        Assert.Contains("artifact metadata", report.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("attempt")]
    [InlineData("run-id")]
    public async Task Wrong_own_run_identity_stops_before_feed_reads_or_writes(string invalidPart)
    {
        using var fixture = await RecoveryFixture.CreateAsync();
        var run = fixture.GitHub.GetCurrentRun();
        fixture.GitHub.CurrentRunOverride = invalidPart switch
        {
            "attempt" => run with { Attempt = 2 },
            "run-id" => run with { Id = run.Id + 1 },
            _ => throw new ArgumentOutOfRangeException(nameof(invalidPart))
        };

        var report = await fixture.RunAsync(execute: true);

        Assert.False(report.Succeeded);
        Assert.Equal(0, fixture.Feed.ListCalls);
        Assert.Empty(fixture.Feed.PushedPackageIds);
        Assert.Equal(0, fixture.State.WriteAttempts);
        Assert.Contains("current run", report.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Active_publisher_workflow_stops_before_feed_reads_or_writes()
    {
        using var fixture = await RecoveryFixture.CreateAsync();
        fixture.GitHub.WorkflowStateOverride = "active";

        var report = await fixture.RunAsync(execute: true);

        Assert.False(report.Succeeded);
        Assert.Equal(0, fixture.Feed.ListCalls);
        Assert.Empty(fixture.Feed.PushedPackageIds);
        Assert.Equal(0, fixture.State.WriteAttempts);
        Assert.Contains("not disabled_manually", report.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Nonzero_or_noninteger_workflow_count_stops_before_feed_reads()
    {
        using var fixture = await RecoveryFixture.CreateAsync();
        fixture.GitHub.RunCountOverride = (workflowId, status) =>
            workflowId == 307637495 && status == "queued" ? new Exact20RunCount(false, -1) : null;

        var report = await fixture.RunAsync(execute: false);

        Assert.False(report.Succeeded);
        Assert.Equal(0, fixture.Feed.ListCalls);
        Assert.Empty(fixture.Feed.PushedPackageIds);
        Assert.Equal(0, fixture.State.WriteAttempts);
    }

    [Fact]
    public async Task A_candidate_get_that_is_not_exact_404_stops_before_any_put()
    {
        using var fixture = await RecoveryFixture.CreateAsync();
        fixture.Feed.PreflightResponse = packageId => packageId == fixture.RecoveryIds[0]
            ? new Exact20PackageResponse(401, null)
            : null;

        var report = await fixture.RunAsync(execute: true);

        Assert.False(report.Succeeded);
        Assert.Empty(fixture.Feed.PushedPackageIds);
        Assert.Equal(0, fixture.State.WriteAttempts);
    }

    [Fact]
    public async Task A_newer_prerelease_in_the_feed_index_refuses_the_candidate_floor()
    {
        using var fixture = await RecoveryFixture.CreateAsync();
        fixture.Feed.AdditionalFeedVersion = "4.0.0-preview.10";

        var report = await fixture.RunAsync(execute: true);

        Assert.False(report.Succeeded);
        Assert.Empty(fixture.Feed.PushedPackageIds);
        Assert.Equal(0, fixture.State.WriteAttempts);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("author")]
    [InlineData("issue")]
    [InlineData("head")]
    [InlineData("manifest")]
    public async Task Invalid_owner_receipt_stops_before_feed_preflight_or_any_put(string invalidPart)
    {
        using var fixture = await RecoveryFixture.CreateAsync();
        var comment = fixture.GitHub.ApprovalComment;
        switch (invalidPart)
        {
            case "missing": fixture.GitHub.MissingApprovalComment = true; break;
            case "author": fixture.GitHub.ApprovalAuthor = "someone-else"; break;
            case "issue": fixture.GitHub.ApprovalComment = comment with { IssueUrl = "https://api.github.com/repos/elsa-workflows/elsa-foundation/issues/2504" }; break;
            case "head": fixture.GitHub.ApprovalComment = comment with { Body = comment.Body.Replace(fixture.Pins.SourceCommit, new string('f', 40), StringComparison.Ordinal) }; break;
            case "manifest": fixture.GitHub.ApprovalComment = comment with { Body = comment.Body.Replace(fixture.Pins.ManifestSha256, new string('f', 64), StringComparison.Ordinal) }; break;
        }

        var report = await fixture.RunAsync(execute: true);

        Assert.False(report.Succeeded);
        Assert.Equal(0, fixture.Feed.ListCalls);
        Assert.Equal(0, fixture.Feed.PrepareCount);
        Assert.Empty(fixture.Feed.PushedPackageIds);
        Assert.Equal(0, fixture.Feed.ListCalls);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("\"0\"")]
    [InlineData("0.0")]
    public async Task GitHub_run_counts_reject_boolean_string_and_float_json_values(string rawCount)
    {
        using var http = new HttpClient(new StubHttpHandler(_ => Json($"{{\"total_count\":{rawCount}}}")));
        var github = new Exact20GitHub(http, "test-token");

        var result = await github.GetNonterminalRunCountAsync(368711260, "queued", CancellationToken.None);

        Assert.False(result.IsInteger);
        Assert.NotEqual(0, result.Value);
    }

    [Fact]
    public async Task GitHub_artifact_redirect_never_forwards_the_api_token_to_storage()
    {
        var requests = new List<(string Url, string? Authorization)>();
        using var http = new HttpClient(new StubHttpHandler(request =>
        {
            requests.Add((request.RequestUri!.ToString(), request.Headers.Authorization?.ToString()));
            return requests.Count == 1
                ? new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri("https://objects.githubusercontent.com/signed/file") } }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) };
        }));
        var github = new Exact20GitHub(http, "test-token");
        var destination = Path.Join(Path.GetTempPath(), $"exact20-artifact-{Guid.NewGuid():N}.zip");

        try
        {
            await github.DownloadArtifactAsync(123, destination, 16, CancellationToken.None);

            Assert.Equal(2, requests.Count);
            Assert.Equal("Bearer test-token", requests[0].Authorization);
            Assert.Null(requests[1].Authorization);
            Assert.Equal([1, 2, 3], await File.ReadAllBytesAsync(destination));
        }
        finally
        {
            File.Delete(destination);
        }
    }

    [Fact]
    public async Task Artifact_redirect_to_an_unapproved_host_is_not_followed()
    {
        var requests = 0;
        using var http = new HttpClient(new StubHttpHandler(_ =>
        {
            requests++;
            return new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri("https://attacker.example/fake") } };
        }));
        var github = new Exact20GitHub(http, "test-token");
        var destination = Path.Join(Path.GetTempPath(), $"exact20-artifact-{Guid.NewGuid():N}.zip");

        await Assert.ThrowsAsync<InvalidOperationException>(() => github.DownloadArtifactAsync(123, destination, 16, CancellationToken.None));

        Assert.Equal(1, requests);
        Assert.False(File.Exists(destination));
    }

    [Fact]
    public void Recovery_feed_rejects_a_different_service_index_even_on_feedz()
    {
        using var http = new HttpClient(new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));

        Assert.Throws<ArgumentException>(() => new NuGetFeed(
            http,
            new Uri("https://f.feedz.io/elsa-workflows/elsa-4/other/index.json"),
            string.Empty,
            requireFeedzResourceHost: true,
            suppressResponseBodies: true));
    }

    [Fact]
    public async Task Recovery_feed_rejects_a_same_host_resource_outside_the_incident_feed_path()
    {
        using var http = new HttpClient(new StubHttpHandler(_ => Json("""
            {"resources":[
              {"@type":"PackagePublish/2.0.0","@id":"https://f.feedz.io/elsa-workflows/elsa-4/nugetevil/v2/package"},
              {"@type":"PackageBaseAddress/3.0.0","@id":"https://f.feedz.io/elsa-workflows/elsa-4/nugetevil/v3/flat2/"}
            ]}
            """)));
        var feed = new NuGetFeed(http, new Uri(Exact20RecoveryPins.FeedIndex), string.Empty, requireFeedzResourceHost: true, suppressResponseBodies: true);

        await Assert.ThrowsAsync<FeedException>(() => feed.ListVersionsAsync("RECOVER00", CancellationToken.None));
    }

    [Fact]
    public async Task Recovery_feed_rejects_an_unexpected_nested_resource_path()
    {
        using var http = new HttpClient(new StubHttpHandler(_ => Json("""
            {"resources":[
              {"@type":"PackagePublish/2.0.0","@id":"https://f.feedz.io/elsa-workflows/elsa-4/nuget"},
              {"@type":"PackageBaseAddress/3.0.0","@id":"https://f.feedz.io/elsa-workflows/elsa-4/nuget/v3/flat2/"}
            ]}
            """)));
        var feed = new NuGetFeed(http, new Uri(Exact20RecoveryPins.FeedIndex), string.Empty, requireFeedzResourceHost: true, suppressResponseBodies: true);

        await Assert.ThrowsAsync<FeedException>(() => feed.ListVersionsAsync("RECOVER00", CancellationToken.None));
    }

    [Fact]
    public async Task Recovery_feed_accepts_the_observed_exact_package_publish_resource_uri()
    {
        var requests = new List<(HttpMethod Method, string Url)>();
        using var http = new HttpClient(new StubHttpHandler(request =>
        {
            requests.Add((request.Method, request.RequestUri!.ToString()));
            return request.Method == HttpMethod.Get
                ? Json("""
                    {"resources":[
                      {"@type":"PackagePublish/2.0.0","@id":"https://f.feedz.io/elsa-workflows/elsa-4/nuget"},
                      {"@type":"PackageBaseAddress/3.0.0","@id":"https://f.feedz.io/elsa-workflows/elsa-4/nuget/v3/packages/"}
                    ]}
                    """)
                : new HttpResponseMessage(HttpStatusCode.Created);
        }));
        var feed = new NuGetFeed(http, new Uri(Exact20RecoveryPins.FeedIndex), "test-key", requireFeedzResourceHost: true, suppressResponseBodies: true);
        var packagePath = Path.Join(Path.GetTempPath(), $"exact20-feed-{Guid.NewGuid():N}.nupkg");

        try
        {
            await File.WriteAllBytesAsync(packagePath, [1, 2, 3]);
            var result = await feed.PushAsync(packagePath, CancellationToken.None);

            Assert.Equal(PushStatus.Pushed, result.Status);
            Assert.Equal(2, requests.Count);
            Assert.Equal(HttpMethod.Put, requests[1].Method);
            Assert.Equal("https://f.feedz.io/elsa-workflows/elsa-4/nuget", requests[1].Url);
        }
        finally
        {
            File.Delete(packagePath);
        }
    }

    private static HttpResponseMessage Json(string content) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(content, Encoding.UTF8, "application/json")
    };

    private static void AssertCanonicalConfirmedProposal(RecoveryFixture fixture, Exact20RecoveryReport report)
    {
        var proposed = PublishedVersions.Parse(Encoding.UTF8.GetBytes(report.ProposedRecord), "test proposal");
        Assert.Equal(proposed.Serialize(), report.ProposedRecord);
        Assert.All(fixture.RecoveryIds, id => Assert.Equal("4.0.1-preview.9", proposed.Find(id)!.Version.ToString()));
    }

    private sealed class StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(response(request));
    }

    private sealed class RecoveryFixture : IDisposable
    {
        private const long ArtifactId = 101;
        private const long RunId = 202;
        private const long WorkflowId = 303;
        private const long RepositoryId = 404;
        private const long CurrentRunId = 505;
        private const long ApprovalCommentId = 606;
        private const string StateTip = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        private const string StateRowCommit = "dddddddddddddddddddddddddddddddddddddddd";

        private readonly string directory;
        private readonly string eventPath;
        public string ManifestPath { get; }

        public string SourceCommit { get; } = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        public IReadOnlyList<string> RecoveryIds { get; }
        public Exact20RecoveryPins Pins { get; }
        public FakeGitHub GitHub { get; }
        public FakeFeed Feed { get; }
        public FakePublishState State { get; }
        public Exact20RecoveryCommand Command { get; }

        private RecoveryFixture(
            string directory,
            string eventPath,
            string manifestPath,
            IReadOnlyList<string> recoveryIds,
            Exact20RecoveryPins pins,
            FakeGitHub github,
            FakeFeed feed,
            FakePublishState state,
            Exact20RecoveryCommand command)
        {
            this.directory = directory;
            this.eventPath = eventPath;
            ManifestPath = manifestPath;
            RecoveryIds = recoveryIds;
            Pins = pins;
            GitHub = github;
            Feed = feed;
            State = state;
            Command = command;
        }

        public static async Task<RecoveryFixture> CreateAsync()
        {
            var directory = Path.Join(Path.GetTempPath(), $"exact20-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path.Join(directory, "tools", "versioning", "recovery"));
            var eventPath = Path.Join(directory, "workflow-event.json");
            var artifactPath = Path.Join(directory, "original-artifact.zip");
            var sourceCommit = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
            var expectedCommit = StateRowCommit;
            var recoveryIds = Enumerable.Range(0, 20).Select(index => $"RECOVER{index:00}").ToArray();
            var packageBytes = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            var rows = new List<Exact20ManifestPackage>(138);

            for (var index = 0; index < 138; index++)
            {
                var isRecovery = index < recoveryIds.Length;
                var packageId = isRecovery ? recoveryIds[index] : $"CORE{index - recoveryIds.Length:000}";
                var priorVersion = "4.0.0-preview.9";
                var version = isRecovery ? "4.0.1-preview.9" : priorVersion;
                IEnumerable<(string Id, string Range)> dependencies = index switch
                {
                    >= 12 and < 18 => [(recoveryIds[index % 6], "[4.0.1-preview.9, 5.0.0)")],
                    18 => [(recoveryIds[12], "[4.0.1-preview.9, 5.0.0)")],
                    19 => [(recoveryIds[13], "[4.0.1-preview.9, 5.0.0)")],
                    _ => []
                };
                var bytes = SyntheticPackages.Bytes(packageId, version, "sha256:exact20-fixture", sourceCommit, dependencies);
                packageBytes.Add(packageId, bytes);
                rows.Add(new Exact20ManifestPackage
                {
                    PackageId = packageId,
                    Version = version,
                    Sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)),
                    Recover = isRecovery,
                    ExpectedStateVersion = priorVersion,
                    ExpectedStateCommit = expectedCommit
                });
            }

            await using (var file = new FileStream(artifactPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
            {
                foreach (var (id, bytes) in packageBytes)
                {
                    using var stream = archive.CreateEntry($"{id}.nupkg").Open();
                    await stream.WriteAsync(bytes);
                }
            }

            var artifactBytes = await File.ReadAllBytesAsync(artifactPath);
            var artifactSha = Convert.ToHexStringLower(SHA256.HashData(artifactBytes));
            var record = new PublishedVersions(sourceCommit, rows.Select(row => new PublishedPackage(
                row.PackageId!,
                PackageVersionNumber.Parse(row.ExpectedStateVersion!, "fixture state"),
                row.ExpectedStateCommit!)));
            var stateBytes = Encoding.UTF8.GetBytes(record.Serialize());
            var stateSha = Convert.ToHexStringLower(SHA256.HashData(stateBytes));
            var manifest = new Exact20Manifest
            {
                SchemaVersion = 1,
                SourceCommit = sourceCommit,
                ArtifactId = ArtifactId,
                RunId = RunId,
                WorkflowId = WorkflowId,
                RepositoryId = RepositoryId,
                ArtifactSha256 = artifactSha,
                StateCommit = StateTip,
                StateSha256 = stateSha,
                Packages = rows
            };
            var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest);
            var manifestSha = Convert.ToHexStringLower(SHA256.HashData(manifestBytes));
            var manifestPath = Path.Join(directory, "tools", "versioning", "recovery", "merge245-exact20.json");
            await File.WriteAllBytesAsync(manifestPath, manifestBytes);
            var pins = new Exact20RecoveryPins(
                1,
                ArtifactId,
                RunId,
                WorkflowId,
                RepositoryId,
                sourceCommit,
                artifactSha,
                StateTip,
                stateSha,
                "fixture-artifact",
                manifestSha,
                138,
                20);
            var state = new FakePublishState(StateTip, record, stateBytes);
            var localGit = new FakeLocalGit(sourceCommit, state);
            var feed = new FakeFeed(rows, packageBytes);
            var github = new FakeGitHub(pins, artifactPath)
            {
                ApprovalComment = new Exact20ApprovalComment(
                    ApprovalCommentId,
                    Exact20RecoveryPins.ApprovalIssueUrl,
                    Exact20RecoveryPins.ApprovalAuthor,
                    $"{Exact20RecoveryPins.ApprovalLine}\nmanifest_sha256={manifestSha}\nworkflow_commit={sourceCommit}",
                    DateTimeOffset.Parse("2026-10-12T10:00:00Z"),
                    DateTimeOffset.Parse("2026-10-12T10:00:00Z"))
            };
            var output = new StringWriter();
            var command = new Exact20RecoveryCommand(
                pins,
                github,
                feed,
                state,
                localGit,
                output,
                (_, _) => Task.CompletedTask);
            var fixture = new RecoveryFixture(directory, eventPath, manifestPath, recoveryIds, pins, github, feed, state, command);
            github.CancelIfAfterPush = () => github.CancelAfterFirstPush && feed.PushedPackageIds.Count > 0;
            await File.WriteAllTextAsync(eventPath, "{}");
            return fixture;
        }

        public Task<Exact20RecoveryReport> RunAsync(bool execute)
        {
            if (!execute)
                return Command.RunAsync(new Exact20RunOptions(directory, false, null, null, new Dictionary<string, string?>(), null));

            var eventPayload = JsonSerializer.Serialize(new
            {
                inputs = new
                {
                    execute = "true",
                    confirmation = Exact20RecoveryPins.Confirmation,
                    approval_comment_id = ApprovalCommentId.ToString(System.Globalization.CultureInfo.InvariantCulture)
                }
            });
            File.WriteAllText(eventPath, eventPayload);
            var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["GITHUB_ACTIONS"] = "true",
                ["GITHUB_EVENT_NAME"] = "workflow_dispatch",
                ["GITHUB_REF"] = Exact20RecoveryPins.MainRef,
                ["GITHUB_WORKFLOW"] = "Exact20 recovery",
                ["GITHUB_JOB"] = "recover",
                ["GITHUB_WORKFLOW_REF"] = $"{Exact20RecoveryPins.Repository}/{Exact20RecoveryPins.RecoveryWorkflowPath}@{Exact20RecoveryPins.MainRef}",
                ["GITHUB_WORKFLOW_SHA"] = SourceCommit,
                ["GITHUB_REPOSITORY"] = Exact20RecoveryPins.Repository,
                ["GITHUB_RUN_ATTEMPT"] = "1",
                ["GITHUB_RUN_ID"] = CurrentRunId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["GITHUB_SHA"] = SourceCommit,
                ["GITHUB_EVENT_PATH"] = eventPath
            };
            return Command.RunAsync(new Exact20RunOptions(directory, true, Exact20RecoveryPins.Confirmation, ApprovalCommentId, environment, null));
        }

        public void Dispose()
        {
            try { Directory.Delete(directory, recursive: true); }
            catch (DirectoryNotFoundException) { }
        }

        public sealed class FakeGitHub(Exact20RecoveryPins pins, string artifactPath) : IExact20GitHub
        {
            private const long CurrentRunId = 505;
            public bool CancelAfterFirstPush { get; set; }
            public Func<bool>? CancelIfAfterPush { get; set; }
            public Exact20WorkflowRun? CurrentRunOverride { get; set; }
            public Exact20Artifact? ArtifactOverride { get; set; }
            public string ApprovalAuthor { get; set; } = Exact20RecoveryPins.ApprovalAuthor;
            public bool MissingApprovalComment { get; set; }
            public string WorkflowStateOverride { get; set; } = "disabled_manually";
            public Exact20ApprovalComment ApprovalComment { get; set; } = new(606, Exact20RecoveryPins.ApprovalIssueUrl, Exact20RecoveryPins.ApprovalAuthor, "", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
            public Func<long, string, Exact20RunCount?>? RunCountOverride { get; set; }
            public List<(long WorkflowId, string Status)> RunCountCalls { get; } = [];

            public Task<string> GetMainShaAsync(CancellationToken cancellationToken)
            {
                if (CancelIfAfterPush?.Invoke() == true)
                    throw new OperationCanceledException("cancel after one exact package confirmation");
                return Task.FromResult(pins.SourceCommit);
            }

            public Exact20WorkflowRun GetCurrentRun() => new(CurrentRunId, 999, Exact20RecoveryPins.RecoveryWorkflowPath, "workflow_dispatch", "in_progress", null, 1, "main", pins.SourceCommit, pins.RepositoryId, pins.RepositoryId);

            public Exact20Artifact GetPinnedArtifact() => new(
                pins.ArtifactId,
                pins.ArtifactName,
                false,
                new FileInfo(artifactPath).Length,
                DateTimeOffset.UtcNow.AddHours(1),
                $"sha256:{pins.ArtifactSha256}",
                $"{Exact20RecoveryPins.RepositoryApi}actions/artifacts/{pins.ArtifactId}/zip",
                pins.RunId,
                pins.RepositoryId,
                pins.RepositoryId,
                "main",
                pins.SourceCommit);

            public Task<Exact20WorkflowRun> GetRunAsync(long runId, CancellationToken cancellationToken) => Task.FromResult(runId == CurrentRunId
                ? CurrentRunOverride ?? GetCurrentRun()
                : new Exact20WorkflowRun(pins.RunId, pins.WorkflowId, ".github/workflows/packages.yml", "push", "completed", "cancelled", 1, "main", pins.SourceCommit, pins.RepositoryId, pins.RepositoryId));

            public Task<Exact20Artifact> GetArtifactAsync(long artifactId, CancellationToken cancellationToken) => Task.FromResult(ArtifactOverride ?? GetPinnedArtifact());

            public Task DownloadArtifactAsync(long artifactId, string destination, long maximumBytes, CancellationToken cancellationToken)
            {
                if (new FileInfo(artifactPath).Length > maximumBytes)
                    throw new InvalidDataException("fixture artifact exceeded bound");
                File.Copy(artifactPath, destination);
                return Task.CompletedTask;
            }

            public Task<string> GetWorkflowStateAsync(long workflowId, CancellationToken cancellationToken) => Task.FromResult(WorkflowStateOverride);

            public Task<Exact20RunCount> GetNonterminalRunCountAsync(long workflowId, string status, CancellationToken cancellationToken)
            {
                RunCountCalls.Add((workflowId, status));
                return Task.FromResult(RunCountOverride?.Invoke(workflowId, status) ?? new Exact20RunCount(true, 0));
            }

            public Task<Exact20ApprovalComment> GetApprovalCommentAsync(long commentId, CancellationToken cancellationToken) =>
                MissingApprovalComment
                    ? Task.FromException<Exact20ApprovalComment>(new InvalidOperationException("The approval receipt was not found."))
                    : Task.FromResult(ApprovalComment with { Author = ApprovalAuthor });
        }

        public sealed class FakeFeed(IReadOnlyList<Exact20ManifestPackage> rows, IReadOnlyDictionary<string, byte[]> packages) : IExact20Feed
        {
            private readonly Dictionary<string, Exact20ManifestPackage> byId = rows.ToDictionary(row => row.PackageId!, StringComparer.OrdinalIgnoreCase);
            private readonly ConcurrentDictionary<string, byte[]> available = new(StringComparer.OrdinalIgnoreCase);
            private int listCalls;
            public int ListCalls => listCalls;
            public int PrepareCount { get; private set; }
            public List<string> PushedPackageIds { get; } = [];
            public PushStatus NextPushStatus { get; set; } = PushStatus.Pushed;
            public Func<string, Exact20PackageResponse>? PostPushResponse { get; set; }
            public Func<string, Exact20PackageResponse?>? PreflightResponse { get; set; }
            public Func<string, IReadOnlyList<string>?>? VersionsOverride { get; set; }
            public string? AdditionalFeedVersion { get; set; }

            public byte[] PinnedPackageBytes(string packageId) => packages[packageId];

            public void PrepareForExecution() => PrepareCount++;

            public Task<IReadOnlyList<string>> ListVersionsAsync(string packageId, CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref listCalls);
                if (VersionsOverride?.Invoke(packageId) is { } overridden)
                    return Task.FromResult(overridden);
                var versions = new List<string> { byId[packageId].ExpectedStateVersion! };
                if (AdditionalFeedVersion is { } extra)
                    versions.Add(extra);
                return Task.FromResult<IReadOnlyList<string>>(versions);
            }

            public Task<Exact20PackageResponse> GetPackageAsync(string packageId, string version, CancellationToken cancellationToken)
            {
                if (PreflightResponse?.Invoke(packageId) is { } preflight)
                    return Task.FromResult(preflight);
                if (PostPushResponse is { } overrideResponse && PushedPackageIds.Contains(packageId, StringComparer.OrdinalIgnoreCase))
                    return Task.FromResult(overrideResponse(packageId));
                return Task.FromResult(available.TryGetValue(packageId, out var bytes)
                    ? new Exact20PackageResponse(200, bytes)
                    : byId[packageId].Recover
                        ? new Exact20PackageResponse(404, null)
                        : new Exact20PackageResponse(200, packages[packageId]));
            }

            public Task<PushResult> PushAsync(string packagePath, CancellationToken cancellationToken)
            {
                var packed = PackedPackage.Read(packagePath);
                PushedPackageIds.Add(packed.PackageId);
                available[packed.PackageId] = File.ReadAllBytes(packagePath);
                return Task.FromResult(new PushResult(NextPushStatus, NextPushStatus == PushStatus.Pushed ? "201" : "ambiguous"));
            }
        }

        public sealed class FakePublishState : IPublishState
        {
            private readonly Dictionary<string, PublishedVersions> records = new(StringComparer.Ordinal);
            private readonly Dictionary<string, byte[]> blobs = new(StringComparer.Ordinal);
            private int nextCommit = 0;
            public string Tip { get; set; }
            public PublishedVersions CurrentRecord => records[Tip];
            public int WriteAttempts { get; private set; }
            public int SuccessfulWrites { get; private set; }
            public Action<FakePublishState>? BeforeWrite { get; set; }
            public string? ReadbackTipOverride { get; set; }
            public bool CorruptReadbackBlobAfterWrite { get; set; }
            public bool ThrowAfterAccept { get; set; }
            public byte[]? ReadbackBlobOverride { get; set; }

            public FakePublishState(string tip, PublishedVersions record, byte[] bytes)
            {
                Tip = tip;
                records[tip] = record;
                blobs[tip] = bytes;
            }

            public string? FetchTip() => SuccessfulWrites > 0 ? ReadbackTipOverride ?? Tip : Tip;
            public IReadOnlyList<string> Revisions(string tip) => [tip];
            public PublishedVersions Read(string revision) => records[revision];
            public byte[] ReadBlob(string revision) => SuccessfulWrites > 0 && CorruptReadbackBlobAfterWrite
                ? Encoding.UTF8.GetBytes("{}").ToArray()
                : ReadbackBlobOverride ?? blobs[revision];

            public string Write(PublishedVersions record, string? parent, string message)
            {
                WriteAttempts++;
                BeforeWrite?.Invoke(this);
                if (parent != Tip)
                    throw new InvalidOperationException("publish-state compare-and-swap rejected moved tip");
                var commit = (++nextCommit).ToString("x40", System.Globalization.CultureInfo.InvariantCulture);
                Tip = commit;
                records[commit] = record;
                blobs[commit] = Encoding.UTF8.GetBytes(record.Serialize());
                SuccessfulWrites++;
                if (ThrowAfterAccept)
                    throw new InvalidOperationException("simulated write accepted remotely but response was lost");
                return commit;
            }
        }

        private sealed class FakeLocalGit(string head, FakePublishState state) : IExact20LocalGit
        {
            public string ResolveHead() => head;
            public bool IsAncestorOrSelf(string ancestor, string descendant) => ancestor == head && descendant == head;
            public byte[] ReadPublishedVersions(string commit) => state.ReadBlob(commit);
        }
    }
}
