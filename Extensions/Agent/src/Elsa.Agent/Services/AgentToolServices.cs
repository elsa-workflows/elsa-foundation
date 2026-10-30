using System.Text.Json;
using System.Text.RegularExpressions;
using Elsa.Agent.Core;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Events.Core.Contracts;

namespace Elsa.Agent.Services;

/// <summary>One request-scoped catalog, collected through the named tool event; it retains no cross-request tools.</summary>
internal sealed partial class DefaultAgentToolRegistry(IInlineEventPublisher events) : IAgentToolRegistry
{
    private IReadOnlyCollection<IAgentTool>? _tools;

    public async Task<IReadOnlyCollection<IAgentTool>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (_tools is not null)
            return _tools;
        var collecting = new AgentToolsCollecting();
        await events.Publish(collecting, cancellationToken);
        var tools = collecting.Tools.ToList();
        var duplicate = tools.GroupBy(x => x.Descriptor.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault(x => x.Count() > 1);
        if (duplicate is not null)
            throw new AgentContributionException($"Duplicate agent tool name '{duplicate.Key}'.");
        foreach (var tool in tools)
        {
            var descriptor = tool.Descriptor;
            if (!ToolNameRegex().IsMatch(descriptor.Name ?? string.Empty))
                throw new AgentContributionException($"Invalid agent tool name '{descriptor.Name}'; expected ^[a-zA-Z0-9_-]+$.");
            if (string.IsNullOrWhiteSpace(descriptor.ModuleId) || string.IsNullOrWhiteSpace(descriptor.ContractVersion))
                throw new AgentContributionException($"Agent tool '{descriptor.Name}' has no installed owner or contract version.");
            try
            {
                using var schema = JsonDocument.Parse(descriptor.JsonSchema);
                if (schema.RootElement.ValueKind != JsonValueKind.Object)
                    throw new AgentContributionException($"Agent tool '{descriptor.Name}' requires an object input schema.");
            }
            catch (JsonException ex)
            {
                throw new AgentContributionException($"Agent tool '{descriptor.Name}' has invalid JSON schema.", ex);
            }
        }
        return _tools = tools;
    }

    public async Task<IAgentTool?> FindAsync(string toolName, CancellationToken cancellationToken = default)
        => (await ListAsync(cancellationToken)).SingleOrDefault(x => string.Equals(x.Descriptor.Name, toolName, StringComparison.OrdinalIgnoreCase));

    [GeneratedRegex("^[a-zA-Z0-9_-]+$")]
    private static partial Regex ToolNameRegex();
}
