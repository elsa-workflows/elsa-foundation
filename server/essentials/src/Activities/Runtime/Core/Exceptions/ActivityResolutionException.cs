namespace Elsa.Activities.Runtime.Core.Exceptions;

/// <summary>
/// Base domain failure for executable activity activation. Missing consumers and unsupported
/// schemas are deployment/artifact compatibility failures and are not ordinary retryable activity
/// failures.
/// </summary>
public class ActivityResolutionException(
    string message,
    string consumerKey,
    string schemaVersion,
    Exception? innerException = null) : Exception(message, innerException)
{
    public string ConsumerKey { get; } = consumerKey;
    public string SchemaVersion { get; } = schemaVersion;
}

/// <summary>No Runtime consumer is registered for the persisted consumer key.</summary>
public sealed class UnknownActivityConsumerException(string consumerKey, string schemaVersion)
    : ActivityResolutionException(
        $"No activity constructor is registered for Runtime consumer '{consumerKey}' and schema '{schemaVersion}'. The feature that owns this consumer may not be installed.",
        consumerKey,
        schemaVersion);

/// <summary>The Runtime consumer is installed, but does not support the persisted descriptor schema.</summary>
public sealed class UnsupportedActivityDescriptorSchemaException(
    string consumerKey,
    string schemaVersion,
    IReadOnlyCollection<string> supportedSchemaVersions)
    : ActivityResolutionException(
        $"Runtime consumer '{consumerKey}' does not support descriptor schema '{schemaVersion}'. Supported schemas: {string.Join(", ", supportedSchemaVersions.Order(StringComparer.Ordinal))}.",
        consumerKey,
        schemaVersion)
{
    public IReadOnlyCollection<string> SupportedSchemaVersions { get; } = supportedSchemaVersions.ToArray();
}
