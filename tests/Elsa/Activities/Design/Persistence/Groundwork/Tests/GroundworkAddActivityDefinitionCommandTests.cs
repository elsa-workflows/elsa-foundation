using System.Text.Json;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Exceptions;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Persistence.Groundwork.Services;
using Elsa.Locking.Core;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Primitives.Contracts;
using Xunit;

#pragma warning disable CS0618

namespace Elsa.Activities.Design.Persistence.Groundwork.Tests;

public sealed class GroundworkAddActivityDefinitionCommandTests
{
    private static readonly FakePayloadSerializer Payloads = new();
    private static readonly DateTimeOffset FixedNow = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Mismatched_version_tenant_rejects_the_complete_batch_before_staging()
    {
        using var harness = ActivityDesignV2TestHarness.Create();
        var command = CreateDefinitionCommand(harness);
        await Assert.ThrowsAsync<InvalidOperationException>(() => command.Execute(
            new DesignOperationKey("tenant-mismatch"), Definition("def-1"), Version("def-1", "ver-1", tenantId: "tenant-b")));
        Assert.Empty(harness.Rows(ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind));
        Assert.Empty(harness.Rows(ActivitiesDesignStorageManifest.ActivityDefinitionVersionDocumentKind));
        Assert.Empty(harness.Rows(ActivitiesDesignStorageManifest.DesignOperationDocumentKind));
    }

    [Fact]
    public async Task Add_version_rejects_explicit_wrong_tenant_before_store_io()
    {
        using var harness = ActivityDesignV2TestHarness.Create();
        var command = CreateVersionCommand(harness);
        await Assert.ThrowsAsync<InvalidOperationException>(() => command.Execute(
            new DesignOperationKey("tenant-mismatch"), Version("def-1", "ver-1", tenantId: "tenant-b")));
        Assert.Empty(harness.Rows(ActivitiesDesignStorageManifest.ActivityDefinitionVersionDocumentKind));
        Assert.Empty(harness.Rows(ActivitiesDesignStorageManifest.DesignOperationDocumentKind));
    }

    [Fact]
    public async Task Create_commits_definition_and_version_and_returns_the_authoritative_ids()
    {
        using var harness = ActivityDesignV2TestHarness.Create();
        var result = await CreateDefinitionCommand(harness).Execute(
            new DesignOperationKey("create-1"), Definition("def-1"), Version("def-1", "ver-1"));
        Assert.Equal("def-1", result.DefinitionId);
        Assert.Equal("ver-1", result.VersionId);
        Assert.Equal("1.0.0", result.Version);
        Assert.Equal("Acme.Send", (await new GroundworkActivityDefinitionStore(harness.Store).GetAsync("def-1")).ActivityTypeKey);
        var persistedVersion = await CreateVersionStore(harness).GetAsync("ver-1");
        Assert.Equal("def-1", persistedVersion.DefinitionId);
        Assert.Equal("Acme.SendActivity", persistedVersion.DescriptorType);
        Assert.Equal(FixedNow, persistedVersion.LastModifiedAt);
    }

    [Fact]
    public async Task Create_exact_replay_returns_original_ids_without_staging_the_second_request()
    {
        using var harness = ActivityDesignV2TestHarness.Create();
        var command = CreateDefinitionCommand(harness);
        var first = await command.Execute(new DesignOperationKey("create-replay"), Definition("def-1"), Version("def-1", "ver-1"));
        var replay = await command.Execute(new DesignOperationKey("create-replay"), Definition("def-2"), Version("def-2", "ver-2"));
        Assert.Equal(first, replay);
        Assert.Equal("def-1", replay.DefinitionId);
        Assert.Equal("ver-1", replay.VersionId);
        Assert.Single(harness.Rows(ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind));
        Assert.Single(harness.Rows(ActivitiesDesignStorageManifest.ActivityDefinitionVersionDocumentKind));
        Assert.Single(harness.Rows(ActivitiesDesignStorageManifest.DesignOperationDocumentKind));
    }

