using Xunit;

namespace Elsa.Architecture.Tests;

/// <summary>
/// Marks a test that loads and SDK-evaluates every shipping project (spec 1207 hash and baseline registers). One pass
/// takes over an hour, so it runs only when <c>ELSA_SHIPPING_CENSUS=1</c> is set: an implementer runs it once when hash
/// owners, JSON option sets or their source closures change, and records the result as PR evidence. CI skips it.
/// </summary>
internal sealed class ShippingCensusFactAttribute : FactAttribute
{
    internal const string Variable = "ELSA_SHIPPING_CENSUS";

    public ShippingCensusFactAttribute()
    {
        if (Environment.GetEnvironmentVariable(Variable) != "1")
            Skip = $"Shipping census evidence runs on demand: set {Variable}=1 (see docs/serialization.md).";
    }
}
