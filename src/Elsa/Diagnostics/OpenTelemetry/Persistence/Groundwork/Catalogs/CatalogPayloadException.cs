namespace Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork.Catalogs;

/// <summary>Raised when an OpenTelemetry catalog document cannot be mapped to or from its durable payload.</summary>
public sealed class CatalogPayloadException : Exception
{
    public CatalogPayloadException(string message) : base(message)
    {
    }

    public CatalogPayloadException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
