using System.Text.Json;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Xunit;
using Elsa.Workflows.Design.Api.Endpoints.Structures.List;
using StructuresEndpoint = Elsa.Workflows.Design.Api.Endpoints.Structures.List.Endpoint;

namespace Elsa.Workflows.Design.Api.Tests.Unit;

public sealed class ListActivityStructuresHandlerTests
{
    private static readonly IActivityStructureHandler[] Handlers =
    [
        // Deliberately unsorted; "test.alpha" sorts after "test.Zebra" ordinally (uppercase first).
        new StubStructureHandler("test.alpha", "1.0.0", supportsScopedVariables: true, payloadType: typeof(StubAuthoredStructure)),
        new StubStructureHandler("test.Zebra", "2.0.0", supportsScopedVariables: false, payloadType: null)
    ];

    private static Task<ActivityStructuresResponse> HandleAsync() =>
        new StructuresEndpoint(Handlers).HandleAsync(new ListActivityStructures(), CancellationToken.None);

    [Fact]
    public async Task Registered_kinds_round_trip_in_ordinal_kind_order()
    {
        var response = await HandleAsync();

        Assert.Collection(
            response.Items,
            zebra =>
            {
                Assert.Equal("test.Zebra", zebra.Kind);
                Assert.Equal("2.0.0", zebra.SchemaVersion);
                Assert.False(zebra.SupportsScopedVariables);
            },
            alpha =>
            {
                Assert.Equal("test.alpha", alpha.Kind);
                Assert.Equal("1.0.0", alpha.SchemaVersion);
                Assert.True(alpha.SupportsScopedVariables);
            });
    }

    [Fact]
    public async Task Payload_schema_is_published_only_for_handlers_exposing_an_authored_payload_type()
    {
        var response = await HandleAsync();

        var alpha = Assert.Single(response.Items, item => item.Kind == "test.alpha");
        var zebra = Assert.Single(response.Items, item => item.Kind == "test.Zebra");

        Assert.Null(zebra.PayloadSchema);
        Assert.NotNull(alpha.PayloadSchema);

        // The payload schema follows the wire contract: camelCase property names.
        var payloadSchema = alpha.PayloadSchema!;
        var properties = payloadSchema.Value.GetProperty("properties");
        Assert.True(properties.TryGetProperty("childNodeIds", out _));
        Assert.True(properties.TryGetProperty("startNodeId", out _));
    }

    [Fact]
    public async Task Equal_kinds_are_ordered_ordinally_by_schema_version()
    {
        IActivityStructureHandler[] handlers =
        [
            new StubStructureHandler("test.same", "2.0.0", supportsScopedVariables: false, payloadType: null),
            new StubStructureHandler("test.same", "10.0.0", supportsScopedVariables: false, payloadType: null)
        ];

        var response = await new StructuresEndpoint(handlers).HandleAsync(new ListActivityStructures(), CancellationToken.None);

        // Ordinal string ordering: "10.0.0" precedes "2.0.0".
        Assert.Equal(["10.0.0", "2.0.0"], response.Items.Select(item => item.SchemaVersion));
    }

    [Fact]
    public async Task Fingerprint_is_canonical_and_stable_across_calls()
    {
        var first = await HandleAsync();
        var second = await HandleAsync();

        Assert.StartsWith("sha256:", first.Fingerprint, StringComparison.Ordinal);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
    }

    private sealed record StubAuthoredStructure(IReadOnlyCollection<string> ChildNodeIds, string? StartNodeId);

    private sealed class StubStructureHandler(string kind, string schemaVersion, bool supportsScopedVariables, Type? payloadType)
        : IActivityStructureHandler
    {
        public string Kind => kind;

        public string SchemaVersion => schemaVersion;

        public bool SupportsScopedVariables => supportsScopedVariables;

        public Type? AuthoredPayloadType => payloadType;

        public IReadOnlyCollection<ActivityChildProjection> ProjectChildren(ActivityNode activity) => [];

        public ActivityNode ReplaceChildren(ActivityNode activity, IReadOnlyCollection<ActivityChildProjection> childProjections) => activity;

        public ActivityNodeStructure CompileExecutableStructure(ActivityNode activity) =>
            throw new NotSupportedException("The structure registry must not compile structures.");
    }
}