    [Fact]
    public async Task Create_changed_material_for_the_same_key_conflicts_without_mutation()
    {
        using var harness = ActivityDesignV2TestHarness.Create();
        var command = CreateDefinitionCommand(harness);
        await command.Execute(new DesignOperationKey("create-conflict"), Definition("def-1"), Version("def-1", "ver-1"));
        await Assert.ThrowsAsync<GroundworkDesignOperationConflictException>(() => command.Execute(
            new DesignOperationKey("create-conflict"), Definition("def-2", category: "Changed"), Version("def-2", "ver-2")));
        Assert.Single(harness.Rows(ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind));
        Assert.Single(harness.Rows(ActivitiesDesignStorageManifest.ActivityDefinitionVersionDocumentKind));
        Assert.Single(harness.Rows(ActivitiesDesignStorageManifest.DesignOperationDocumentKind));
    }

    [Fact]
    public async Task Create_rejects_a_different_operation_key_that_collides_with_an_existing_activity_type()
    {
        using var harness = ActivityDesignV2TestHarness.Create();
        var command = CreateDefinitionCommand(harness);
        await command.Execute(new DesignOperationKey("create-first"), Definition("def-1"), Version("def-1", "ver-1"));
        await Assert.ThrowsAsync<ArgumentException>(() => command.Execute(
            new DesignOperationKey("create-collision"), Definition("def-2"), Version("def-2", "ver-2")));
        Assert.Single(harness.Rows(ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind));
        Assert.Single(harness.Rows(ActivitiesDesignStorageManifest.ActivityDefinitionVersionDocumentKind));
    }

    [Fact]
    public async Task Create_rejects_a_provider_write_failure_without_persisting_any_document()
    {
        using var harness = ActivityDesignV2TestHarness.Create();
        var command = CreateDefinitionCommand(harness);
        await Assert.ThrowsAnyAsync<Exception>(() => command.Execute(
            new DesignOperationKey("create-rejected"), Definition("def-1"), Version("def-1", "ver-1"), new CancellationToken(true)));
        Assert.Empty(harness.Rows(ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind));
        Assert.Empty(harness.Rows(ActivitiesDesignStorageManifest.ActivityDefinitionVersionDocumentKind));
        Assert.Empty(harness.Rows(ActivitiesDesignStorageManifest.DesignOperationDocumentKind));
    }

    [Fact]
    public async Task Add_version_commits_and_returns_the_authoritative_id()
    {
        using var harness = ActivityDesignV2TestHarness.Create();
        var result = await CreateVersionCommand(harness).Execute(new DesignOperationKey("version-1"), Version("def-1", "ver-1"));
        Assert.Equal("ver-1", result.VersionId);
        Assert.Equal("def-1", result.DefinitionId);
        Assert.Single(harness.Rows(ActivitiesDesignStorageManifest.ActivityDefinitionVersionDocumentKind));
        Assert.Single(harness.Rows(ActivitiesDesignStorageManifest.DesignOperationDocumentKind));
    }

    [Fact]
    public async Task Add_version_exact_replay_returns_original_id_without_staging_the_second_request()
    {
        using var harness = ActivityDesignV2TestHarness.Create();
        var command = CreateVersionCommand(harness);
        var first = await command.Execute(new DesignOperationKey("version-replay"), Version("def-1", "ver-1"));
        var replay = await command.Execute(new DesignOperationKey("version-replay"), Version("def-1", "ver-2"));
        Assert.Equal(first, replay);
        Assert.Equal("ver-1", replay.VersionId);
        Assert.Equal("def-1", replay.DefinitionId);
        Assert.Single(harness.Rows(ActivitiesDesignStorageManifest.ActivityDefinitionVersionDocumentKind));
    }

