using System.Text.Json;
using Elsa.Studio.Preferences.Core;
using Elsa.Studio.Preferences.Core.Contracts;
using Elsa.Studio.Preferences.Core.Exceptions;
using Elsa.Studio.Preferences.Core.Models;
using Elsa.Studio.Preferences.Core.Services;
using Xunit;

namespace Elsa.Studio.Preferences.Tests;

public sealed class StudioPreferenceServiceTests
{
    private readonly StudioPreferenceService _service;

    public StudioPreferenceServiceTests()
    {
        var registry = new StudioPreferenceNamespaceRegistry(
        [
            new DashboardPreferenceNamespace(),
            new AttentionPreferenceNamespace()
        ]);
        _service = new(registry, new InMemoryStudioPreferenceStore(), TimeProvider.System);
    }

    [Fact]
    public async Task CreateAndUpdateRequireMatchingRevisions()
    {
        var key = Key(StudioPreferenceNamespaces.Dashboard);
        var created = await _service.WriteAsync(key, new(1, Json("{\"refreshIntervalMinutes\":5}")), StudioPreferenceWriteCondition.MustNotExist);

        Assert.Equal("rev-1", created.Revision);

        await Assert.ThrowsAsync<StudioPreferenceConflictException>(async () =>
            await _service.WriteAsync(key, new(1, Json("{}")), StudioPreferenceWriteCondition.MustNotExist));
        await Assert.ThrowsAsync<StudioPreferenceConflictException>(async () =>
            await _service.WriteAsync(key, new(1, Json("{}")), StudioPreferenceWriteCondition.Matches("rev-0")));

        var updated = await _service.WriteAsync(key, new(1, Json("{}")), StudioPreferenceWriteCondition.Matches(created.Revision));
        Assert.Equal("rev-2", updated.Revision);
    }

    [Fact]
    public async Task ScopeSeparatesUserTenantAndStudioHost()
    {
        var first = Key(StudioPreferenceNamespaces.Dashboard);
        await _service.WriteAsync(first, new(1, Json("{\"owner\":\"first\"}")), StudioPreferenceWriteCondition.MustNotExist);

        Assert.Null(await _service.FindAsync(first with { SubjectId = "other-user" }));
        Assert.Null(await _service.FindAsync(first with { TenantId = "other-tenant" }));
        Assert.Null(await _service.FindAsync(first with { StudioHostId = "other-studio" }));
    }

    [Fact]
    public async Task RejectsUnknownNamespaceInvalidShapeAndQuotaOverflow()
    {
        await Assert.ThrowsAsync<StudioPreferenceNamespaceNotFoundException>(async () =>
            await _service.FindAsync(Key("unregistered")));
        await Assert.ThrowsAsync<StudioPreferenceValidationException>(async () =>
            await _service.WriteAsync(Key(StudioPreferenceNamespaces.Attention), new(1, Json("[]")), StudioPreferenceWriteCondition.MustNotExist));

        var oversized = Json($"{{\"value\":\"{new string('x', AttentionPreferenceNamespace.DefaultMaxBytes)}\"}}");
        await Assert.ThrowsAsync<StudioPreferenceQuotaExceededException>(async () =>
            await _service.WriteAsync(Key(StudioPreferenceNamespaces.Attention), new(1, oversized), StudioPreferenceWriteCondition.MustNotExist));
    }

    [Fact]
    public async Task RejectsInvalidScopeAndFutureSchema()
    {
        await Assert.ThrowsAsync<StudioPreferenceScopeException>(async () =>
            await _service.FindAsync(Key(StudioPreferenceNamespaces.Dashboard) with { SubjectId = "" }));
        await Assert.ThrowsAsync<StudioPreferenceValidationException>(async () =>
            await _service.WriteAsync(Key(StudioPreferenceNamespaces.Dashboard), new(2, Json("{}")), StudioPreferenceWriteCondition.MustNotExist));
    }

    private static StudioPreferenceKey Key(string @namespace) => new("user-1", "tenant-1", "studio-primary", @namespace);
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
