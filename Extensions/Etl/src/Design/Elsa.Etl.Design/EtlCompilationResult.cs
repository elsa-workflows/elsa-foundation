using Elsa.Etl.Core;

namespace Elsa.Etl.Design;

/// <summary>The artifact snapshot or blocking diagnostics from one compilation.</summary>
public sealed record EtlCompilationResult(EtlArtifact? Artifact, IReadOnlyList<EtlDiagnostic> Diagnostics);
