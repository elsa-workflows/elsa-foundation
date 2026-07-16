using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Liquid.Contracts;
using Elsa.Expressions.Liquid.Enums;
using Elsa.Expressions.Liquid.Filters;
using Elsa.Expressions.Liquid.Handlers;
using Elsa.Expressions.Liquid.Notifications;
using Elsa.Expressions.Liquid.Options;
using Elsa.Expressions.Liquid.Services;
using Elsa.Events.Core.Contracts;
using Fluid;
using Fluid.Filters;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Encodings.Web;

namespace Elsa.Expressions.Liquid;

/// <summary>
/// Configures Liquid functionality.
/// </summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Expressions")]
[ManifestFeatureCategory("Liquid")]
[ShellFeature(
    name: "Liquid",
    DisplayName = "Liquid Expressions",
    Description = "Provides Liquid template expression evaluation capabilities for workflows")]
public class LiquidExpressionsFeature : IShellFeature
{
    /// <summary>
    /// A dictionary of filter registrations.
    /// </summary>
    private readonly Dictionary<string, Type> filterRegistrations = [];

    /// <summary>
    /// Gets or sets the default encoder to use when rendering a template.
    /// </summary>
    private TextEncoder Encoder => EncodingType == LiquidEncodingType.Null
        ? NullEncoder.Default
        : HtmlEncoder.Default;

    /// <summary>
    /// Gets or sets a value indicating whether to allow access to the configuration object.
    /// </summary>
    [ManifestSetting(DisplayName = "Allow configuration access", Description = "Allows Liquid templates to read application configuration values.", Category = "Security", Advanced = true)]
    public bool AllowConfigurationAccess { get; set; }

    [ManifestSetting(DisplayName = "Add array filters", Description = "Registers the built-in Liquid array filters.", Category = "Filters", DefaultValue = "true")]

    public bool AddArrayFilters { get; set; } = true;

    [ManifestSetting(DisplayName = "Add string filters", Description = "Registers the built-in Liquid string filters.", Category = "Filters", DefaultValue = "true")]

    public bool AddStringFilters { get; set; } = true;

    [ManifestSetting(DisplayName = "Add number filters", Description = "Registers the built-in Liquid number filters.", Category = "Filters", DefaultValue = "true")]

    public bool AddNumberFilters { get; set; } = true;

    [ManifestSetting(DisplayName = "Add miscellaneous filters", Description = "Registers the built-in miscellaneous Liquid filters.", Category = "Filters", DefaultValue = "true")]

    public bool AddMiscFilters { get; set; } = true;

    [ManifestSetting(DisplayName = "Encoding type", Description = "Text encoder used when rendering Liquid templates.", Category = "Rendering")]

    public LiquidEncodingType EncodingType { get; set; }

    public IEnumerable<string> VariableDescriptorTypes { get; set; } = [];

    public void ConfigureServices(IServiceCollection services)
    {
        AddLiquidFilter<Base64Filter>(services, "base64");
        AddLiquidFilter<DictionaryKeysFilter>(services, "keys");

        var variableDescriptorTypes = GetVariableDescriptorTypes().ToArray();
        var engineOptions = new ConfigureLiquidEngineOptions(variableDescriptorTypes, AllowConfigurationAccess);
        services
            .AddSingleton(engineOptions)
            .AddScoped<IEventHandler<RenderingLiquidTemplate>, ConfigureLiquidEngine>();

        var templateManagerOptions = new LiquidTemplateManagerOptions(Encoder, filterRegistrations, ConfigureFilters);
        services
            .AddSingleton(templateManagerOptions)
            .AddScoped<ILiquidTemplateManager, LiquidTemplateManager>()
            .AddScoped<IPortableExpressionHandler, PortableLiquidExpressionHandler>()
            .AddScoped<FluidParser>()
            .AddSingleton<IExpressionDescriptorProvider, LiquidExpressionDescriptorProvider>();
    }

    private void AddLiquidFilter<T>(IServiceCollection services, string name) where T : class, ILiquidFilter
    {
        filterRegistrations[name] = typeof(T);
        services.AddScoped<T>();
    }

    private IEnumerable<Type> GetVariableDescriptorTypes()
    {
        foreach (var typeName in VariableDescriptorTypes)
        {
            var type = Type.GetType(typeName)
                       ?? throw new TypeUnloadedException($"Type '{typeName}' is not loaded"); ;

            if (!type.IsClass)
                throw new InvalidOperationException("Variable descriptors must be a class");

            if (type.ContainsGenericParameters)
                throw new InvalidOperationException("Variable descriptor class cannot contain generic parameters");

            yield return type;
        }
    }

    private void ConfigureFilters(TemplateContext context)
    {
        if (AddNumberFilters)
        {
            context.Options.Filters.WithNumberFilters();
        }
        if (AddStringFilters)
        {
            context.Options.Filters.WithStringFilters();
        }
        if (AddMiscFilters)
        {
            context.Options.Filters.WithMiscFilters();
        }
        if (AddArrayFilters)
        {
            context.Options.Filters.WithArrayFilters();
        }
    }
}
