using Elsa.Persistence.EntityFramework;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;

/// <summary>Content is unchanged; v2 maintenance also accounts for canonical ingress retention roots.</summary>
[EfSchemaUpcaster("1.0.0", "2.0.0")]
internal sealed class RuntimeArtifactV1Upcaster : IEfSchemaUpcaster
{
    public RuntimeArtifactV1Upcaster() { }
    public EfSchemaRowContent Upcast(EfSchemaRowContent row) => row;
}
