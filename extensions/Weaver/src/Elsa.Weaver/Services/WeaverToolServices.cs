using System.Text.Json;
using System.Text.RegularExpressions;
using Elsa.Weaver.Core;
using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;
using Elsa.Events.Core.Contracts;

namespace Elsa.Weaver.Services;

/// <summary>One request-scoped catalog, collected through the named tool event; it retains no cross-request tools.</summary>
internal sealed partial class DefaultWeaverToolRegistry(IInlineEventPublisher events) : IWeaverToolRegistry
{
    private IReadOnlyCollection<IWeaverTool>? _tools;

    public async Task<IReadOnlyCollection<IWeaverTool>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (_tools is not null)
            return _tools;
        var collecting = new WeaverToolsCollecting();
        await events.Publish(collecting, cancellationToken);
        var tools = collecting.Tools.ToList();
        var duplicate = tools.GroupBy(x => x.Descriptor.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault(x => x.Count() > 1);
        if (duplicate is not null)
            throw new WeaverContributionException($"Duplicate Weaver tool name '{duplicate.Key}'.");
        foreach (var tool in tools)
        {
            var descriptor = tool.Descriptor;
            if (!ToolNameRegex().IsMatch(descriptor.Name ?? string.Empty))
                throw new WeaverContributionException($"Invalid Weaver tool name '{descriptor.Name}'; expected ^[a-zA-Z0-9_-]+$.");
            if (string.IsNullOrWhiteSpace(descriptor.ModuleId) || string.IsNullOrWhiteSpace(descriptor.ContractVersion))
                throw new WeaverContributionException($"Weaver tool '{descriptor.Name}' has no installed owner or contract version.");
            try
            {
                using var schema = JsonDocument.Parse(descriptor.JsonSchema);
                if (schema.RootElement.ValueKind != JsonValueKind.Object)
                    throw new WeaverContributionException($"Weaver tool '{descriptor.Name}' requires an object input schema.");
            }
            catch (JsonException ex)
            {
                throw new WeaverContributionException($"Weaver tool '{descriptor.Name}' has invalid JSON schema.", ex);
            }
        }
        return _tools = tools;
    }

    public async Task<IWeaverTool?> FindAsync(string toolName, CancellationToken cancellationToken = default)
        => (await ListAsync(cancellationToken)).SingleOrDefault(x => string.Equals(x.Descriptor.Name, toolName, StringComparison.OrdinalIgnoreCase));

    [GeneratedRegex("^[a-zA-Z0-9_-]+$")]
    private static partial Regex ToolNameRegex();
}
