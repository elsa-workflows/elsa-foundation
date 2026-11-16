using Elsa.Activities.Runtime.Core.Contracts;

namespace Elsa.Activities.Runtime.Core.Models;

public sealed record RuntimeActivityConsumerCapability : IRuntimeActivityConsumerCapability
{
    public RuntimeActivityConsumerCapability(string consumerKey, IEnumerable<string> supportedSchemaVersions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerKey);
        ArgumentNullException.ThrowIfNull(supportedSchemaVersions);

        var versions = supportedSchemaVersions
            .Select(version =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(version);
                return version;
            })
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (versions.Length == 0)
            throw new ArgumentException("A runtime activity consumer must support at least one schema version.", nameof(supportedSchemaVersions));

        ConsumerKey = consumerKey;
        SupportedSchemaVersions = versions;
    }

    public string ConsumerKey { get; }
    public IReadOnlyCollection<string> SupportedSchemaVersions { get; }
}
