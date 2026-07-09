using System.Collections;
using System.Text.Json;
using Elsa.Http.Core.Contracts;
using Elsa.Http.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Http.Tests;

internal static class TestNodes
{
    /// <summary>A minimal, valid root <see cref="ExecutableNode"/> — the observer path never inspects it.</summary>
    public static ExecutableNode Root(string nodeId = "root")
    {
        using var document = JsonDocument.Parse("""{"type":"test"}""");
        return new ExecutableNode(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: "test",
            activityTypeVersion: "1.0.0",
            descriptorType: "test",
            descriptorPayload: document.RootElement.Clone(),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>());
    }
}

/// <summary>Minimal in-memory <see cref="IRouteTable"/> for unit scope — mirrors the real refresh/enumerate semantics.</summary>
internal sealed class FakeRouteTable : IRouteTable
{
    private readonly List<HttpRouteData> _routes = new();

    public IReadOnlyList<string> RouteTemplates => _routes.Select(r => r.Route).ToArray();

    public ValueTask Add(string route) => Add(new HttpRouteData(route));

    public ValueTask Add(HttpRouteData httpRouteData)
    {
        _routes.Add(httpRouteData);
        return ValueTask.CompletedTask;
    }

    public ValueTask Remove(string route)
    {
        _routes.RemoveAll(r => StringComparer.Ordinal.Equals(r.Route, route));
        return ValueTask.CompletedTask;
    }

    public async ValueTask AddRange(IEnumerable<string> routes)
    {
        foreach (var route in routes)
            await Add(route);
    }

    public ValueTask Refresh(IEnumerable<string> routes)
    {
        _routes.Clear();
        return AddRange(routes);
    }

    public ValueTask Refresh(IEnumerable<HttpRouteData> routes)
    {
        _routes.Clear();
        _routes.AddRange(routes);
        return ValueTask.CompletedTask;
    }

    public async ValueTask RemoveRange(IEnumerable<string> routes)
    {
        foreach (var route in routes)
            await Remove(route);
    }

    public IEnumerator<HttpRouteData> GetEnumerator() => _routes.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

internal static class Bindings
{
    /// <summary>Builds an HTTP-endpoint trigger binding carrying the standard routing metadata.</summary>
    public static WorkflowTriggerBinding HttpEndpoint(string artifactId, string nodeId, string template, string method, string? definitionId = null)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Elsa.Http.Core.HttpEndpointRouting.TemplateMetadataKey] = template,
            [Elsa.Http.Core.HttpEndpointRouting.MethodMetadataKey] = method.ToLowerInvariant(),
        };

        return Build(artifactId, nodeId, Elsa.Http.Core.HttpEndpointRouting.StimulusType, $"sha256:{template}:{method}", metadata, definitionId);
    }

    /// <summary>Builds a non-HTTP binding (used to prove the resolver ignores other stimulus types).</summary>
    public static WorkflowTriggerBinding Other(string artifactId, string nodeId, string stimulusType = "Event") =>
        Build(artifactId, nodeId, stimulusType, $"sha256:{stimulusType}:{nodeId}", new Dictionary<string, string>(StringComparer.Ordinal));

    public static WorkflowTriggerBinding Build(
        string artifactId,
        string nodeId,
        string stimulusType,
        string stimulusHash,
        IReadOnlyDictionary<string, string> metadata,
        string? definitionId = null) =>
        new(
            TriggerBindingId: WorkflowTriggerBinding.BuildId(artifactId, nodeId, stimulusHash),
            ArtifactId: artifactId,
            DefinitionId: definitionId ?? $"def-{artifactId}",
            ArtifactVersion: "1.0.0",
            ArtifactHash: $"sha256:{artifactId}",
            ExecutableNodeId: nodeId,
            StimulusType: stimulusType,
            StimulusHash: stimulusHash,
            CorrelationScope: null,
            Metadata: metadata,
            CreatedAt: DateTimeOffset.UnixEpoch);
}
