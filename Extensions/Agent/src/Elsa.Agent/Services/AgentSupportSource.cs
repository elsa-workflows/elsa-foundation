using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Agent.Core;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Agent.Services;

/// <summary>Projects the ordinary registries; never prepares tools or invokes a context/disclosure callback.</summary>
internal sealed class DefaultAgentSupportSource(
    IAgentToolRegistry tools,
    IEnumerable<IAgentContextProvider> sources,
    IAgentToolAuthorizer authorizer,
    IAgentPolicyEvaluator policyEvaluator,
    AgentEffectsAdmission effects,
    IOptions<AgentContextOptions> options,
    TimeProvider timeProvider,
    ILogger<DefaultAgentSupportSource> logger) : IAgentSupportSource
{
    private const int MaxItems = 64;
    private const int MaxValues = 8;
    private const int MaxIdentifierLength = 128;
    private const int MaxContentLength = 24 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public async Task<AgentSupportView> ReadAsync(string actorId, string tenantId, AgentPolicy policy, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentNullException.ThrowIfNull(policy);
        cancellationToken.ThrowIfCancellationRequested();
        // Copy the supplied scope, not global startup state; no session policy is invented here.
        policy = policy with { AllowedContextKinds = policy.AllowedContextKinds.ToArray(), DeniedCapabilityIds = policy.DeniedCapabilityIds.ToArray() };
        using var deadline = new CancellationTokenSource(options.Value.SourceTimeout, timeProvider);
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        var token = bounded.Token;
        var complete = true;
        var known = false;
        var contextRows = new List<(AgentContextProviderDescriptor Descriptor, AgentContextSupportView View)>();
        var toolRows = new List<(IAgentTool Tool, AgentToolDescriptor Descriptor, AgentToolSupportView View)>();

        try
        {
            var installed = sources.Select(source => source.Descriptor).Take(MaxItems + 1).ToArray();
            if (installed.Length > MaxItems) complete = false;
            var identities = new HashSet<string>(StringComparer.Ordinal);
            foreach (var descriptor in installed.Take(MaxItems))
            {
                AgentModuleCodes.Validate(descriptor.ModuleCodes);
                if (!identities.Add(descriptor.Id)) throw new AgentContributionException("Installed context identities are ambiguous.");
                if (!Identifier(descriptor.Id) || !Identifier(descriptor.ModuleId) || !Identifier(descriptor.ScopeKind)
                    || !Values(descriptor.ContextKinds) || !Values(descriptor.SchemaVersions) || !Values(descriptor.ResourceTypes))
                { complete = false; continue; }
                var snapshot = descriptor with
                {
                    ContextKinds = Array.AsReadOnly(descriptor.ContextKinds.ToArray()),
                    SchemaVersions = Array.AsReadOnly(descriptor.SchemaVersions.ToArray()),
                    ResourceTypes = Array.AsReadOnly(descriptor.ResourceTypes.ToArray())
                };
                contextRows.Add((snapshot, new(snapshot.Id, snapshot.ModuleId, snapshot.ScopeKind,
                    Array.AsReadOnly(snapshot.ContextKinds.ToArray()), Array.AsReadOnly(snapshot.SchemaVersions.ToArray()),
                    Array.AsReadOnly(snapshot.ResourceTypes.ToArray()), AgentSupportStatus.Unknown, AgentSupportStatus.Unchecked)));
            }
            known = true;
        }
        catch (Exception failure)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // A malformed/ambiguous source set cannot be represented as complete empty discovery.
            contextRows.Clear(); complete = false; Diagnose(failure);
        }

        try
        {
            var installed = await AwaitAsync(() => tools.ListAsync(token), token);
            var snapshot = installed.Take(MaxItems + 1).Select(tool => (Tool: tool, Descriptor: tool.Descriptor)).ToArray();
            if (snapshot.Length + contextRows.Count > MaxItems) complete = false;
            var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (tool, descriptor) in snapshot.Take(Math.Max(0, MaxItems - contextRows.Count)))
            {
                AgentModuleCodes.Validate(descriptor.ModuleCodes);
                if (!identities.Add(descriptor.Name)) throw new AgentContributionException("Installed tool identities are ambiguous.");
                if (!Identifier(descriptor.Name) || !Identifier(descriptor.ModuleId) || !Identifier(descriptor.ContractVersion)
                    || !Enum.IsDefined(descriptor.ExecutionLocation))
                { complete = false; continue; }
                // Freeze authority metadata before any owner await; no callback can mutate accepted fields.
                var frozen = descriptor with
                {
                    RequiredPermissions = Array.AsReadOnly(descriptor.RequiredPermissions.ToArray()),
                    AgentScopes = Array.AsReadOnly(descriptor.AgentScopes.ToArray())
                };
                toolRows.Add((tool, frozen, new(frozen.Name, frozen.ModuleId, frozen.ContractVersion,
                    frozen.ExecutionLocation, AgentSupportStatus.Unknown, AgentSupportStatus.Unchecked)));
            }
            known = true;
        }
        catch (Exception failure)
        {
            cancellationToken.ThrowIfCancellationRequested();
            toolRows.Clear(); complete = false; Diagnose(failure);
        }
        cancellationToken.ThrowIfCancellationRequested();

        for (var index = 0; index < contextRows.Count; index++)
        {
            var row = contextRows[index];
            try
            {
                var unavailable = row.Descriptor.ModuleCodes is null ? null
                    : await AwaitAsync(() => effects.CheckAsync(token).AsTask(), token);
                token.ThrowIfCancellationRequested();
                contextRows[index] = (row.Descriptor, row.View with
                { Admission = unavailable is null ? AgentSupportStatus.AvailableHint : AgentSupportStatus.Withheld });
            }
            catch (Exception failure)
            { cancellationToken.ThrowIfCancellationRequested(); complete = false; Diagnose(failure); }
        }
        for (var index = 0; index < toolRows.Count; index++)
        {
            var row = toolRows[index];
            try
            {
                var unavailable = await AwaitAsync(() => effects.CheckPreparationAsync(row.Descriptor, token).AsTask(), token);
                var policyAllowed = await AwaitAsync(() => policyEvaluator.EvaluateAvailabilityAsync(policy, token).AsTask(), token);
                var capability = await AwaitAsync(() => policyEvaluator.EvaluateCapabilityAsync(policy, row.Descriptor.Name, token).AsTask(), token);
                token.ThrowIfCancellationRequested();
                if (unavailable is not null || !policyAllowed.Allowed || !capability.Allowed)
                {
                    toolRows[index] = (row.Tool, row.Descriptor, row.View with { Admission = AgentSupportStatus.Withheld });
                    continue;
                }
                var authority = await AwaitAsync(() => AgentToolAccess.AuthorizeAsync(row.Tool, row.Descriptor, authorizer,
                    actorId, tenantId, null, token).AsTask(), token);
                // The owner's targetless check can await external I/O. It never freezes host limits.
                unavailable = await AwaitAsync(() => effects.CheckPreparationAsync(row.Descriptor, token).AsTask(), token);
                token.ThrowIfCancellationRequested();
                toolRows[index] = (row.Tool, row.Descriptor, row.View with
                {
                    Admission = unavailable is null ? AgentSupportStatus.AvailableHint : AgentSupportStatus.Withheld,
                    Authority = authority.Allowed ? AgentSupportStatus.AvailableHint : AgentSupportStatus.Withheld
                });
            }
            catch (Exception failure)
            {
                cancellationToken.ThrowIfCancellationRequested();
                complete = false; Diagnose(failure);
                toolRows[index] = (row.Tool, row.Descriptor, row.View with { Authority = AgentSupportStatus.Unknown });
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        var result = new AgentSupportView(complete ? AgentSupportCompleteness.Complete
            : known ? AgentSupportCompleteness.Incomplete : AgentSupportCompleteness.Unavailable,
            Array.AsReadOnly(toolRows.Select(row => row.View).ToArray()), Array.AsReadOnly(contextRows.Select(row => row.View).ToArray()));
        // Bound the complete wire projection, including escaped strings. Truncation is always explicit.
        while (JsonSerializer.Serialize(result, JsonOptions).Length > MaxContentLength)
            result = result.Tools.Count > 0
                ? result with { Completeness = AgentSupportCompleteness.Incomplete, Tools = Array.AsReadOnly(result.Tools.SkipLast(1).ToArray()) }
                : result with { Completeness = AgentSupportCompleteness.Incomplete, Contexts = Array.AsReadOnly(result.Contexts.SkipLast(1).ToArray()) };
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    private static bool Identifier(string? value) => value is { Length: > 0 and <= MaxIdentifierLength }
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-' or ':' or '*');

    private static bool Values(IReadOnlyCollection<string> values) => values.Count <= MaxValues && values.All(Identifier);

    private void Diagnose(Exception failure) => logger.LogDebug("Installed Agent support observation was incomplete ({FailureType}).", failure.GetType().Name);

    private static async Task<T> AwaitAsync<T>(Func<Task<T>> operation, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var pending = operation();
        try { return await pending.WaitAsync(token); }
        finally { _ = ObserveAsync(pending); }
    }

    private static async Task ObserveAsync(Task pending)
    {
        try { await pending; }
        catch (Exception) { }
    }
}
