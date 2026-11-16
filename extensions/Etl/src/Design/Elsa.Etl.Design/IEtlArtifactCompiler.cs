using Elsa.Etl.Core;

namespace Elsa.Etl.Design;

/// <summary>Validates the finite topology and snapshots it into a runnable immutable artifact.</summary>
public interface IEtlArtifactCompiler
{
    /// <summary>Compiles a published definition version or returns safe authoring diagnostics.</summary>
    EtlCompilationResult Compile(string definitionId, int definitionVersion, EtlPipeline pipeline);
}
