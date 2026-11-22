using System.Reflection;
using Elsa.Activities.Http.Activities;
using Elsa.Activities.Runtime.Core;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Http.Core;
using Elsa.Http.Services;
using Xunit;

namespace Elsa.Activities.Http.Tests;

public sealed class HttpEndpointMetadataTests
{
    [Theory]
    [InlineData(nameof(HttpEndpoint.CanStartWorkflow), "Simple", 0)]
    [InlineData(nameof(HttpEndpoint.Path), "Simple", 10)]
    [InlineData(nameof(HttpEndpoint.SupportedMethods), "Simple", 20)]
    [InlineData(nameof(HttpEndpoint.ResponseMode), "Simple", 30)]
    [InlineData(nameof(HttpEndpoint.Authorize), "Advanced", 100)]
    [InlineData(nameof(HttpEndpoint.Policy), "Advanced", 110)]
    [InlineData(nameof(HttpEndpoint.RequestTimeout), "Advanced", 120)]
    [InlineData(nameof(HttpEndpoint.RequestSizeLimit), "Advanced", 130)]
    public void Input_HasExpectedCategoryAndOrder(string propertyName, string category, float order)
    {
        var property = typeof(HttpEndpoint).GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(property);
        var metadata = Assert.IsType<ActivityInputAttribute>(property.GetCustomAttribute<ActivityInputAttribute>());

        Assert.Equal(category, metadata.Category);
        Assert.Equal(order, metadata.Order);
    }

    [Fact]
    public void SupportedMethods_AdvertisesChecklistOptions()
    {
        var property = typeof(HttpEndpoint).GetProperty(nameof(HttpEndpoint.SupportedMethods), BindingFlags.Instance | BindingFlags.Public)!;
        var metadata = Assert.IsType<ActivityInputAttribute>(property.GetCustomAttribute<ActivityInputAttribute>());

        Assert.Equal(ActivityInputUIHints.CheckList, metadata.UIHint);
        Assert.Equal(new[] { "GET", "POST", "PUT", "HEAD", "DELETE" }, metadata.Options);
    }

    [Fact]
    public void ResponseMode_AdvertisesDropdownOptionsAndSyncDefault()
    {
        var property = typeof(HttpEndpoint).GetProperty(nameof(HttpEndpoint.ResponseMode), BindingFlags.Instance | BindingFlags.Public)!;
        var metadata = Assert.IsType<ActivityInputAttribute>(property.GetCustomAttribute<ActivityInputAttribute>());
        var options = property.GetCustomAttributes<ActivityInputOptionAttribute>().ToArray();

        Assert.Equal(ActivityInputUIHints.Dropdown, metadata.UIHint);
        Assert.Equal("Sync", metadata.DefaultValue);
        Assert.Collection(
            options,
            option =>
            {
                Assert.Equal("Wait for workflow response", option.Label);
                Assert.Equal(ResponseMode.Sync, option.Value);
            },
            option =>
            {
                Assert.Equal("Accept and run in background", option.Label);
                Assert.Equal(ResponseMode.Async, option.Value);
            });
        Assert.Equal(ResponseMode.Sync, new HttpEndpoint(new HttpRequestBodyParser()).ResponseMode);
    }
}
