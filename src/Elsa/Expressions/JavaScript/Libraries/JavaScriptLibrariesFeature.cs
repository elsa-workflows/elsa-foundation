using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Libraries.Options;
using Elsa.Expressions.JavaScript.Libraries.PreProcessors;
using Elsa.Primitives.Exceptions;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Expressions.JavaScript.Libraries;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Expressions")]
[ManifestFeatureCategory("JavaScript")]
[ShellFeature(
    name: "JavaScriptLibraries",
    DisplayName = "JavaScript libraries such as 'lodash', 'lodashFp', or 'moment'",
    Description = "Registers embedded JavaScript libraries for expression script preprocessing."
)]
public class JavaScriptLibrariesFeature : IShellFeature
{
    private const string ElsaClientLibResourceNameFormat = "Elsa.Expressions.JavaScript.Libraries.ClientLib.dist.{0}.js";

    [ManifestSetting(DisplayName = "Full module resource name", Description = "Embedded JavaScript module resource name to register for script preprocessing.", Category = "JavaScript", Advanced = true)]
    public string? FullModuleResourceName { get; set; }

    [ManifestSetting(DisplayName = "Module name", Description = "Built-in JavaScript module name to register for script preprocessing.", Category = "JavaScript")]
    public string? ModuleName { get; set; }

    public void ConfigureServices(IServiceCollection services)
    {
        ValidateFeatureOptions();

        services
            .AddScoped<IScriptPreProcessor, LibraryResourcePreProcessor>()
            .Configure<JavaScriptLibrariesFeatureOptions>(o => o.FullModuleResourceName = GetFullModuleResourceName())
            ;
    }

    private void ValidateFeatureOptions()
    {
        var bothEmpty = string.IsNullOrWhiteSpace(FullModuleResourceName) && string.IsNullOrWhiteSpace(ModuleName);
        var bothSpecified = !string.IsNullOrWhiteSpace(FullModuleResourceName) && !string.IsNullOrWhiteSpace(ModuleName);

        if (bothEmpty || bothSpecified)
        {
            throw new FeatureConfigurationException($"You must specify one of the following optionws: '{nameof(FullModuleResourceName)}' or '{nameof(ModuleName)}'");
        }
    }

    private string GetFullModuleResourceName()
    {
        if (!string.IsNullOrWhiteSpace(FullModuleResourceName))
            return FullModuleResourceName;

        return string.Format(ElsaClientLibResourceNameFormat, ModuleName);
    }
}
