namespace Elsa.Integrations.Sdk.Signing;

/// <summary>
/// STUB until the scan and signing pipeline in valence-works/elsa-control#802 lands.
/// Recorded on the package until CI can see a real signature instead of a stub.
/// </summary>
public static class ConnectorPackageAttestation
{
    public const string Status = "stub";
    public const string PipelineIssue = "valence-works/elsa-control#802";
    public const string Note = "Scan and signing pipeline has not landed. This package is unsigned and unscanned.";

    public static bool IsSigned => false;
    public static bool IsScanned => false;
}
