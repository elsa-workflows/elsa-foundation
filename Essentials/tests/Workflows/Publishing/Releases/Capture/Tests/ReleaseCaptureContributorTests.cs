using System.Collections;
using Elsa.Persistence.Core;
using Elsa.Events;
using Elsa.Events.Core.Contracts;
using Elsa.Workflows.Publishing.Releases.Core;
using Elsa.Workflows.Publishing.Releases.Capture.Core;
using Elsa.Workflows.Publishing.Releases.Persistence.Core;
using Elsa.Workflows.Publishing.Releases.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Publishing.Releases.Persistence.EntityFrameworkCore.Tests;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Publishing.Releases.Capture.Tests;

/// <summary>Controlled module fan-in with the actual builder and SQLite archive; no native/model owner qualification.</summary>
public sealed class ReleaseCaptureContributorTests
{
    private static readonly ReleaseCodecKey Codec = new("workflow-executable", "contributor-fixture", 1);

    [Fact]
    public async Task Transitive_member_contributions_are_processed_once_and_all_retention_survives_the_actual_archive_commit()
    {
        using var fixture = await Fixture.CreateAsync(); var b = Data.Create("b", 2); var c = Data.Create("c", 3);
        fixture.Verifier.Dependencies["a"] = ["b"]; fixture.Verifier.Dependencies["b"] = ["c"];
        var contributor = new Contributor("module", containers => containers.SelectMany(member => member.Key.Identity switch
            { "a" => new[] { b }, "b" => new[] { c }, _ => [] }).ToArray());
        fixture.Contributors = [contributor];
        var receipt = await fixture.Service().CreateAsync("transitive", Selection());
        Assert.Equal(ReleaseCaptureOutcome.Created, receipt.Outcome); Assert.Equal(1, fixture.Writer.Calls);
        Assert.True(fixture.Writer.CommittedWhileHeld); Assert.False(fixture.Source.Held);
        Assert.All(contributor.Frames, frame => Assert.False(frame.Held));
        Assert.Equal(new[] { "a", "b", "c" }, contributor.Observed.SelectMany(batch => batch).Order(StringComparer.Ordinal));
        fixture.Source.Available = false;
        var archive = await fixture.Scope.ServiceProvider.GetRequiredService<IReleaseArchiveReader>().FindAsync("transitive");
        Assert.NotNull(archive); Assert.Equal(3, archive.Transport.Members.Count);
        Assert.Equal(b.Payload, archive.Transport.GetBlobBytes(b.Member.Descriptor.PayloadDigest));
        Assert.Throws<InvalidOperationException>(() => contributor.Frames[0].Reader.GetPayload(b.Member.Key));
    }

