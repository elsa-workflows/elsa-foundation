using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;

namespace Elsa.Agent.Core.Services;

public sealed class DefaultAgentToolRegistry(IEnumerable<IAgentTool> tools) : IAgentToolRegistry
{
    private readonly IReadOnlyDictionary<string, IAgentTool> _tools = tools
        .GroupBy(x => x.Descriptor.Name, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<AgentToolDescriptor> Descriptors => _tools.Values.Select(x => x.Descriptor).ToList();

    public IAgentTool? Find(string toolName)
        => !string.IsNullOrWhiteSpace(toolName) && _tools.TryGetValue(toolName, out var tool) ? tool : null;
}
