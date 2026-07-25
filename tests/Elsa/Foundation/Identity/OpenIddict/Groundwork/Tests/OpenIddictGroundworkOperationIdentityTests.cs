using Elsa.Foundation.Identity.OpenIddict.Groundwork.Stores;

namespace Elsa.Foundation.Identity.OpenIddict.Groundwork.Tests;

public sealed class OpenIddictGroundworkOperationIdentityTests
{
    [Fact]
    public void Equal_operation_inputs_produce_the_same_opaque_identity()
    {
        var first = OpenIddictGroundworkOperationIdentity.Create(
            "token-redeem",
            "token-a",
            "request-a");
        var second = OpenIddictGroundworkOperationIdentity.Create(
            "token-redeem",
            "token-a",
            "request-a");

        Assert.Equal(first, second);
        Assert.Matches("^oidc:token-redeem:[a-f0-9]{64}$", first.Value);
        Assert.DoesNotContain("token-a", first.Value, StringComparison.Ordinal);
        Assert.DoesNotContain("request-a", first.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void Length_framing_component_order_and_operation_name_are_identity_bearing()
    {
        var splitOne = OpenIddictGroundworkOperationIdentity.Create("token-redeem", "ab", "c");
        var splitTwo = OpenIddictGroundworkOperationIdentity.Create("token-redeem", "a", "bc");
        var reversed = OpenIddictGroundworkOperationIdentity.Create("token-redeem", "c", "ab");
        var otherOperation = OpenIddictGroundworkOperationIdentity.Create("token-revoke", "ab", "c");

        Assert.Equal(4, new[] { splitOne, splitTwo, reversed, otherOperation }.Distinct().Count());
    }
}
