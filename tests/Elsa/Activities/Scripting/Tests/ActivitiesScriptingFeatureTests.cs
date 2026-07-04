using CShells.Features;
using Elsa.Activities.Runtime.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Scripting.Tests;

/// <summary>
/// Feature-registration coverage for <see cref="ActivitiesScriptingFeature"/> (constitution §2.23.1). The
/// <c>RunJavaScript</c> activity is constructed by the runtime's CLR activity constructor and evaluates through
/// the shared Jint evaluator, so the feature registers no per-type activity services of its own; it declares the
/// dependency on the JavaScript Jint engine feature (which registers <c>IJavaScriptEvaluator</c>) so composing
/// this feature yields a working scripting activity.
/// </summary>
public sealed class ActivitiesScriptingFeatureTests
{
    [Fact]
    public void ConfigureServices_RegistersNoServices()
    {
        var services = new ServiceCollection();

        new ActivitiesScriptingFeature().ConfigureServices(services);

        Assert.Empty(services);
    }

    [Fact]
    public void ConfigureServices_RegistersNoActivityConstructor()
    {
        var services = new ServiceCollection();

        new ActivitiesScriptingFeature().ConfigureServices(services);

        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IActivityConstructor));
    }

    [Fact]
    public void DeclaresJavaScriptJintEngineDependency()
    {
        var attribute = Assert.Single(
            typeof(ActivitiesScriptingFeature).GetCustomAttributes(typeof(ShellFeatureAttribute), inherit: false)
                .Cast<ShellFeatureAttribute>());

        Assert.Equal("ActivitiesScripting", attribute.Name);
        var dependencies = attribute.DependsOn.Select(d => d?.ToString()).ToArray();
        Assert.Contains("JavaScriptJintEngine", dependencies);
    }
}