    [Fact]
    public async Task Add_version_changed_material_for_the_same_key_conflicts_without_mutation()
    {
        using var harness = ActivityDesignV2TestHarness.Create();
        var command = CreateVersionCommand(harness);
        await command.Execute(new DesignOperationKey("version-conflict"), Version("def-1", "ver-1"));
        await Assert.ThrowsAsync<GroundworkDesignOperationConflictException>(() => command.Execute(
            new DesignOperationKey("version-conflict"), Version("def-1", "ver-2", version: "2.0.0")));
        Assert.Single(harness.Rows(ActivitiesDesignStorageManifest.ActivityDefinitionVersionDocumentKind));
    }

    [Fact]
    public async Task Add_version_rejects_a_different_operation_key_that_collides_with_an_existing_semantic_version()
    {
        using var harness = ActivityDesignV2TestHarness.Create();
        var command = CreateVersionCommand(harness);
        await command.Execute(new DesignOperationKey("version-first"), Version("def-1", "ver-1"));
        await Assert.ThrowsAsync<ActivityDefinitionVersionConflictException>(() => command.Execute(
            new DesignOperationKey("version-collision"), Version("def-1", "ver-2")));
        Assert.Single(harness.Rows(ActivitiesDesignStorageManifest.ActivityDefinitionVersionDocumentKind));
    }

    [Fact]
    public async Task Add_version_rejects_a_provider_write_failure_without_persisting_a_document()
    {
        using var harness = ActivityDesignV2TestHarness.Create();
        await Assert.ThrowsAnyAsync<Exception>(() => CreateVersionCommand(harness).Execute(
            new DesignOperationKey("version-rejected"), Version("def-1", "ver-1"), new CancellationToken(true)));
        Assert.Empty(harness.Rows(ActivitiesDesignStorageManifest.ActivityDefinitionVersionDocumentKind));
        Assert.Empty(harness.Rows(ActivitiesDesignStorageManifest.DesignOperationDocumentKind));
    }

