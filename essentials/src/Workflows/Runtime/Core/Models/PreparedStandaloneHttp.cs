using System.Text.Json;

namespace Elsa.Workflows.Runtime.Core.Models;

internal static class PreparedStandaloneHttp
{
    internal static bool IsHttp(RuntimeStandaloneIngressSource source) =>
        source.SourceFamily == "Http" && TryObject(source.CanonicalPayload, "trigger", out var trigger) &&
        trigger.TryGetProperty("typeKey", out var type) && type.ValueKind == JsonValueKind.String && type.ValueEquals("http");

    internal static bool IsAsyncFanOut(RuntimeStandaloneIngressSource source) => HasMode(source, "AsyncFanOut");

    internal static bool IsWorkflowResponse(RuntimeStandaloneIngressSource source) => HasMode(source, "WorkflowResponse");

    private static bool HasMode(RuntimeStandaloneIngressSource source, string expected) =>
        IsHttp(source) && TryObject(source.CanonicalPayload, "trigger", out var trigger) &&
        TryObject(trigger, "configuration", out var configuration) && TryObject(configuration, "http", out var http) &&
        http.TryGetProperty("mode", out var mode) && mode.ValueKind == JsonValueKind.String && mode.ValueEquals(expected);

    internal static bool HasEmptyWaitObservation(RuntimeStandaloneIngressSource source, RuntimeIngressWaitCandidate? candidate) =>
        (IsAsyncFanOut(source) || IsWorkflowResponse(source)) && candidate?.ObservedWaits is { Count: 0 };

    internal static bool HasVerifiedEmptyWaits(RuntimePostCommitOutboxItem item) =>
        item.StandaloneIngress is { } source && item.SubscriptionDelivery?.WaitDelivery is { Recipients.Count: 0 } cohort &&
        HasEmptyWaitObservation(source, cohort.Candidate);

    internal static (string Method, string Path)? AcceptedRoute(RuntimePostCommitOutboxItem item)
    {
        if (!HasVerifiedEmptyWaits(item) || item.SubscriptionDelivery!.WaitDelivery!.Candidate.Input is not { } request ||
            request.ValueKind != JsonValueKind.Object ||
            !request.TryGetProperty("Method", out var method) || method.ValueKind != JsonValueKind.String ||
            !request.TryGetProperty("Path", out var path) || path.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(method.GetString()) || string.IsNullOrWhiteSpace(path.GetString()))
            return null;
        return (method.GetString()!, path.GetString()!);
    }

    private static bool TryObject(JsonElement parent, string name, out JsonElement child)
    {
        child = default;
        return parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out child) && child.ValueKind == JsonValueKind.Object;
    }
}
