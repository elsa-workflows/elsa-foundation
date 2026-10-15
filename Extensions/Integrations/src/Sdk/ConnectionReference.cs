namespace Elsa.Integrations.Sdk;

/// <summary>
/// The only connection value that may appear in workflow JSON: a tenant-scoped technical name.
/// </summary>
internal sealed record ConnectionReference(string Name)
{
    public string Name { get; } = string.IsNullOrWhiteSpace(Name)
        ? throw new ArgumentException("A connection reference needs a name.", nameof(Name))
        : Name.Trim();

    public override string ToString() => Name;
}
