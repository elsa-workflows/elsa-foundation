using Elsa.Etl.Core;

namespace Elsa.Etl.Runtime.Hosting;

/// <summary>Verifies retained ETL content using the owning runtime's intrinsic rules without host admission or I/O.</summary>
public static class EtlArtifactSemantics
{
    /// <summary>Checks the complete owner format, hash, identity, limits, connectors and transformation contract.</summary>
    /// <remarks>Current host capacity, binding revisions, credentials, stores and transport availability are separate readiness facts.</remarks>
    public static void Validate(EtlArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        try
        {
            switch (artifact.FormatVersion)
            {
                case EtlArtifact.CurrentFormatVersion: _ = EtlExecutableReader.ReadPortable(artifact); break;
                case EtlArtifactV2Json.FormatVersion: _ = EtlConnectorExecutableReader.ReadPortable(artifact); break;
                default: throw new EtlArtifactSemanticException("etl.executable.unsupported-format");
            }
        }
        catch (EtlExecutableException exception) { throw new EtlArtifactSemanticException(exception.Code); }
    }
}

/// <summary>A stable value-free owner refusal; it contains no pipeline values or underlying parser text.</summary>
public sealed class EtlArtifactSemanticException(string code) : Exception("The retained ETL artifact was refused (" + code + ").")
{
    public string Code { get; } = code;
}
