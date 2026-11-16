using System.Net;
using Elsa.Etl.Core;
using Elsa.Etl.Design;
using Elsa.Etl.Design.Api.Models;
using Elsa.Etl.Design.Stores;
using Elsa.Etl.Persistence.EntityFrameworkCore;
using Elsa.Etl.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Etl.Tests;

public sealed partial class EtlDesignApiHttpTests
{
    [Fact]
    public async Task Identical_publish_replays_when_another_request_commits_between_immutable_and_draft_reads()
    {
        var gate = new PublicationReadGate();
        await using var host = await EtlDesignApiTestHost.StartAsync(configureServices: services =>
            services.AddScoped<IEtlDefinitionStore>(provider => new PausedPublicationReadStore(
                new EfEtlDefinitionStore(provider.GetRequiredService<EtlDesignDbContext>(), provider.GetRequiredService<IEtlArtifactCompiler>()), gate)));
        await CreateDefinitionAsync(host, "orders");
        var validation = await ValidateAsync(host, "orders", 1);
        var request = new EtlPublishRequest("orders", validation.Revision, validation.DefinitionVersion, validation.ContentHash!);
        var firstRequest = host.SendAsync(HttpMethod.Post, "/etl/definitions/orders/publish", request, permissions: AuthorPermissions);
        try
        {
            await gate.FirstMiss.Task.WaitAsync(TimeSpan.FromSeconds(10));
            using var second = await host.SendAsync(HttpMethod.Post, "/etl/definitions/orders/publish", request, permissions: AuthorPermissions);
            Assert.Equal(HttpStatusCode.OK, second.StatusCode);
            Assert.Equal("published", (await ReadAsync<EtlPublicationView>(second)).Status);
        }
        finally
        {
            gate.Release.TrySetResult();
        }
        using var first = await firstRequest;
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal("replay", (await ReadAsync<EtlPublicationView>(first)).Status);
        using var versions = await host.SendAsync(HttpMethod.Get, "/etl/definitions/orders/versions", permissions: DefinitionRead);
        Assert.Single((await ReadAsync<EtlPage<EtlPublishedVersionView>>(versions)).Items);
    }

    private sealed class PublicationReadGate
    {
        public int Reads;
        public TaskCompletionSource FirstMiss { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>Pauses one actual SQLite read response without replacing storage or fabricating a publication.</summary>
    private sealed class PausedPublicationReadStore(IEtlDefinitionStore inner, PublicationReadGate gate) : IEtlDefinitionStore
    {
        public ValueTask<EtlDefinitionDraft?> FindDraftAsync(EtlScope scope, string definitionId, CancellationToken cancellationToken = default) =>
            inner.FindDraftAsync(scope, definitionId, cancellationToken);
        public ValueTask<EtlDefinitionWriteResult> CreateDraftAsync(EtlScope scope, string definitionId, EtlPipeline pipeline, CancellationToken cancellationToken = default) =>
            inner.CreateDraftAsync(scope, definitionId, pipeline, cancellationToken);
        public ValueTask<EtlDefinitionWriteResult> UpdateDraftAsync(EtlScope scope, string definitionId, long expectedRevision, EtlPipeline pipeline, CancellationToken cancellationToken = default) =>
            inner.UpdateDraftAsync(scope, definitionId, expectedRevision, pipeline, cancellationToken);
        public ValueTask<EtlPublicationResult> PublishAsync(EtlScope scope, string definitionId, long expectedRevision, EtlArtifact artifact, CancellationToken cancellationToken = default) =>
            inner.PublishAsync(scope, definitionId, expectedRevision, artifact, cancellationToken);

        public async ValueTask<EtlArtifact?> FindPublishedAsync(EtlScope scope, string definitionId, int definitionVersion, CancellationToken cancellationToken = default)
        {
            var artifact = await inner.FindPublishedAsync(scope, definitionId, definitionVersion, cancellationToken);
            if (artifact is null && Interlocked.Increment(ref gate.Reads) == 1)
            {
                gate.FirstMiss.TrySetResult();
                await gate.Release.Task.WaitAsync(cancellationToken);
            }
            return artifact;
        }
    }
}
