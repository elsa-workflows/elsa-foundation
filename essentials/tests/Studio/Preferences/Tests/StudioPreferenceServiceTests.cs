using System.Text.Json;
using Elsa.Studio.Preferences.Api;
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
            new AttentionPreferenceNamespace(),
            new HomeLayoutPreferenceNamespace()
        ]);
        _service = new(registry, new InMemoryStudioPreferenceStore(), TimeProvider.System);
    }

    [Fact]
    public async Task Quota_counts_the_retained_raw_utf8_value_including_whitespace_and_unicode()
    {
        var service = new StudioPreferenceService(new StudioPreferenceNamespaceRegistry([new RawQuotaNamespace()]), new InMemoryStudioPreferenceStore(), TimeProvider.System);
        var key = Key("raw-quota");
        var oversized = "{" + new string(' ', 65) + "\"n\":1}";
        await Assert.ThrowsAsync<StudioPreferenceQuotaExceededException>(async () =>
            await service.WriteAsync(key, new(1, Json(oversized)), StudioPreferenceWriteCondition.MustNotExist));
        Assert.Null(await service.FindAsync(key));
        var unicode = "{\"note\":\"" + new string('雪', 10) + "\"}";
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(unicode) < 64);
        var saved = await service.WriteAsync(key, new(1, Json(unicode)), StudioPreferenceWriteCondition.MustNotExist);
        Assert.Equal(unicode, saved.Value.GetRawText());
    }

    private sealed class RawQuotaNamespace : ObjectPreferenceNamespace
    {
        public override string Name => "raw-quota";
        public override int MaxBytes => 64;
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

    [Fact]
    public async Task DeleteRemovesTheUserDocument()
    {
        var key = Key(StudioPreferenceNamespaces.HomeLayout);
        await _service.WriteAsync(key, new(1, StudioHomeLayout.BuiltInValue), StudioPreferenceWriteCondition.MustNotExist);

        Assert.True(await _service.DeleteAsync(key));
        Assert.Null(await _service.FindAsync(key));
        Assert.False(await _service.DeleteAsync(key));
    }

    [Fact]
    public async Task HomeLayoutRejectsDuplicateIdsAndUnknownSizes()
    {
        var key = Key(StudioPreferenceNamespaces.HomeLayout);
        await Assert.ThrowsAsync<StudioPreferenceValidationException>(async () =>
            await _service.WriteAsync(key, new(1, Json("""{"widgets":[{"id":"a","size":"huge"}]}""")), StudioPreferenceWriteCondition.MustNotExist));
        await Assert.ThrowsAsync<StudioPreferenceValidationException>(async () =>
            await _service.WriteAsync(key, new(1, Json("""{"widgets":[{"id":"a","size":"small"},{"id":"a","size":"medium"}]}""")), StudioPreferenceWriteCondition.MustNotExist));

        var saved = await _service.WriteAsync(
            key,
            new(1, Json("""{"widgets":[{"id":"home.get-started","size":"L"}]}""")),
            StudioPreferenceWriteCondition.MustNotExist);
        Assert.Equal("home-layout", saved.Namespace);
        Assert.Equal(1, saved.Value.GetProperty("widgets").GetArrayLength());
    }

    private static StudioPreferenceKey Key(string @namespace) => new("user-1", "tenant-1", "studio-primary", @namespace);
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
