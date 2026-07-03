using System.Text.Json;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.EFCore.EntityHandlers;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Activities.Design.Tests.Unit;

/// <summary>
/// US3 / SC-002 (spec 006 T041) — the design domain persists a descriptor as an opaque
/// <c>(DescriptorType string, JsonElement DescriptorPayload)</c> pair and round-trips it through the
/// real SQLite store without ever resolving the descriptor type to a CLR type. The
/// <see cref="ActivityDefinitionVersionLoadingHandler"/> takes only an <c>IPayloadSerializer</c> — it
/// has no kind→type registry dependency — so the round-trip below works even when the
/// <c>DescriptorType</c> names a type that does not exist in any loaded assembly.
/// </summary>
public sealed class DescriptorPersistenceRoundTripTests
{
    // A descriptor-type FullName that intentionally resolves to NO CLR type. If any part of the
    // persistence path tried to Type.GetType()/deserialize-to-type it, the round-trip would fail.
    private const string OpaqueDescriptorType = "Acme.Never.Loaded.PhantomDescriptor";

    private readonly JsonPayloadSerializer _serializer = new(new JsonPayloadConverterRegistry());

    [Fact]
    public async Task ActivityDefinitionVersion_RoundTripsDescriptor_AsOpaqueTypeAndPayload()
    {
        var savingHandler = new ActivityDefinitionVersionSavingHandler(_serializer);
        var loadingHandler = new ActivityDefinitionVersionLoadingHandler(_serializer, NullLogger<ActivityDefinitionVersionLoadingHandler>.Instance);

        using var host = ActivitiesDesignTestHost.Create();

        var definitionId = Guid.NewGuid().ToString("N");
        var versionId = Guid.NewGuid().ToString("N");
        var payload = JsonSerializer.SerializeToElement(new { definitionId = "def-42", versionId = "ver-7", version = "2.0.0" });

        await using (var ctx = host.CreateContext())
        {
            ctx.ActivityDefinitions.Add(new ActivityDefinition
            {
                Id = definitionId,
                ActivityTypeKey = "Acme.Foo",
                Category = "Test"
            });

            var version = new ActivityDefinitionVersion("2.0.0", definitionId)
            {
                Id = versionId,
                DescriptorType = OpaqueDescriptorType,
                DescriptorPayload = payload,
                SourceKind = "Workflow",
                SourceId = "src-1"
            };

            // The saving handler serializes the JsonElement into the DescriptorPayloadSource column —
            // the same work the OnEntitySaving aggregator drives in production.
            await savingHandler.Handle(ctx, version, CancellationToken.None);
            ctx.ActivityDefinitionVersions.Add(version);
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = host.CreateContext())
        {
            var loaded = await ctx.ActivityDefinitionVersions.SingleAsync(v => v.Id == versionId);

            // The raw persisted form survived: the type string verbatim, the payload as serialized JSON.
            Assert.Equal(OpaqueDescriptorType, loaded.DescriptorType);
            Assert.False(string.IsNullOrWhiteSpace(loaded.DescriptorPayloadSource));

            // Loading parses the string back into a JsonElement — no type resolution.
            await loadingHandler.Handle(ctx, loaded, CancellationToken.None);

            Assert.Equal(JsonValueKind.Object, loaded.DescriptorPayload.ValueKind);
            Assert.Equal("def-42", loaded.DescriptorPayload.GetProperty("definitionId").GetString());
            Assert.Equal("ver-7", loaded.DescriptorPayload.GetProperty("versionId").GetString());
            Assert.Equal("2.0.0", loaded.DescriptorPayload.GetProperty("version").GetString());
        }
    }
}
