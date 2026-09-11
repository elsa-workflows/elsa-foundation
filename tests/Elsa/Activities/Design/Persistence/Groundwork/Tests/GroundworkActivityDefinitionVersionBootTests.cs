using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Groundwork.Services;
using Elsa.Primitives.Contracts;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText.Services;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Xunit;

#pragma warning disable CS0618

namespace Elsa.Activities.Design.Persistence.Groundwork.Tests;

/// <summary>
/// Runs the activity-version write and read paths against the production <see cref="JsonPayloadSerializer"/>, whose
/// converters arrive only when the converter startup task runs. Startup-task resolution constructs the version store
/// before any task runs, so the store must pick the converters up at read time (#1661).
/// </summary>
public sealed class GroundworkActivityDefinitionVersionBootTests : IDisposable
{
    private readonly ActivityDesignV2TestHarness harness = ActivityDesignV2TestHarness.Create();

    [Fact]
    public async Task Version_written_on_one_boot_reads_back_on_the_next_through_a_store_built_before_converters_register()
    {
        var firstBoot = new Boot();
        firstBoot.RegisterConverters();
        await new GroundworkAddActivityDefinitionCommand(
                firstBoot.Serializer, harness.Access, new GroundworkActivityDefinitionStore(harness.Store),
                new ImmediateDistributedLockProvider(), new FixedSystemClock(), new GroundworkDesignAtomicWrite(harness.Store))
            .Execute(new DesignOperationKey("boot-1"), Definition(), Version());

        var secondBoot = new Boot();
        var store = new GroundworkActivityDefinitionVersionStore(
            harness.Store, new GroundworkActivityDefinitionStore(harness.Store), secondBoot.Serializer);
        secondBoot.RegisterConverters();

        Assert.Contains("\"executionType\":\"Task\"", StoredVersionJson());
        Assert.Equal(ActivityExecutionType.Task, (await store.GetAsync("ver-1")).ExecutionType);
        Assert.Equal(ActivityExecutionType.Task, Assert.Single(await store.ListByDefinitionIdsAsync(["def-1"])).ExecutionType);
    }

    [Fact]
    public void Document_options_are_reused_until_the_payload_serializer_rebuilds_its_options()
    {
        var boot = new Boot();
        var beforeRegistration = GroundworkActivitiesDesignDocumentSerialization.Get(boot.Serializer);
        Assert.Same(beforeRegistration, GroundworkActivitiesDesignDocumentSerialization.Get(boot.Serializer));

        boot.RegisterConverters();
        var afterRegistration = GroundworkActivitiesDesignDocumentSerialization.Get(boot.Serializer);

        Assert.NotSame(beforeRegistration, afterRegistration);
        Assert.Contains(afterRegistration.Converters, converter => converter is JsonStringEnumConverter);
        Assert.True(afterRegistration.IsReadOnly);
    }

    public void Dispose() => harness.Dispose();

    private string StoredVersionJson() => Assert.Single(
        harness.Rows(ActivitiesDesignStorageManifest.ActivityDefinitionVersionDocumentKind)).ContentJson;

    private static ActivityDefinition Definition() => new()
    {
        Id = "def-1", ActivityTypeKey = "Acme.Send", Category = "General", DisplayName = "Send", TenantId = "tenant-a"
    };

    private static ActivityDefinitionVersion Version() => new("1.0.0", "def-1", executionType: ActivityExecutionType.Task)
    {
        Id = "ver-1", TenantId = "tenant-a", DescriptorType = "Acme.SendActivity",
        DescriptorPayload = JsonSerializer.SerializeToElement(new { kind = "send" }), SourceKind = "Json", SourceId = "asset-1"
    };

    /// <summary>One process lifetime: a fresh converter registry and the payload serializer that reads it.</summary>
    private sealed class Boot
    {
        private readonly JsonPayloadConverterRegistry registry = new();

        public Boot() => Serializer = new JsonPayloadSerializer(registry);

        public IPayloadSerializer Serializer { get; }

        // What JsonPayloadConvertersInitializingStartupTask does with the built-in converter source's enum converter.
        public void RegisterConverters() => registry.RegisterAll([new JsonStringEnumConverter()]);
    }

    private sealed class FixedSystemClock : ISystemClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    }
}

#pragma warning restore CS0618