    [Fact]
    public async Task Several_contributors_may_share_a_container_codec_without_reprocessing_the_same_member_digest()
    {
        using var fixture = await Fixture.CreateAsync(); var b = Data.Create("b", 2); var c = Data.Create("c", 3);
        fixture.Verifier.Dependencies["a"] = ["b", "c"];
        var first = new Contributor("a-module", batch => batch.Any(member => member.Key.Identity == "a") ? [b] : []);
        var second = new Contributor("b-module", batch => batch.Any(member => member.Key.Identity == "a") ? [c] : []);
        fixture.Contributors = [second, first];
        await fixture.Service().CreateAsync("shared-codec", Selection());
        foreach (var contributor in fixture.Contributors)
        {
            var observed = contributor.Observed.SelectMany(batch => batch).ToArray();
            Assert.Equal(3, observed.Length); Assert.Equal(3, observed.Distinct(StringComparer.Ordinal).Count());
        }
        Assert.True(fixture.Writer.CommittedWhileHeld);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Contributor_descriptor_or_actual_byte_conflict_refuses_before_any_archive_write(bool sameDescriptor)
    {
        using var fixture = await Fixture.CreateAsync(); var original = fixture.Source.Material;
        var conflicting = sameDescriptor ? original with { Payload = original.Payload.Select((value, index) => index == 0 ? (byte)(value ^ 1) : value).ToArray() }
            : Data.Create("a", 2);
        fixture.Contributors = [new("conflict", _ => [conflicting])];
        var refused = await Assert.ThrowsAsync<ReleaseCaptureException>(() => fixture.Service().CreateAsync("conflict", Selection()).AsTask());
        Assert.Equal("capture_member_conflict", refused.Code); Assert.Equal(0, fixture.Writer.Calls);
        Assert.False(fixture.Source.Held); Assert.All(fixture.Contributors[0].Frames, frame => Assert.False(frame.Held));
    }

    [Fact]
    public async Task Expanded_byte_limit_refuses_before_contributor_acquisition_or_payload_reads()
    {
        using var fixture = await Fixture.CreateAsync(); var contributor = new Contributor("never-read", _ => []); fixture.Contributors = [contributor];
        await Assert.ThrowsAsync<ReleaseCaptureException>(() => fixture.Service().CreateAsync("expanded", Selection(), new()
        { TransportLimits = new() { MaxExpandedBytes = fixture.Source.Material.Payload.Length - 1 } }).AsTask());
        Assert.Equal(0, contributor.Calls); Assert.Equal(0, fixture.Source.Reader.Reads); Assert.Equal(0, fixture.Writer.Calls);
    }

    [Fact]
    public async Task Per_adapter_inspection_budget_is_checked_before_any_contributor_is_invoked()
    {
        using var fixture = await Fixture.CreateAsync(); var first = new Contributor("first", _ => []); var second = new Contributor("second", _ => []);
        fixture.Contributors = [first, second];
        var refused = await Assert.ThrowsAsync<ReleaseCaptureException>(() => fixture.Service().CreateAsync("inspection", Selection(), new()
        { MaxInspectionBytes = fixture.Source.Material.Payload.Length }).AsTask());
        Assert.Equal("capture_source_inspection_limit_exceeded", refused.Code);
        Assert.Equal(0, first.Calls); Assert.Equal(0, second.Calls); Assert.Equal(0, fixture.Source.Reader.Reads); Assert.Equal(0, fixture.Writer.Calls);
    }

    [Fact]
    public async Task One_digest_cannot_claim_different_lengths_under_distinct_keys_to_bypass_expanded_bounds()
    {
        var original = Data.Create("a", 1); var descriptor = original.Member.Descriptor;
        var alias = new Data(new(new(descriptor.Kind, "alias", descriptor.Codec, descriptor.CodecVersion,
            descriptor.PayloadDigest, descriptor.ByteLength + 1), original.Member.OwnerVersion), original.Payload);
        var first = new Reader([original], () => true); var second = new Reader([alias], () => true);
        var contributor = new Contributor("must-not-start", _ => []); var completes = 0;
        var refused = await Assert.ThrowsAsync<ReleaseCaptureException>(() => new ReleaseCaptureContributors([contributor]).ExecuteAsync(
            [new(first, []), new(second, [])], new() { TransportLimits = new() { MaxExpandedBytes = descriptor.ByteLength } },
            (_, _) => { completes++; return ValueTask.CompletedTask; }, default).AsTask());
        Assert.Equal("capture_member_conflict", refused.Code); Assert.Equal(0, contributor.Calls);
        Assert.Equal(0, first.Reads); Assert.Equal(0, second.Reads); Assert.Equal(0, completes);
    }

    [Fact]
    public async Task Dependency_round_limit_releases_every_retained_contribution_and_never_archives_a_partial_closure()
    {
        using var fixture = await Fixture.CreateAsync(); var b = Data.Create("b", 2); var c = Data.Create("c", 3);
        fixture.Contributors = [new("chain", batch => batch.Any(member => member.Key.Identity == "a") ? [b] : [c])];
        var refused = await Assert.ThrowsAsync<ReleaseCaptureException>(() => fixture.Service().CreateAsync("round-limit", Selection(), new()
        { TransportLimits = new() { MaxDependencyDepth = 1 } }).AsTask());
        Assert.Equal("capture_source_round_limit_exceeded", refused.Code); Assert.Equal(1, fixture.Contributors[0].Calls);
        Assert.All(fixture.Contributors[0].Frames, frame => Assert.False(frame.Held)); Assert.Equal(0, fixture.Writer.Calls);
    }

    [Fact]
    public async Task Canceled_contribution_callback_does_not_acquire_the_next_contributor()
    {
        using var fixture = await Fixture.CreateAsync(); var first = new Contributor("a-cancel", _ => []) { CallbackToken = new(true) };
        var second = new Contributor("b-must-not-start", _ => []); fixture.Contributors = [first, second];
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Service().CreateAsync("cancelled", Selection()).AsTask());
        Assert.Equal(1, first.Calls); Assert.Equal(0, second.Calls); Assert.Equal(0, fixture.Writer.Calls);
    }

