using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Integrations.Sdk.Harness;

/// <summary>Harness <see cref="IRuntimeSecretMask"/> so tests can prove tokens are registered and masked.</summary>
internal sealed class CapturingSecretMask : IRuntimeSecretMask
{
    private readonly Dictionary<string, List<(string Name, string Value)>> _values = new(StringComparer.Ordinal);

    public IReadOnlyList<(string Name, string Value)> Registrations(string activityExecutionId) =>
        _values.TryGetValue(activityExecutionId, out var list) ? list : [];

    public void Register(string activityExecutionId, string referenceName, string value)
    {
        if (string.IsNullOrEmpty(value))
            return;
        if (!_values.TryGetValue(activityExecutionId, out var list))
            _values[activityExecutionId] = list = [];
        list.Add((referenceName, value));
    }

    public bool HasRegistrations(string activityExecutionId) =>
        _values.TryGetValue(activityExecutionId, out var list) && list.Count > 0;

    public string Mask(string activityExecutionId, string text)
    {
        if (!_values.TryGetValue(activityExecutionId, out var list))
            return text;
        foreach (var (name, value) in list)
            text = text.Replace(value, $"[secret:{name}]", StringComparison.Ordinal);
        return text;
    }

    public void Release(string activityExecutionId) => _values.Remove(activityExecutionId);
}
