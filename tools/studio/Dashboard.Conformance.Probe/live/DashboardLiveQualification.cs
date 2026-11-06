using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using CShells.Features;
using Elsa.Agent.GitHubCopilot;
using Elsa.Agent.GitHubCopilot.Services;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Modularity.Api.Authorization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MinimalEndpoints;

// Compiled only by the dedicated owned live-host template. No production API is expanded.
[ShellFeature(name: "DashboardLiveQualification", DependsOn = [typeof(GitHubCopilotAgentFeature), "AcmeProcessing", "StudioDashboardWeaver"])]
public sealed class DashboardLiveQualificationFeature : GitHubCopilotAgentFeature
{
    public override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        var original = services.Single(item => item.ServiceType == typeof(IGitHubCopilotClientFactory));
        if (original.ImplementationType?.FullName != "Elsa.Agent.GitHubCopilot.Services.GitHubCopilotSdkClientFactory"
            || original.ImplementationType.Assembly != typeof(IGitHubCopilotClientFactory).Assembly)
            throw new InvalidOperationException("The live profile requires the actual SDK factory; protocol fixtures and replacements are refused.");
        services.Replace(ServiceDescriptor.Singleton<IGitHubCopilotClientFactory>(provider => new DashboardLiveFactory(
            (IGitHubCopilotClientFactory)ActivatorUtilities.CreateInstance(provider, original.ImplementationType),
            provider.GetRequiredService<DashboardLiveCounters>())));
    }
}

