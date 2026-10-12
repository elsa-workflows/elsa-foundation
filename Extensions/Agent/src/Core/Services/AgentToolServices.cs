using System.Text.RegularExpressions;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;

namespace Elsa.Agent.Core.Services;

public sealed partial class DefaultAgentToolRegistry : IAgentToolRegistry
{
    private readonly IReadOnlyDictionary<string, IAgentTool> _tools;

    public DefaultAgentToolRegistry(IEnumerable<IAgentTool> tools)
    {
        var registered = tools.ToList();

        // Tool names cross into agent harnesses (GitHub Copilot, Anthropic) whose APIs require ^[a-zA-Z0-9_-]+$.
        // Fail fast at registration with a clear message rather than surfacing a cryptic provider error at runtime.
        var invalid = registered
            .Select(x => x.Descriptor.Name)
            .Where(name => !ToolNameRegex().IsMatch(name ?? string.Empty))
            .ToList();
        if (invalid.Count > 0)
            throw new InvalidOperationException($"Invalid agent tool name(s): {string.Join(", ", invalid)}. Tool names must match ^[a-zA-Z0-9_-]+$ (no dots) so they are accepted by agent harnesses.");

        // Fail fast on duplicate names (case-insensitive, matching the lookup comparer) rather than
        // silently keeping the first registration — two tools answering the same name is a composition
        // bug the host should learn about at startup, consistent with the invalid-name guard above.
        var duplicates = registered
            .GroupBy(x => x.Descriptor.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        if (duplicates.Count > 0)
            throw new InvalidOperationException($"Duplicate agent tool name(s): {string.Join(", ", duplicates)}. Each agent tool must have a unique name (case-insensitive) so registrations are not silently dropped.");

        _tools = registered
            .GroupBy(x => x.Descriptor.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
    }

    [GeneratedRegex("^[a-zA-Z0-9_-]+$")]
    private static partial Regex ToolNameRegex();

    public IReadOnlyCollection<AgentToolDescriptor> Descriptors => _tools.Values.Select(x => x.Descriptor).ToList();

    public IReadOnlyCollection<IAgentTool> Tools => _tools.Values.ToList();

    public IAgentTool? Find(string toolName)
        => !string.IsNullOrWhiteSpace(toolName) && _tools.TryGetValue(toolName, out var tool) ? tool : null;
}
