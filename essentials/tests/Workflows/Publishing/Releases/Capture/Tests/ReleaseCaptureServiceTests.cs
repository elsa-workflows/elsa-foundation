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

/// <summary>Controlled owner acquisition with the actual shared builder and durable SQLite archive; not native/model capture qualification.</summary>
public sealed class ReleaseCaptureServiceTests
{
    [Fact]
    public async Task Complete_archive_is_durable_before_source_retention_is_released_and_survives_source_removal()
    {
        using var fixture = await CaptureFixture.CreateAsync();
        var result = await fixture.Service.CreateAsync("complete", Selection());
        Assert.Equal(ReleaseCaptureOutcome.Created, result.Outcome); Assert.False(fixture.Source.Held);
        Assert.True(fixture.Writer.CommittedWhileHeld); fixture.Source.Available = false;
        using var reopened = fixture.Provider.CreateScope();
        var archived = await reopened.ServiceProvider.GetRequiredService<IReleaseArchiveReader>().FindAsync("complete");
        Assert.NotNull(archived); Assert.Equal(result.ReleaseDigest, archived.Transport.Digest);
        Assert.Equal(fixture.TransportFixture.Payload, archived.Transport.GetBlobBytes(fixture.TransportFixture.BlobDigest));
        Assert.Throws<InvalidOperationException>(() => fixture.Source.Reader.GetPayload(fixture.Source.Reader.Members[0].Key));
    }

    [Fact]
    public async Task Same_complete_content_retries_through_the_existing_archive_without_overwriting_it()
    {
        using var fixture = await CaptureFixture.CreateAsync();
        var first = await fixture.Service.CreateAsync("same", Selection());
        var second = await fixture.Service.CreateAsync("same", Selection());
        Assert.Equal(ReleaseCaptureOutcome.Existing, second.Outcome); Assert.Equal(first.ReleaseDigest, second.ReleaseDigest);
        Assert.Equal(2, fixture.Source.Executions);
    }

    [Fact]
    public async Task Lost_commit_acknowledgment_is_preserved_and_reconciles_by_capture_id_without_reinvoking_source()
    {
        using var fixture = await CaptureFixture.CreateAsync(); fixture.Writer.LoseAcknowledgment = true;
        await Assert.ThrowsAsync<ReleaseArchiveCommitOutcomeUnknownException>(async () => await fixture.Service.CreateAsync("lost-ack", Selection()));
        Assert.Equal(1, fixture.Source.Executions); Assert.False(fixture.Source.Held); fixture.Source.Available = false;
        Assert.NotNull(await fixture.Scope.ServiceProvider.GetRequiredService<IReleaseArchiveReader>().FindAsync("lost-ack"));
    }

    [Fact]
    public async Task A_source_that_swallows_callback_failure_cannot_hide_an_unknown_archive_commit()
    {
        using var fixture = await CaptureFixture.CreateAsync(); fixture.Writer.LoseAcknowledgment = true;
        fixture.Source.SwallowCallbackFailure = true;
        await Assert.ThrowsAsync<ReleaseArchiveCommitOutcomeUnknownException>(async () => await fixture.Service.CreateAsync("swallowed-ack", Selection()));
        Assert.Equal(1, fixture.Source.Executions); Assert.False(fixture.Source.Held);
        Assert.NotNull(await fixture.Scope.ServiceProvider.GetRequiredService<IReleaseArchiveReader>().FindAsync("swallowed-ack"));
    }

    [Fact]
    public async Task Semantic_refusal_leaves_no_archive_and_releases_source_retention()
    {
        using var fixture = await CaptureFixture.CreateAsync(); fixture.Verifier.Refuse = true;
        var exception = await Assert.ThrowsAsync<ReleaseCaptureException>(async () => await fixture.Service.CreateAsync("refused", Selection()));
        Assert.Equal("capture_content_not_verified", exception.Code); Assert.NotNull(exception.Verification);
        Assert.Equal(ReleaseSemanticStatus.Unsupported, exception.Verification.Status);
        Assert.False(fixture.Source.Held); Assert.Equal(0, fixture.Writer.Calls);
        Assert.Null(await fixture.Scope.ServiceProvider.GetRequiredService<IReleaseArchiveReader>().FindAsync("refused"));
    }

