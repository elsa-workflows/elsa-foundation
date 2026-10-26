namespace Elsa.Workflows.Runtime.Services.Executables;

/// <summary>Owns the default native stores as one process-local material boundary.</summary>
internal sealed class InMemoryRuntimeArtifactStores
{
    public InMemoryRuntimeArtifactStores(TimeProvider clock)
    {
        Executables = new();
        References = new(Executables);
        Templates = InMemoryExecutableActivityTemplateStore.CreateForMaintenance(Executables, References, clock);
    }

    public InMemoryWorkflowExecutableStore Executables { get; }
    public InMemoryWorkflowExecutableSourceReferenceStore References { get; }
    public InMemoryExecutableActivityTemplateStore Templates { get; }
}
