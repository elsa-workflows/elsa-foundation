namespace Elsa.Primitives.Identity;

/// <summary>
/// Configures <see cref="SnowflakeIdentityGenerator"/>.
/// </summary>
public sealed class SnowflakeIdentityGeneratorOptions
{
    /// <summary>
    /// The worker (node) identifier, between 0 and 1023 inclusive. Every node generating identifiers concurrently must
    /// use a distinct value to guarantee uniqueness across the deployment.
    /// </summary>
    public long WorkerId { get; set; }

    /// <summary>
    /// The epoch the 41-bit timestamp component is measured from. Defaults to <c>2020-01-01T00:00:00Z</c>, giving a
    /// usable range of ~69 years.
    /// </summary>
    public DateTimeOffset Epoch { get; set; } = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
}
