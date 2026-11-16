namespace Elsa.Etl.Design;

/// <summary>A stable code and authored field path, without row values or host paths.</summary>
public sealed record EtlDiagnostic(string Code, string Path, string Message);