    [Fact]
    public async Task Create_observes_caller_cancellation_before_the_atomic_attempt()
    {
        using var harness = ActivityDesignV2TestHarness.Create();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateDefinitionCommand(harness).Execute(
            new DesignOperationKey("create-cancelled"), Definition("def-1"), Version("def-1", "ver-1"), cancellation.Token));
        Assert.Empty(harness.Rows(ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind));
        Assert.Empty(harness.Rows(ActivitiesDesignStorageManifest.ActivityDefinitionVersionDocumentKind));
        Assert.Empty(harness.Rows(ActivitiesDesignStorageManifest.DesignOperationDocumentKind));
    }

    [Fact]
    public async Task Add_version_observes_caller_cancellation_before_the_atomic_attempt()
    {
        using var harness = ActivityDesignV2TestHarness.Create();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateVersionCommand(harness).Execute(
            new DesignOperationKey("version-cancelled"), Version("def-1", "ver-1"), cancellation.Token));
        Assert.Empty(harness.Rows(ActivitiesDesignStorageManifest.ActivityDefinitionVersionDocumentKind));
        Assert.Empty(harness.Rows(ActivitiesDesignStorageManifest.DesignOperationDocumentKind));
    }

    [Fact]
    public async Task Create_reconciles_a_lost_commit_acknowledgement_and_retries_from_its_durable_result()
    {
        using var harness = ActivityDesignV2TestHarness.Create();
        var command = CreateDefinitionCommand(harness);
        var first = await command.Execute(new DesignOperationKey("create-lost-acknowledgement"), Definition("def-1"), Version("def-1", "ver-1"));
        var replay = await command.Execute(new DesignOperationKey("create-lost-acknowledgement"), Definition("def-2"), Version("def-2", "ver-2"));
        Assert.Equal(first, replay);
        Assert.Equal("def-1", replay.DefinitionId);
        Assert.Equal("ver-1", replay.VersionId);
        Assert.Single(harness.Rows(ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind));
        Assert.Single(harness.Rows(ActivitiesDesignStorageManifest.ActivityDefinitionVersionDocumentKind));
    }

    [Fact]
    public async Task Add_version_reconciles_a_lost_commit_acknowledgement_and_retries_from_its_durable_result()
    {
        using var harness = ActivityDesignV2TestHarness.Create();
        var command = CreateVersionCommand(harness);
        var first = await command.Execute(new DesignOperationKey("version-lost-acknowledgement"), Version("def-1", "ver-1"));
        var replay = await command.Execute(new DesignOperationKey("version-lost-acknowledgement"), Version("def-1", "ver-2"));
        Assert.Equal(first, replay);
        Assert.Equal("ver-1", replay.VersionId);
        Assert.Single(harness.Rows(ActivitiesDesignStorageManifest.ActivityDefinitionVersionDocumentKind));
    }

    [Fact]
    public async Task Create_stamps_CreatedAt_and_LastModifiedAt_on_definition_and_first_version()
    {
        using var harness = ActivityDesignV2TestHarness.Create();
        var definition = Definition("def-1");
        var version = Version("def-1", "ver-1");
        await CreateDefinitionCommand(harness).Execute(new DesignOperationKey("create-timestamps"), definition, version);
        var createdDefinition = await new GroundworkActivityDefinitionStore(harness.Store).GetAsync("def-1");
        var createdVersion = await CreateVersionStore(harness).GetAsync("ver-1");
        Assert.Equal(FixedNow, createdDefinition.CreatedAt);
        Assert.Equal(FixedNow, createdDefinition.LastModifiedAt);
        Assert.Equal(FixedNow, createdVersion.CreatedAt);
        Assert.Equal(FixedNow, createdVersion.LastModifiedAt);
    }

    [Fact]
    public async Task Add_version_stamps_CreatedAt_and_LastModifiedAt()
    {
        using var harness = ActivityDesignV2TestHarness.Create();
        var version = Version("def-1", "ver-1");
        await CreateVersionCommand(harness).Execute(new DesignOperationKey("version-timestamps"), version);
        var persisted = await CreateVersionStore(harness).GetAsync("ver-1");
        Assert.Equal(FixedNow, persisted.CreatedAt);
        Assert.Equal(FixedNow, persisted.LastModifiedAt);
    }

    private static GroundworkAddActivityDefinitionCommand CreateDefinitionCommand(ActivityDesignV2TestHarness harness) =>
        new(Payloads, harness.Access, new GroundworkActivityDefinitionStore(harness.Store),
            new ImmediateDistributedLockProvider(), new FixedSystemClock(FixedNow), new GroundworkDesignAtomicWrite(harness.Store));

    private static GroundworkAddActivityDefinitionVersionCommand CreateVersionCommand(ActivityDesignV2TestHarness harness) =>
        new(Payloads, harness.Access, CreateVersionStore(harness), new ImmediateDistributedLockProvider(),
            new FixedSystemClock(FixedNow), new GroundworkDesignAtomicWrite(harness.Store));

    private static GroundworkActivityDefinitionVersionStore CreateVersionStore(ActivityDesignV2TestHarness harness) =>
        new(harness.Store, new GroundworkActivityDefinitionStore(harness.Store), Payloads);

    private static ActivityDefinition Definition(string id, string category = "General", string tenantId = "tenant-a") => new()
    {
        Id = id, ActivityTypeKey = "Acme.Send", Category = category, DisplayName = "Send", TenantId = tenantId
    };

    private static ActivityDefinitionVersion Version(
        string definitionId,
        string id,
        string version = "1.0.0",
        string? tenantId = "tenant-a") => new(version, definitionId)
    {
        Id = id, TenantId = tenantId, DescriptorType = "Acme.SendActivity",
        DescriptorPayload = JsonSerializer.SerializeToElement(new { kind = "send" }), SourceKind = "Json", SourceId = "asset-1"
    };

    private sealed class FixedSystemClock(DateTimeOffset now) : ISystemClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }
}

#pragma warning restore CS0618