internal sealed class DashboardLiveCounters
{
    private static readonly Regex MeasuredRows = new(@"""rows""\s*:\s*\[", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(100));
    private readonly ConcurrentDictionary<string, long> counts = new(StringComparer.Ordinal);
    private string[] canaries = [];
    private long privacyFailures;
    internal void Count(string name) => counts.AddOrUpdate(name, 1, (_, value) => value + 1);
    internal object Snapshot() => new { category = "actual-sdk-forwarding", counts = counts.OrderBy(item => item.Key).ToDictionary(item => item.Key, item => item.Value),
        privacyFailures = Interlocked.Read(ref privacyFailures), physicalModelHttpRequests = "not exposed by public SDK factory seam",
        inferenceBoundary = "session.SendAsync starts the real SDK turn; native actions must add zero sends", provider = "github-copilot" };
    internal void Canaries(string[] values)
    {
        if (values.Length > 64 || values.Any(value => string.IsNullOrWhiteSpace(value) || value.Length is < 8 or > 256))
            throw new ArgumentException("Supply only bounded synthetic source-record identifiers.");
        Volatile.Write(ref canaries, values.Distinct(StringComparer.Ordinal).ToArray());
    }
    internal void Inspect(string content, bool result = false)
    {
        var forbidden = Volatile.Read(ref canaries).Any(value => content.Contains(value, StringComparison.Ordinal))
            || MeasuredRows.IsMatch(content.Replace("\\\"", "\"", StringComparison.Ordinal));
        if (result)
        {
            using var document = JsonDocument.Parse(content);
            forbidden |= HasMeasuredResult(document.RootElement);
        }
        if (!forbidden) return;
        Interlocked.Increment(ref privacyFailures);
        throw new InvalidOperationException("The live qualifier refused source result facts before SDK disclosure.");
    }
    private static bool HasMeasuredResult(JsonElement item) => item.ValueKind switch {
        JsonValueKind.Object => item.EnumerateObject().Any(member =>
            (member.Name.Equals("rows", StringComparison.OrdinalIgnoreCase) && member.Value.ValueKind == JsonValueKind.Array) || HasMeasuredResult(member.Value)),
        JsonValueKind.Array => item.EnumerateArray().Any(HasMeasuredResult),
        JsonValueKind.String => HasEmbeddedResult(item.GetString()), _ => false };
    private static bool HasEmbeddedResult(string? value)
    {
        // Guarded tools can return a JSON string. Inspect it in memory without logging/retaining it.
        if (string.IsNullOrWhiteSpace(value) || (value[0] != '{' && value[0] != '[')) return false;
        try { using var nested = JsonDocument.Parse(value); return HasMeasuredResult(nested.RootElement); }
        catch (JsonException) { return false; }
    }
}

internal static class DashboardLiveQualificationRoutes
{
    internal static void Map(IEndpointRouteBuilder endpoints, DashboardLiveCounters counters)
    {
        endpoints.MapGet("/__qualification/dashboard-live/counters", () => Results.Json(counters.Snapshot()))
            .WithHostOwner("Elsa.Workbench").WithAuthoringModel(EndpointAuthoringModels.MinimalApi).RequirePermission(ModuleManagementPermissionKeys.Read);
        endpoints.MapPost("/__qualification/dashboard-live/canaries", (string[] values) => { counters.Canaries(values); return Results.NoContent(); })
            .WithHostOwner("Elsa.Workbench").WithAuthoringModel(EndpointAuthoringModels.MinimalApi).RequirePermission(ModuleManagementPermissionKeys.Read);
    }
}

internal sealed class DashboardLiveFactory(IGitHubCopilotClientFactory inner, DashboardLiveCounters counters) : IGitHubCopilotClientFactory
{
    public IGitHubCopilotClient Create(GitHubCopilotClientRequest request)
    { counters.Count("client.create"); return new Client(inner.Create(request), counters); }
    private sealed class Client(IGitHubCopilotClient inner, DashboardLiveCounters counters) : IGitHubCopilotClient
    {
        public Task StartAsync(CancellationToken cancellationToken) { counters.Count("client.start"); return inner.StartAsync(cancellationToken); }
        public Task PingAsync(CancellationToken cancellationToken) { counters.Count("diagnostics.ping"); return inner.PingAsync(cancellationToken); }
        public Task<IReadOnlyCollection<string>> ListModelsAsync(CancellationToken cancellationToken)
        { counters.Count("diagnostics.models"); return inner.ListModelsAsync(cancellationToken); }
        public async Task<IGitHubCopilotSession> CreateSessionAsync(GitHubCopilotSessionRequest request, CancellationToken cancellationToken)
        { counters.Count("session.create"); return new Session(await inner.CreateSessionAsync(Wrap(request), cancellationToken), counters); }
        public async Task<IGitHubCopilotSession> ResumeSessionAsync(GitHubCopilotSessionRequest request, CancellationToken cancellationToken)
        { counters.Count("session.resume"); return new Session(await inner.ResumeSessionAsync(Wrap(request), cancellationToken), counters); }
        private GitHubCopilotSessionRequest Wrap(GitHubCopilotSessionRequest request)
        {
            counters.Inspect(request.SystemMessage ?? string.Empty);
            return request with { CustomTools = request.CustomTools?.Select(tool => (AIFunction)new Function(tool, counters)).ToArray() };
        }
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
    private sealed class Session(IGitHubCopilotSession inner, DashboardLiveCounters counters) : IGitHubCopilotSession
    {
        public string SessionId => inner.SessionId;
        public string? WorkspacePath => inner.WorkspacePath;
        public async IAsyncEnumerable<GitHubCopilotStreamEvent> SendAsync(string prompt, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            counters.Inspect(prompt); counters.Count("session.send");
            await foreach (var item in inner.SendAsync(prompt, cancellationToken)) { counters.Count("event." + item.Kind); yield return item; }
        }
        public Task AbortAsync(CancellationToken cancellationToken) { counters.Count("session.abort"); return inner.AbortAsync(cancellationToken); }
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
    private sealed class Function(AIFunction inner, DashboardLiveCounters counters) : AIFunction
    {
        public override string Name => inner.Name;
        public override string Description => inner.Description;
        public override JsonElement JsonSchema => inner.JsonSchema;
        public override IReadOnlyDictionary<string, object?> AdditionalProperties => inner.AdditionalProperties;
        protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
        {
            counters.Count("tool.invoke." + Name);
            var result = await inner.InvokeAsync(arguments, cancellationToken);
            counters.Inspect(JsonSerializer.Serialize(result), result: true);
            counters.Count("tool.return." + Name);
            return result;
        }
    }
}
