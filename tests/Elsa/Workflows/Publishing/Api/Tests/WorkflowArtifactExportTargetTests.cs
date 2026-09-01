using System.Text;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText.Services;
using Elsa.Workflows.Publishing.Api.Services;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

/// <summary>
/// §2.23.1 registration and §2.23.2 behaviour for the export-target seam and its one v1 implementation.
/// </summary>
public sealed class WorkflowArtifactExportTargetTests
{
    [Fact]
    public void The_api_feature_contributes_the_download_target_into_the_fan_in()
    {
        var services = new ServiceCollection();
        new WorkflowsPublishingFeature().ConfigureServices(services);
        new WorkflowsPublishingApiFeature().ConfigureServices(services);
        services.TryAddSingleton<IPayloadSerializer>(new JsonPayloadSerializer(new JsonPayloadConverterRegistry()));
        services.TryAddSingleton<IWorkflowArtifactClosureSerializer, WorkflowArtifactClosureSerializer>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var target = Assert.Single(scope.ServiceProvider.GetServices<IWorkflowArtifactExportTarget>());

        Assert.IsType<DownloadWorkflowArtifactExportTarget>(target);
        Assert.Equal("download", target.TargetId);
    }

    [Fact]
    public void A_later_target_contributes_alongside_the_built_in_rather_than_replacing_it()
    {
        // Fan-in, never replacement (framework §2.24.2 #9): the deferred folder writer and blob push must not be
        // able to displace the one destination a safe GET is allowed to bind to.
        var services = new ServiceCollection();
        new WorkflowsPublishingApiFeature().ConfigureServices(services);
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IWorkflowArtifactExportTarget, FolderTargetStandIn>());
        services.TryAddSingleton<IPayloadSerializer>(new JsonPayloadSerializer(new JsonPayloadConverterRegistry()));
        services.TryAddSingleton<IWorkflowArtifactClosureSerializer, WorkflowArtifactClosureSerializer>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var targetIds = scope.ServiceProvider.GetServices<IWorkflowArtifactExportTarget>()
            .Select(target => target.TargetId)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["download", "folder"], targetIds);
    }

    [Fact]
    public void Registering_the_built_in_twice_still_yields_one_download_target()
    {
        var services = new ServiceCollection();
        new WorkflowsPublishingApiFeature().ConfigureServices(services);
        new WorkflowsPublishingApiFeature().ConfigureServices(services);
        services.TryAddSingleton<IPayloadSerializer>(new JsonPayloadSerializer(new JsonPayloadConverterRegistry()));
        services.TryAddSingleton<IWorkflowArtifactClosureSerializer, WorkflowArtifactClosureSerializer>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.Single(scope.ServiceProvider.GetServices<IWorkflowArtifactExportTarget>());
    }

    [Fact]
    public async Task The_download_target_delivers_an_inline_payload_and_writes_nowhere()
    {
        var closure = WorkflowExecutableExportEndpointTests.Fixture.Closure();
        var codec = WorkflowExecutableExportEndpointTests.Fixture.Codec();
        var target = new DownloadWorkflowArtifactExportTarget(codec);

        var delivery = await target.DeliverAsync(closure);

        Assert.Equal("download", delivery.TargetId);
        Assert.Equal(WorkflowArtifactExportDeliveryKind.InlinePayload, delivery.Kind);
        Assert.Null(delivery.Location);
        Assert.NotNull(delivery.Payload);

        // Encoded through the shared codec, so the bytes are byte-for-byte what the importer reads.
        var payload = delivery.Payload!.Value.ToArray();
        Assert.Equal(codec.Serialize(closure), Encoding.UTF8.GetString(payload));
        Assert.NotEqual(0xEF, payload[0]); // no UTF-8 BOM
        Assert.Equal(closure.RootArtifactId, codec.Deserialize(Encoding.UTF8.GetString(payload))!.RootArtifactId);
    }

    [Fact]
    public async Task The_download_target_honours_cancellation_before_it_encodes()
    {
        var target = new DownloadWorkflowArtifactExportTarget(new ThrowingCodec());
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            target.DeliverAsync(WorkflowExecutableExportEndpointTests.Fixture.Closure(), cancellation.Token));
    }

    [Fact]
    public async Task The_download_target_rejects_a_null_closure()
    {
        var target = new DownloadWorkflowArtifactExportTarget(new ThrowingCodec());

        await Assert.ThrowsAsync<ArgumentNullException>(() => target.DeliverAsync(null!));
    }

    private sealed class FolderTargetStandIn : IWorkflowArtifactExportTarget
    {
        public string TargetId => "folder";

        public Task<WorkflowArtifactExportDelivery> DeliverAsync(
            WorkflowArtifactClosure closure,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(WorkflowArtifactExportDelivery.Receipt(TargetId, "file:///exports/closure.json"));
    }

    private sealed class ThrowingCodec : IWorkflowArtifactClosureSerializer
    {
        public string Serialize(WorkflowArtifactClosure closure) =>
            throw new InvalidOperationException("The codec must not be reached.");

        public WorkflowArtifactClosure? Deserialize(string json) =>
            throw new InvalidOperationException("The codec must not be reached.");
    }
}
