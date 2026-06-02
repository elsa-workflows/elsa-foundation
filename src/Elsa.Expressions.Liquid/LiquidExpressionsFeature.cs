using CShells.Features;
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

namespace Elsa.Expressions.Liquid
{
    /// <summary>
    /// Configures Liquid functionality.
    /// </summary>
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
        public bool AllowConfigurationAccess { get; set; }

        public bool AddArrayFilters { get; set; } = true;

        public bool AddStringFilters { get; set; } = true;

        public bool AddNumberFilters { get; set; } = true;

        public bool AddMiscFilters { get; set; } = true;

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
}
