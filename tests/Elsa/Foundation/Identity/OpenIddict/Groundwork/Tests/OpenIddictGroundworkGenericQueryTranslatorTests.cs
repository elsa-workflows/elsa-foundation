using Elsa.Foundation.Identity.OpenIddict.Groundwork.Exceptions;
using Elsa.Foundation.Identity.OpenIddict.Groundwork.Querying;

namespace Elsa.Foundation.Identity.OpenIddict.Groundwork.Tests;

public sealed class OpenIddictGroundworkGenericQueryTranslatorTests
{
    [Fact]
    public void Count_delegate_is_rejected_without_invoking_caller_code()
    {
        var invoked = false;
        Func<IQueryable<ProbeRecord>, IQueryable<string>> query = records =>
        {
            invoked = true;
            return records.Select(record => record.Id);
        };

        var exception = Assert.Throws<OpenIddictGroundworkCapabilityException>(() =>
            OpenIddictGroundworkGenericQueryTranslator.RequireCountRoute(query, "application.CountAsync"));

        Assert.False(invoked);
        Assert.Equal(OpenIddictGroundworkCapabilityException.UnsupportedGenericQueryCode, exception.Code);
        Assert.Equal("application.CountAsync", exception.Operation);
    }

    [Theory]
    [InlineData("authorization.GetAsync")]
    [InlineData("scope.ListAsync")]
    [InlineData("token.GetAsync")]
    public void Stateful_delegate_is_rejected_without_invoking_caller_code(string operation)
    {
        var invoked = false;
        Func<IQueryable<ProbeRecord>, string, IQueryable<string>> query = (records, state) =>
        {
            invoked = true;
            return records.Where(record => record.Id == state).Select(record => record.Id);
        };

        var exception = Assert.Throws<OpenIddictGroundworkCapabilityException>(() =>
            OpenIddictGroundworkGenericQueryTranslator.RequireStatefulRoute(query, operation));

        Assert.False(invoked);
        Assert.Equal(OpenIddictGroundworkCapabilityException.UnsupportedGenericQueryCode, exception.Code);
        Assert.Equal(operation, exception.Operation);
    }

    [Fact]
    public void Null_generic_delegate_is_rejected_as_a_caller_error_before_capability_admission()
    {
        Assert.Throws<ArgumentNullException>(() =>
            OpenIddictGroundworkGenericQueryTranslator.RequireCountRoute<ProbeRecord, string>(
                null!,
                "token.CountAsync"));
    }

    private sealed record ProbeRecord(string Id);
}
