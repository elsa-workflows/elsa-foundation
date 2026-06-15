using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.JavaScript.Constants;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Events;
using Elsa.Expressions.JavaScript.Core.Models;
using Elsa.Expressions.JavaScript.Core.Options;
using Elsa.Expressions.JavaScript.Handlers;
using Elsa.Expressions.JavaScript.Options;
using Elsa.Expressions.JavaScript.PreProcessors;
using Elsa.Expressions.JavaScript.Services;
using Elsa.Events.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Expressions.JavaScript;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Expressions")]
[ManifestFeatureCategory("JavaScript")]
[ShellFeature(
    name: "JavaScriptExpressions",
    DisplayName = "JavaScript Expressions",
    Description = "Provides functions to register and configure JavaScript Expressions"
)]
public class JavaScriptFeature : IShellFeature
{
    public IEnumerable<JavaScriptTypeDescriptor> TypeDescriptors { get; set; } = DefaultTypeDescriptors.Get();

    public ConfigurationAccessFunctionProviderOptions? GetConfigurationFunction { get; set; }

    public void ConfigureServices(IServiceCollection services)
    {
        services
            .Configure<ConfigurationAccessFunctionProviderOptions>(o =>
            {
                o.AllowConfigurationAccess = GetConfigurationFunction?.AllowConfigurationAccess ?? false;
                o.DisallowedSections = GetConfigurationFunction?.DisallowedSections ?? [];
            })
            .Configure<JavaScriptOptions>(o =>
            {
                TypeDescriptors.ToList().ForEach(o.TypeDescriptors.Add);
            })

            .AddEventHandler<OnEvaluatingScript, PreProcessScript>()
            .AddEventHandler<OnScriptEvaluated, PostProcessScript>()
            .AddScoped<IScriptPreProcessor, ArgsObjectPreProcessor>()
            .AddScoped<IScriptPreProcessor, CommonFunctionsPreProcessor>()
            .AddScoped<IScriptPreProcessor, ArgumentFunctionsPreProcessor>()
            .AddScoped<IScriptPreProcessor, ConfigurationAccessFunctionPreProcessor>()
            .AddScoped<IScriptPreProcessor, TypeRegistrationPreProcessor>()

            .AddScoped<IExpressionHandler, JavaScriptExpressionHandler>()
            .AddScoped<IExpressionDescriptorProvider, JavaScriptExpressionDescriptorProvider>();
    }
}