    [Fact]
    public async Task A_contributor_cannot_swallow_an_unknown_actual_archive_commit_and_release_a_created_receipt()
    {
        using var fixture = await Fixture.CreateAsync(); var contributor = new Contributor("swallows", _ => []) { SwallowFailure = true };
        fixture.Contributors = [contributor]; fixture.Writer.LoseAcknowledgment = true;
        await Assert.ThrowsAsync<ReleaseArchiveCommitOutcomeUnknownException>(() => fixture.Service().CreateAsync("unknown", Selection()).AsTask());
        Assert.Equal(1, fixture.Writer.Calls); Assert.Equal(1, contributor.Swallowed); Assert.False(fixture.Source.Held);
        Assert.NotNull(await fixture.Scope.ServiceProvider.GetRequiredService<IReleaseArchiveReader>().FindAsync("unknown"));
    }

    [Fact]
    public async Task Returned_root_action_must_equal_the_selected_action_even_when_owner_grants_both_actions()
    {
        using var fixture = await Fixture.CreateAsync(); fixture.Source.ActionOverride = ReleaseRootAction.ActivateServing;
        var refused = await Assert.ThrowsAsync<ReleaseCaptureException>(() => fixture.Service().CreateAsync("wrong-action", Selection(ReleaseRootAction.MakeAvailable)).AsTask());
        Assert.Equal("capture_source_root_action_mismatch", refused.Code); Assert.Equal(0, fixture.Writer.Calls);
    }

