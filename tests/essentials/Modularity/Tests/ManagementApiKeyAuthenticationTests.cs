using Elsa.Workbench;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Modularity.Tests;

public sealed class ManagementApiKeyAuthenticationTests
{
    [Fact]
    public void ValidateReturnsNotFoundWhenNoKeyConfigured()
    {
        var result = ManagementApiKeyAuthentication.Validate(CreateContext(configuredApiKey: null, providedApiKey: "anything"));

        Assert.IsType<NotFound>(result);
    }

    [Fact]
    public void ValidateReturnsUnauthorizedWhenKeyMissingOrWrong()
    {
        var missing = ManagementApiKeyAuthentication.Validate(CreateContext("expected-key", providedApiKey: null));
        var wrong = ManagementApiKeyAuthentication.Validate(CreateContext("expected-key", providedApiKey: "wrong-key"));

        Assert.IsType<UnauthorizedHttpResult>(missing);
        Assert.IsType<UnauthorizedHttpResult>(wrong);
    }

    [Fact]
    public void ValidateReturnsNullWhenKeyMatches()
    {
        Assert.Null(ManagementApiKeyAuthentication.Validate(CreateContext("expected-key", providedApiKey: "expected-key")));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("expected-key", true)]
    public async Task RequireAsyncOnlyInvokesTheEndpointWhenTheKeyMatches(string? providedApiKey, bool expectInvoked)
    {
        var invoked = false;
        var context = new DefaultEndpointFilterInvocationContext(CreateContext("expected-key", providedApiKey));

        var result = await ManagementApiKeyAuthentication.RequireAsync(context, _ =>
        {
            invoked = true;
            return new ValueTask<object?>(Results.Ok());
        });

        Assert.Equal(expectInvoked, invoked);
        if (expectInvoked)
            Assert.IsType<Ok>(result);
        else
            Assert.IsType<UnauthorizedHttpResult>(result);
    }

    [Fact]
    public void KeysEqualUsesExactComparison()
    {
        Assert.True(ManagementApiKeyAuthentication.KeysEqual("secret", "secret"));
        Assert.False(ManagementApiKeyAuthentication.KeysEqual("secret", "Secret"));
        Assert.False(ManagementApiKeyAuthentication.KeysEqual("secret", "secre"));
    }

    private static DefaultHttpContext CreateContext(string? configuredApiKey, string? providedApiKey)
    {
        var settings = new Dictionary<string, string?>();
        if (configuredApiKey is not null)
            settings[ManagementApiKeyAuthentication.ConfigurationKey] = configuredApiKey;

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection().AddSingleton<IConfiguration>(configuration).BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        if (providedApiKey is not null)
            context.Request.Headers[ManagementApiKeyAuthentication.HeaderName] = providedApiKey;

        return context;
    }
}
