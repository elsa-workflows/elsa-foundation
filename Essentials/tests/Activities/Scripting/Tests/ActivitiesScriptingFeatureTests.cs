using CShells.Features;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Scripting.Tests;

/// <summary>
/// Feature-registration coverage for <see cref="ActivitiesScriptingFeature"/>. The
/// <c>RunJavaScript</c> activity is transiently DI-activated and constructor-injected with the isolated script
/// evaluator, so the feature registers no per-type activity services of its own; its manifest dependency on the
/// JavaScript Jint engine supplies that evaluator when the shell is composed.
/// </summary>
public sealed class ActivitiesScriptingFeatureTests
{
    [Fact]
    public void ConfigureServices_DeclaresOnlyThatRunJavaScriptRunsJavaScript()
    {
        // Spec 1228 FR-028: the declaration pins RunJavaScript nodes to the JavaScript value adapter.
        var services = new ServiceCollection();

        new ActivitiesScriptingFeature().ConfigureServices(services);

        var descriptor = Assert.Single(services);
        var declaration = Assert.IsType<Elsa.Expressions.Core.Models.ScriptActivityLanguage>(descriptor.ImplementationInstance);
        Assert.Equal(Elsa.Activities.Scripting.Activities.RunJavaScript.ActivityType, declaration.ActivityType);
        Assert.Equal("JavaScript", declaration.Language);
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
