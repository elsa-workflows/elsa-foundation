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

    /// <summary>How many times <see cref="Refresh(IEnumerable{HttpRouteData})"/>/<see cref="Refresh(IEnumerable{string})"/> ran — proves the observer's HTTP-affecting gate.</summary>
    public int RefreshCount { get; private set; }

    /// <summary>When set, the next <c>Refresh</c> throws (after clearing the flag) — simulates a publish-failing refresh so the retry path can be asserted.</summary>
    public bool FailNextRefresh { get; set; }

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
        ThrowIfFailRequested();
        RefreshCount++;
        _routes.Clear();
        return AddRange(routes);
    }

    public ValueTask Refresh(IEnumerable<HttpRouteData> routes)
    {
        ThrowIfFailRequested();
        RefreshCount++;
        _routes.Clear();
        _routes.AddRange(routes);
        return ValueTask.CompletedTask;
    }

    // A failed refresh leaves the table untouched (the real RouteTable swaps atomically) and does not count.
    private void ThrowIfFailRequested()
    {
        if (!FailNextRefresh)
            return;
        FailNextRefresh = false;
        throw new InvalidOperationException("Simulated route-table refresh failure.");
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

internal static class Resolvers
{
    /// <summary>
    /// Builds a real <see cref="HttpEndpointRoutesResolver"/> over the given trigger-binding store and an
    /// optional bookmark store (empty by default), wiring the production expiry-aware lookup and a null logger.
    /// Keeps the resolver's construction in one place so tests that don't care about bookmarks stay terse.
    /// </summary>
    public static Elsa.Workflows.Runtime.Http.Services.HttpEndpointRoutesResolver Build(
        Elsa.Workflows.Runtime.Core.Contracts.IWorkflowTriggerBindingStore bindingStore,
        Elsa.Workflows.Runtime.Core.Services.InMemoryBookmarkStateStore? bookmarks = null) =>
        new(
            bindingStore,
            new Elsa.Workflows.Runtime.Core.Services.GlobalBookmarkStimulusLookup(bookmarks ?? new()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Elsa.Workflows.Runtime.Http.Services.HttpEndpointRoutesResolver>.Instance);
}

internal static class Bookmarks
{
    /// <summary>Builds a waiting HTTP-endpoint bookmark carrying the standard routing metadata (mid-flow suspension).</summary>
    public static BookmarkState HttpEndpoint(
        string workflowExecutionId,
        string template,
        string method,
        DateTimeOffset? expiresAt = null,
        IReadOnlyDictionary<string, string>? extraMetadata = null)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Elsa.Http.Core.HttpEndpointRouting.TemplateMetadataKey] = template,
            [Elsa.Http.Core.HttpEndpointRouting.MethodMetadataKey] = method.ToLowerInvariant(),
        };

        if (extraMetadata is not null)
            foreach (var (key, value) in extraMetadata)
                metadata[key] = value;

        return Build(workflowExecutionId, Elsa.Http.Core.HttpEndpointRouting.StimulusType, $"sha256:{template}:{method}", metadata, expiresAt);
    }

    /// <summary>Builds a non-HTTP bookmark (used to prove the resolver/observer ignore other stimulus types).</summary>
    public static BookmarkState Other(string workflowExecutionId, string stimulusType = "Event") =>
        Build(workflowExecutionId, stimulusType, $"sha256:{stimulusType}:{workflowExecutionId}", new Dictionary<string, string>(StringComparer.Ordinal));

    public static BookmarkState Build(
        string workflowExecutionId,
        string stimulusType,
        string stimulusHash,
        IReadOnlyDictionary<string, string> metadata,
        DateTimeOffset? expiresAt = null) =>
        new(
            BookmarkId: $"http-endpoint:{workflowExecutionId}:{stimulusHash}",
            WorkflowExecutionId: workflowExecutionId,
            ActivityExecutionId: $"ae-{workflowExecutionId}",
            ExecutableNodeId: $"node-{workflowExecutionId}",
            ResumeTargetId: "OnResume",
            StimulusType: stimulusType,
            StimulusHash: stimulusHash,
            Payload: null,
            Metadata: metadata,
            CreatedAt: DateTimeOffset.UnixEpoch,
            ExpiresAt: expiresAt);
}
