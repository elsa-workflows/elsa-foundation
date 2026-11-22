namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Declares the stable identity of one incident-strategy implementation.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class IncidentStrategyAttribute(string alias, string version) : Attribute
{
    public string Alias { get; } = alias;
    public string Version { get; } = version;
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
}
