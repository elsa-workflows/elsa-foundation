namespace Elsa.ConsumerGuide.Testing;

/// <summary>
/// Marks a test as the pin under a published consumer claim (spec 150 FR-B2). The claim is a one-sentence
/// statement in <c>docs/consumer-guide/claims.json</c>; this attribute is the other end of the link.
/// </summary>
/// <remarks>
/// Two gates keep the pair honest, and they run in both directions on purpose:
/// <list type="bullet">
/// <item>Gate A — every test a claim names must exist and carry this attribute with that claim's id. A
/// claim whose pin was deleted or renamed is a sentence nobody is checking any more.</item>
/// <item>Gate B — every id used here must exist in the claims file. A test that pins a claim nobody
/// published is knowledge the consumer never receives.</item>
/// </list>
/// The gates check the link; CI running the suite is what checks the claim. A published behavioural claim
/// is therefore never older than the last green build.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class ConsumerContractAttribute(string claimId) : Attribute
{
    /// <summary>The <c>id</c> of the claim in docs/consumer-guide/claims.json that this test pins.</summary>
    public string ClaimId { get; } = claimId;
}
