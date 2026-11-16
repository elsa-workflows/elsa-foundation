namespace Elsa.Workflows.Runtime.Services.History;

/// <summary>Production history configuration. Changes apply to new admissions; retained policies stay authoritative.</summary>
public sealed class RuntimeHistoryOptions
{
    public bool Enabled { get; set; } = true;
}