    [Theory]
    [InlineData("duplicate-identity")]
    [InlineData("duplicate-codec")]
    [InlineData("empty-enumeration")]
    [InlineData("too-many")]
    public void Contributor_registration_refuses_ambiguous_or_inconsistent_claims(string change)
    {
        var normal = new Contributor("module", _ => []);
        IReleaseSourceMemberContributor[] values = change switch
        {
            "duplicate-identity" => [normal, new Contributor("module", _ => [])],
            "duplicate-codec" => [new Contributor("module", _ => [], [Codec, Codec])],
            "empty-enumeration" => [new Contributor("module", _ => [], new WrongCountClaims())],
            _ => Enumerable.Range(0, 33).Select(index => (IReleaseSourceMemberContributor)new Contributor("module-" + index, _ => [])).ToArray()
        };
        Assert.Throws<InvalidOperationException>(() => new CaptureReleaseSourceMembers(values));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Detached_callback_is_canceled_and_drained_before_protocol_failure_or_actual_primary_failure_returns(bool throwingCancellationHook)
    {
        var entered = Signal(); var canceled = Signal(); var release = Signal(); var exited = Signal(); Task? callback = null;
        var actualFailure = new ReleaseArchiveCommitOutcomeUnknownException(new IOException("controlled unknown outcome"));
        var commits = 0;
        var operation = RetainedCaptureCallback.ExecuteAsync<object>((capture, token) =>
        {
            callback = capture(new object(), token).AsTask(); return ValueTask.CompletedTask;
        }, async (_, token) =>
        {
            using var registration = token.Register(() => { canceled.TrySetResult(); if (throwingCancellationHook) throw new InvalidOperationException("controlled cancellation hook"); });
            entered.TrySetResult();
            try
            {
                try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
                catch (OperationCanceledException) { canceled.TrySetResult(); await release.Task; if (throwingCancellationHook) throw actualFailure; throw; }
                commits++;
            }
            finally { exited.TrySetResult(); }
        }, default).AsTask();
        try
        {
            await entered.Task; await canceled.Task;
            Assert.False(operation.IsCompleted); Assert.False(exited.Task.IsCompleted); Assert.Equal(0, commits);
            release.TrySetResult();
            if (throwingCancellationHook) Assert.Same(actualFailure, await Assert.ThrowsAsync<ReleaseArchiveCommitOutcomeUnknownException>(() => operation));
            else Assert.Equal("capture_source_callback_invalid", (await Assert.ThrowsAsync<ReleaseCaptureException>(() => operation)).Code);
            Assert.True(exited.Task.IsCompleted); Assert.Equal(0, commits);
        }
        finally
        {
            release.TrySetResult();
            if (callback is not null) { try { await callback; } catch { } }
            try { await operation; } catch { }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Concurrent_duplicate_callback_cannot_replace_the_inflight_unknown_commit(bool detached)
    {
        var release = Signal(); var duplicated = Signal(); var actual = new ReleaseArchiveCommitOutcomeUnknownException(new IOException("controlled unknown commit"));
        Task? first = null; var commits = 0;
        var operation = RetainedCaptureCallback.ExecuteAsync<object>(async (capture, token) =>
        {
            first = capture(new object(), token).AsTask();
            var duplicate = await Assert.ThrowsAsync<ReleaseCaptureException>(() => capture(new object(), token).AsTask());
            Assert.Equal("capture_source_callback_invalid", duplicate.Code); duplicated.SetResult();
            if (!detached) { try { await first; } catch (ReleaseArchiveCommitOutcomeUnknownException) { } }
        }, async (_, _) => { commits++; await release.Task; throw actual; }, default).AsTask();
        try
        {
            await duplicated.Task;
            Assert.False(operation.IsCompleted); Assert.Equal(1, commits);
            release.SetResult();
            Assert.Same(actual, await Assert.ThrowsAsync<ReleaseArchiveCommitOutcomeUnknownException>(() => operation));
            Assert.Equal(1, commits);
        }
        finally
        {
            release.TrySetResult();
            if (first is not null) { try { await first; } catch { } }
            try { await operation; } catch { }
        }
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static ReleaseCaptureSelection[] Selection(ReleaseRootAction action = ReleaseRootAction.ActivateServing) => [new("controlled-roots", "a", new("semver", "1.0.0", null), action)];

    private sealed record Data(ReleaseContentMember Member, byte[] Payload)
    {
        public static Data Create(string id, int value)
        {
            var bytes = new ArchiveFixture(value); return new(new(new(Codec.Kind, id, Codec.Codec, Codec.CodecVersion, bytes.BlobDigest, bytes.Payload.Length),
                new("semver", "1.0.0", null)), bytes.Payload.ToArray());
        }
    }
    private sealed class Reader(Data[] values, Func<bool> retained) : IReleaseContentReader
    {
        public int Reads { get; private set; }
        public IReadOnlyList<ReleaseContentMember> Members { get { Ensure(); return values.Select(value => value.Member).ToArray(); } }
        public ReleaseContentMember GetMember(ReleaseMemberKey key) { Ensure(); return values.Single(value => value.Member.Key == key).Member; }
        public ReadOnlyMemory<byte> GetPayload(ReleaseMemberKey key) { Ensure(); Reads++; return values.Single(value => value.Member.Key == key).Payload.ToArray(); }
        private void Ensure() { if (!retained()) throw new InvalidOperationException("controlled reader outside retention"); }
    }
    private sealed class Frame
    {
        public bool Held { get; set; } = true;
        public Reader Reader { get; }
        public Frame(Data[] data) => Reader = new(data, () => Held);
    }
    private sealed class Contributor(string name, Func<IReadOnlyList<ReleaseContentMember>, Data[]> additions,
        IReadOnlyCollection<ReleaseCodecKey>? codecs = null) : IReleaseSourceMemberContributor
    {
        public ReleaseSemanticAdapterIdentity Identity { get; } = new(name, "1");
        public IReadOnlyCollection<ReleaseCodecKey> SupportedContainerCodecs { get; } = codecs ?? [Codec];
        public List<Frame> Frames { get; } = []; public List<string[]> Observed { get; } = [];
        public int Calls { get; private set; } public int Swallowed { get; private set; }
        public bool SwallowFailure { get; init; } public CancellationToken? CallbackToken { get; init; }
        public async ValueTask ExecuteAsync(IReadOnlyList<ReleaseContentMember> containers, IReleaseContentReader captured,
            ReleaseBundleLimits limits, Func<IReleaseContentReader, CancellationToken, ValueTask> capture, CancellationToken cancellationToken = default)
        {
            Calls++; Observed.Add(containers.Select(member => member.Key.Identity).ToArray()); var frame = new Frame(additions(containers)); Frames.Add(frame);
            try { try { await capture(frame.Reader, CallbackToken ?? cancellationToken); } catch (Exception) when (SwallowFailure) { Swallowed++; } }
            finally { frame.Held = false; }
        }
    }
    private sealed class Source : IReleaseCaptureSource
    {
        public Data Material { get; } = Data.Create("a", 1); public Reader Reader { get; }
        public bool Held { get; private set; } public bool Available { get; set; } = true;
        public ReleaseRootAction? ActionOverride { get; set; }
        public ReleaseSemanticAdapterIdentity Identity { get; } = new("controlled-source", "1");
        public IReadOnlyCollection<string> SupportedSelectionKinds { get; } = ["controlled-roots"];
        public Source() => Reader = new([Material], () => Held && Available);
        public async ValueTask ExecuteAsync(IReadOnlyList<ReleaseCaptureSelection> selections, ReleaseBundleLimits limits,
            Func<ReleaseSourceCapture, CancellationToken, ValueTask> capture, CancellationToken cancellationToken = default)
        {
            Held = true;
            try { await capture(new(Reader, [new(Material.Member.Key, Material.Member.Key, ActionOverride ?? selections[0].Action)]), cancellationToken); }
            finally { Held = false; }
        }
    }
    private sealed class Owner : IReleaseArtifactSemanticVerifier
    {
        public Dictionary<string, string[]> Dependencies { get; } = new(StringComparer.Ordinal);
        public ReleaseSemanticAdapterIdentity Identity { get; } = new("controlled-owner", "1");
        public IReadOnlyCollection<ReleaseCodecKey> SupportedCodecs { get; } = [Codec];
        public ValueTask<ReleaseSemanticFacts> VerifyAsync(ReleaseContentMember member, ReadOnlyMemory<byte> payload, IReleaseContentReader content,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(new ReleaseSemanticFacts(ReleaseSemanticStatus.Verified, member.Key, member.OwnerVersion, true,
            edges: Dependencies.GetValueOrDefault(member.Key.Identity, []).Select(id => new ReleaseSemanticEdge(member.Key, new(Codec.Kind, id), ReleaseEdgeRelation.Dependency, id)).ToArray(),
            rootCapabilities: [new(member.Key, ReleaseRootAction.ActivateServing), new(member.Key, ReleaseRootAction.MakeAvailable)]));
    }
    private sealed class Writer(IReleaseArchiveWriter inner, Func<bool> retained) : IReleaseArchiveWriter
    {
        public int Calls { get; private set; } public bool CommittedWhileHeld { get; private set; } public bool LoseAcknowledgment { get; set; }
        public async ValueTask<ReleaseArchiveCreateReceipt> CreateAsync(string captureId, Stream zip, CancellationToken cancellationToken = default)
        {
            Calls++; Assert.True(retained()); var receipt = await inner.CreateAsync(captureId, zip, cancellationToken);
            CommittedWhileHeld = retained(); Assert.True(CommittedWhileHeld);
            if (LoseAcknowledgment) throw new ReleaseArchiveCommitOutcomeUnknownException(new IOException("controlled lost acknowledgment"));
            return receipt;
        }
    }
    private sealed class Fixture : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "elsa-release-contributors-" + Guid.NewGuid().ToString("N"));
        public Source Source { get; } = new(); public Owner Verifier { get; } = new(); public Contributor[] Contributors { get; set; } = [];
        public ServiceProvider Provider { get; } public IServiceScope Scope { get; } public Writer Writer { get; }
        private Fixture()
        {
            Directory.CreateDirectory(_directory); var services = new ServiceCollection();
            new WorkflowReleaseTransportFeature().ConfigureServices(services); new WorkflowReleaseVerificationFeature().ConfigureServices(services);
            new EventsFeature().ConfigureServices(services); new WorkflowReleaseCaptureFeature().ConfigureServices(services);
            services.AddScoped<IEnumerable<IReleaseSourceMemberContributor>>(_ => Contributors);
            services.AddSingleton<IReleaseArtifactSemanticVerifier>(Verifier);
            services.AddSingleton<IPersistenceAccessContextAccessor>(new MutableAccess(PersistenceAccessContext.Scoped(new PersistenceScope(ArchiveFixture.DefaultScope.Partition))));
            services.AddDbContext<ConsumerContext>(options => options.UseSqlite("Data Source=" + Path.Combine(_directory, "archive.db") + ";Pooling=False"));
            services.AddReleaseArchiveStore<ConsumerContext>(_ => ArchiveFixture.DefaultScope);
            Provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true }); Scope = Provider.CreateScope();
            Writer = new(Scope.ServiceProvider.GetRequiredService<IReleaseArchiveWriter>(), () => Source.Held && Contributors.SelectMany(value => value.Frames).All(frame => frame.Held));
        }
        public ReleaseCaptureService Service() => new(Scope.ServiceProvider.GetRequiredService<IReleaseBundleBuilder>(), Scope.ServiceProvider.GetRequiredService<IReleaseBundleCodec>(),
            Writer, Scope.ServiceProvider.GetRequiredService<IReleaseArchiveStoreCapabilities>(), [Source], Scope.ServiceProvider.GetRequiredService<IInlineEventPublisher>());
        public static async Task<Fixture> CreateAsync()
        {
            var fixture = new Fixture(); try { await fixture.Scope.ServiceProvider.GetRequiredService<ConsumerContext>().Database.EnsureCreatedAsync(); return fixture; }
            catch { fixture.Dispose(); throw; }
        }
        public void Dispose() { Scope.Dispose(); Provider.Dispose(); Directory.Delete(_directory, true); }
    }
    private sealed class WrongCountClaims : IReadOnlyCollection<ReleaseCodecKey>
    {
        public int Count => 1;
        public IEnumerator<ReleaseCodecKey> GetEnumerator() => Enumerable.Empty<ReleaseCodecKey>().GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
