using Elsa.Etl.Core;

namespace Elsa.Etl.Design;

/// <summary>Optional provider capability for compiling connector-capable v2 pipeline definitions.</summary>
/// <remarks>Existing providers implementing only <see cref="IEtlArtifactCompiler"/> remain v1 providers.</remarks>
public interface IEtlArtifactCompilerV2
{
    /// <summary>Validates and snapshots a v2 definition or returns safe authoring diagnostics.</summary>
    EtlCompilationResult Compile(string definitionId, int definitionVersion, EtlPipelineV2 pipeline);
}