    [Theory]
    [InlineData("unsupported")]
    [InlineData("duplicate")]
    [InlineData("empty")]
    [InlineData("invalid-version")]
    public async Task Invalid_request_never_acquires_source_or_writes_an_archive(string change)
    {
        using var fixture = await CaptureFixture.CreateAsync();
        ReleaseCaptureSelection[] selections = change switch
        {
            "unsupported" => [new("unknown-owner", "exact", null, ReleaseRootAction.ActivateServing)],
            "duplicate" => [Selection()[0], Selection()[0]],
            "empty" => [],
            _ => [new("controlled-exact", "exact", new("integer", "wrong", 1), ReleaseRootAction.ActivateServing)]
        };
        await Assert.ThrowsAsync<ReleaseCaptureException>(async () => await fixture.Service.CreateAsync("invalid", selections));
        Assert.Equal(0, fixture.Source.Executions); Assert.Equal(0, fixture.Writer.Calls);
    }

    [Fact]
    public async Task Canceled_request_never_acquires_source()
    {
        using var fixture = await CaptureFixture.CreateAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await fixture.Service.CreateAsync("cancelled", Selection(), cancellationToken: new(true)));
        Assert.Equal(0, fixture.Source.Executions); Assert.Equal(0, fixture.Writer.Calls);
    }

    [Fact]
    public async Task A_source_that_never_calls_capture_cannot_return_a_created_receipt()
    {
        using var fixture = await CaptureFixture.CreateAsync(); fixture.Source.OmitCallback = true;
        var exception = await Assert.ThrowsAsync<ReleaseCaptureException>(async () => await fixture.Service.CreateAsync("omitted", Selection()));
        Assert.Equal("capture_source_callback_invalid", exception.Code); Assert.Equal(0, fixture.Writer.Calls);
    }

    [Fact]
    public async Task A_source_that_repeats_the_callback_is_refused_and_never_retries_the_archive_write()
    {
        using var fixture = await CaptureFixture.CreateAsync(); fixture.Source.RepeatCallback = true;
        var exception = await Assert.ThrowsAsync<ReleaseCaptureException>(async () => await fixture.Service.CreateAsync("repeated", Selection()));
        Assert.Equal("capture_source_callback_invalid", exception.Code); Assert.Equal(1, fixture.Writer.Calls);
        Assert.False(fixture.Source.Held);
        // The first complete durable archive can be reconciled; no partial bundle is exposed as a receipt.
        Assert.NotNull(await fixture.Scope.ServiceProvider.GetRequiredService<IReleaseArchiveReader>().FindAsync("repeated"));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Unqualified_archive_capabilities_refuse_before_source_acquisition(bool durable, bool ready)
    {
        using var fixture = await CaptureFixture.CreateAsync();
        var service = fixture.Compose(capabilities: new ControlledCapabilities(durable, ready));
        var exception = await Assert.ThrowsAsync<ReleaseCaptureException>(async () => await service.CreateAsync("unready", Selection()));
        Assert.Equal("capture_archive_not_durable_or_ready", exception.Code); Assert.Equal(0, fixture.Source.Executions); Assert.Equal(0, fixture.Writer.Calls);
    }

    [Theory]
    [InlineData("digest")]
    [InlineData("diagnostics")]
    public async Task Malformed_selected_builder_result_is_refused_before_archive_writes(string change)
    {
        using var fixture = await CaptureFixture.CreateAsync();
        var builder = new MalformedBuilder(fixture.Scope.ServiceProvider.GetRequiredService<IReleaseBundleBuilder>(), change);
        var service = fixture.Compose(selectedBuilder: builder);
        var exception = await Assert.ThrowsAsync<ReleaseCaptureException>(async () => await service.CreateAsync("bad-builder", Selection()));
        Assert.Equal("capture_content_not_verified", exception.Code); Assert.Equal(0, fixture.Writer.Calls); Assert.False(fixture.Source.Held);
    }

    [Fact]
    public async Task Aggregate_work_limit_is_checked_before_source_payload_copy_or_archive_creation()
    {
        using var fixture = await CaptureFixture.CreateAsync();
        var exception = await Assert.ThrowsAsync<ReleaseCaptureException>(async () => await fixture.Service.CreateAsync("bounded", Selection(),
            new() { MaxInspectionBytes = fixture.TransportFixture.Payload.Length - 1 }));
        Assert.Equal("capture_payload_work_limit_exceeded", exception.Code); Assert.Equal(0, fixture.Source.Reader.Reads);
        Assert.Equal(0, fixture.Writer.Calls); Assert.False(fixture.Source.Held);
    }

    [Fact]
    public async Task Several_owner_callbacks_remain_held_through_one_complete_archive_commit()
    {
        using var fixture = await CaptureFixture.CreateAsync();
        var second = new ControlledSource(new ArchiveFixture(2), "second-artifact", "controlled-secondary");
        fixture.Writer.AllSourcesHeld = () => fixture.Source.Held && second.Held;
        var service = fixture.Compose(sources: [fixture.Source, second]);
        var receipt = await service.CreateAsync("several", [Selection()[0], new("controlled-secondary", "second", new("semver", "1.0.0", null), ReleaseRootAction.ActivateServing)]);
        Assert.Equal(ReleaseCaptureOutcome.Created, receipt.Outcome); Assert.Equal(1, fixture.Writer.Calls);
        Assert.False(fixture.Source.Held); Assert.False(second.Held);
        var archive = await fixture.Scope.ServiceProvider.GetRequiredService<IReleaseArchiveReader>().FindAsync("several");
        Assert.Equal(2, archive!.Transport.Members.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Shared_member_descriptor_or_byte_disagreement_never_creates_an_archive(bool sameDescriptor)
    {
        using var fixture = await CaptureFixture.CreateAsync();
        var changed = new ArchiveFixture(sameDescriptor ? 1 : 2);
        if (sameDescriptor) changed.Payload[^1] ^= 1;
        var second = new ControlledSource(changed, "fixture-artifact", "controlled-secondary");
        var service = fixture.Compose(sources: [fixture.Source, second]);
        var exception = await Assert.ThrowsAsync<ReleaseCaptureException>(async () => await service.CreateAsync("disagreement",
            [Selection()[0], new("controlled-secondary", "second", new("semver", "1.0.0", null), ReleaseRootAction.ActivateServing)]));
        Assert.Equal("capture_member_conflict", exception.Code); Assert.Equal(0, fixture.Writer.Calls);
        Assert.False(fixture.Source.Held); Assert.False(second.Held);
    }

    [Fact]
    public async Task Composed_event_middleware_observes_retention_through_the_actual_archive_commit()
    {
        using var fixture = await CaptureFixture.CreateAsync(); var observed = new List<string>();
        fixture.Provider.GetRequiredService<IEventPipeline>().Setup(pipeline => pipeline.Use(next => async context =>
        {
            Assert.IsType<ReleaseSourceMembersCapturing>(context.Event); Assert.True(fixture.Source.Held);
            observed.Add("before"); await next(context);
            Assert.True(fixture.Source.Held); Assert.Equal(1, fixture.Writer.Calls); Assert.True(fixture.Writer.CommittedWhileHeld);
            observed.Add("after");
        }));
        Assert.Equal(ReleaseCaptureOutcome.Created, (await fixture.Service.CreateAsync("event-pipeline", Selection())).Outcome);
        Assert.Equal(["before", "after"], observed); Assert.False(fixture.Source.Held);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Event_short_circuit_or_middleware_failure_cannot_acknowledge_capture(bool throwing)
    {
        using var fixture = await CaptureFixture.CreateAsync(); var actual = new IOException("controlled middleware failure");
        fixture.Provider.GetRequiredService<IEventPipeline>().Setup(pipeline => pipeline.Use(next => context =>
            throwing ? ValueTask.FromException(actual) : ValueTask.CompletedTask));
        if (throwing)
            Assert.Same(actual, await Assert.ThrowsAsync<IOException>(() => fixture.Service.CreateAsync("blocked-event", Selection()).AsTask()));
        else
            Assert.Equal("capture_source_callback_invalid", (await Assert.ThrowsAsync<ReleaseCaptureException>(() => fixture.Service.CreateAsync("blocked-event", Selection()).AsTask())).Code);
        Assert.Equal(0, fixture.Writer.Calls); Assert.False(fixture.Source.Held);
        Assert.Null(await fixture.Scope.ServiceProvider.GetRequiredService<IReleaseArchiveReader>().FindAsync("blocked-event"));
    }

    private static ReleaseCaptureSelection[] Selection() => [new("controlled-exact", "exact", new("semver", "1.0.0", null), ReleaseRootAction.ActivateServing)];

    private sealed class CaptureFixture : IDisposable
    {
        private readonly string _directory;
        public ArchiveFixture TransportFixture { get; } = new();
        public ControlledSource Source { get; }
        public ControlledVerifier Verifier { get; } = new();
        public ServiceProvider Provider { get; }
        public IServiceScope Scope { get; }
        public GuardedWriter Writer { get; }
        public ReleaseCaptureService Service { get; }
        private CaptureFixture()
        {
            _directory = Path.Combine(Path.GetTempPath(), "elsa-release-capture-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(_directory);
            Source = new(TransportFixture);
            var services = new ServiceCollection(); new WorkflowReleaseTransportFeature().ConfigureServices(services);
            new WorkflowReleaseVerificationFeature().ConfigureServices(services);
            new EventsFeature().ConfigureServices(services); new WorkflowReleaseCaptureFeature().ConfigureServices(services);
            services.AddSingleton<IReleaseArtifactSemanticVerifier>(Verifier);
            services.AddSingleton<IPersistenceAccessContextAccessor>(new MutableAccess(PersistenceAccessContext.Scoped(new PersistenceScope(ArchiveFixture.DefaultScope.Partition))));
            services.AddDbContext<ConsumerContext>(options => options.UseSqlite("Data Source=" + Path.Combine(_directory, "archive.db") + ";Pooling=False"));
            services.AddReleaseArchiveStore<ConsumerContext>(_ => ArchiveFixture.DefaultScope);
            Provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true }); Scope = Provider.CreateScope();
            Writer = new(Scope.ServiceProvider.GetRequiredService<IReleaseArchiveWriter>(), Source);
            Service = Compose();
        }
        public ReleaseCaptureService Compose(IReleaseBundleBuilder? selectedBuilder = null, IReleaseArchiveStoreCapabilities? capabilities = null,
            IEnumerable<IReleaseCaptureSource>? sources = null) =>
            new(selectedBuilder ?? Scope.ServiceProvider.GetRequiredService<IReleaseBundleBuilder>(), Scope.ServiceProvider.GetRequiredService<IReleaseBundleCodec>(), Writer,
                capabilities ?? Scope.ServiceProvider.GetRequiredService<IReleaseArchiveStoreCapabilities>(), sources ?? [Source], Scope.ServiceProvider.GetRequiredService<IInlineEventPublisher>());
        public static async Task<CaptureFixture> CreateAsync()
        {
            var fixture = new CaptureFixture();
            try { await fixture.Scope.ServiceProvider.GetRequiredService<ConsumerContext>().Database.EnsureCreatedAsync(); return fixture; }
            catch { fixture.Dispose(); throw; }
        }
        public void Dispose() { Scope.Dispose(); Provider.Dispose(); Directory.Delete(_directory, true); }
    }

    private sealed class ControlledSource : IReleaseCaptureSource
    {
        public bool Held { get; private set; }
        public bool Available { get; set; } = true;
        public bool OmitCallback { get; set; }
        public bool RepeatCallback { get; set; }
        public bool SwallowCallbackFailure { get; set; }
        public int Executions { get; private set; }
        public ControlledReader Reader { get; }
        public ReleaseSemanticAdapterIdentity Identity { get; } = new("controlled-source", "1");
        public IReadOnlyCollection<string> SupportedSelectionKinds { get; }
        public ControlledSource(ArchiveFixture fixture, string memberIdentity = "fixture-artifact", string selectionKind = "controlled-exact")
        {
            Reader = new(fixture, () => Held && Available, memberIdentity); SupportedSelectionKinds = [selectionKind];
        }
        public async ValueTask ExecuteAsync(IReadOnlyList<ReleaseCaptureSelection> selections, ReleaseBundleLimits limits,
            Func<ReleaseSourceCapture, CancellationToken, ValueTask> capture, CancellationToken cancellationToken = default)
        {
            if (!Available) throw new InvalidOperationException("Controlled source was removed.");
            Executions++; Held = true;
            try
            {
                if (OmitCallback) return;
                var key = Reader.Members[0].Key; var content = new ReleaseSourceCapture(Reader, [new(key, key, selections[0].Action)]);
                try { await capture(content, cancellationToken); }
                catch (Exception) when (SwallowCallbackFailure) { return; }
                if (RepeatCallback) await capture(content, cancellationToken);
            }
            finally { Held = false; }
        }
    }
    private sealed class ControlledReader(ArchiveFixture fixture, Func<bool> retained, string memberIdentity) : IReleaseContentReader
    {
        public int Reads { get; private set; }
        public IReadOnlyList<ReleaseContentMember> Members { get; } = [new(new("workflow-executable", memberIdentity, "fixture-json", 1,
            fixture.BlobDigest, fixture.Payload.Length), new("semver", "1.0.0", null))];
        public ReleaseContentMember GetMember(ReleaseMemberKey key) => Members.Single(item => item.Key == key);
        public ReadOnlyMemory<byte> GetPayload(ReleaseMemberKey key)
        {
            if (!retained()) throw new InvalidOperationException("Controlled source read outside retention.");
            Reads++; _ = GetMember(key); return fixture.Payload.ToArray();
        }
    }
    private sealed class ControlledVerifier : IReleaseArtifactSemanticVerifier
    {
        public bool Refuse { get; set; }
        public ReleaseSemanticAdapterIdentity Identity { get; } = new("controlled-artifact", "1");
        public IReadOnlyCollection<ReleaseCodecKey> SupportedCodecs { get; } = [new("workflow-executable", "fixture-json", 1)];
        public ValueTask<ReleaseSemanticFacts> VerifyAsync(ReleaseContentMember member, ReadOnlyMemory<byte> payload, IReleaseContentReader content, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Refuse ? new ReleaseSemanticFacts(ReleaseSemanticStatus.Unsupported, null, null, false, diagnostics: [new("controlled_unsupported", member.Key)])
                : new ReleaseSemanticFacts(ReleaseSemanticStatus.Verified, member.Key, member.OwnerVersion, true,
                    rootCapabilities: [new(member.Key, ReleaseRootAction.ActivateServing)]));
    }
    private sealed class GuardedWriter(IReleaseArchiveWriter inner, ControlledSource source) : IReleaseArchiveWriter
    {
        public bool LoseAcknowledgment { get; set; }
        public bool CommittedWhileHeld { get; private set; }
        public Func<bool>? AllSourcesHeld { get; set; }
        public int Calls { get; private set; }
        public async ValueTask<ReleaseArchiveCreateReceipt> CreateAsync(string captureId, Stream zip, CancellationToken cancellationToken = default)
        {
            Calls++; Assert.True(source.Held); if (AllSourcesHeld is not null) Assert.True(AllSourcesHeld());
            var receipt = await inner.CreateAsync(captureId, zip, cancellationToken);
            CommittedWhileHeld = source.Held; Assert.True(CommittedWhileHeld);
            if (AllSourcesHeld is not null) Assert.True(AllSourcesHeld());
            if (LoseAcknowledgment) throw new ReleaseArchiveCommitOutcomeUnknownException(new IOException("Controlled lost acknowledgment."));
            return receipt;
        }
    }
    private sealed class ControlledCapabilities(bool durable, bool ready) : IReleaseArchiveStoreCapabilities
    {
        public ValueTask<ReleaseArchiveStoreCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ReleaseArchiveStoreCapabilities("controlled", durable, ready));
    }
    private sealed class MalformedBuilder(IReleaseBundleBuilder inner, string change) : IReleaseBundleBuilder
    {
        public async ValueTask<ReleaseBundleBuildResult> BuildAsync(IReleaseContentReader content, IReadOnlyList<ReleaseBundleRoot> roots,
            ReleaseSemanticVerificationOptions? options = null, CancellationToken cancellationToken = default)
        {
            var actual = await inner.BuildAsync(content, roots, options, cancellationToken); var facts = actual.Verification;
            return new(actual.Bundle, new(ReleaseSemanticStatus.Verified, change == "digest" ? "sha256:" + new string('0', 64) : facts.Digest,
                facts.Edges, facts.Resources, facts.Prerequisites, facts.Capabilities, change == "diagnostics" ? [new("controlled_diagnostic")] : []));
        }
    }
}
