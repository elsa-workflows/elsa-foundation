using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Helpers;
using Elsa.Expressions.Liquid.Helpers;
using Elsa.Expressions.Liquid.Notifications;
using Elsa.Expressions.Liquid.Options;
using Elsa.Events.Core.Contracts;
using Fluid;
using Fluid.Values;
using Microsoft.Extensions.Configuration;

namespace Elsa.Expressions.Liquid.Handlers;

/// <summary>
/// Configures the liquid templating engine before evaluating a liquid expression.
/// </summary>
/// <remarks>
/// Constructor.
/// </remarks>
public sealed class ConfigureLiquidEngine(IConfiguration configuration, ConfigureLiquidEngineOptions options)
    : IEventHandler<RenderingLiquidTemplate>
{
    private readonly ConfigureLiquidEngineOptions _options = options;

    /// <inheritdoc />
    public Task Handle(RenderingLiquidTemplate notification, CancellationToken cancellationToken)
    {
        var context = notification.TemplateContext;
        var options = context.Options;
        var memberAccessStrategy = options.MemberAccessStrategy;

        memberAccessStrategy.Register<LiquidPropertyAccessor, FluidValue>((x, name) => x.GetValueAsync(name));
        memberAccessStrategy.Register<IExpressionExecutionContext, LiquidPropertyAccessor>("Variables", x => new LiquidPropertyAccessor(name => GetVariable(x, name, options)));
        memberAccessStrategy.Register<IExpressionExecutionContext, LiquidPropertyAccessor>("Input", x => new LiquidPropertyAccessor(name => GetInput(x, name, options)));
        memberAccessStrategy.Register<IExpressionExecutionContext, string?>("CorrelationId", x => x.GetCorrelationId());
        memberAccessStrategy.Register<IExpressionExecutionContext, string>("WorkflowDefinitionId", x => x.GetWorkflowDefinitionId());
        memberAccessStrategy.Register<IExpressionExecutionContext, string>("WorkflowDefinitionVersionId", x => x.GetWorkflowDefinitionVersionId());
        memberAccessStrategy.Register<IExpressionExecutionContext, int>("WorkflowDefinitionVersion", x => x.GetWorkflowDefinitionVersion());
        memberAccessStrategy.Register<IExpressionExecutionContext, string>("WorkflowInstanceId", x => x.GetWorkflowInstanceId());

        if (_options.AllowConfigurationAccess)
        {
            memberAccessStrategy.Register<IExpressionExecutionContext, LiquidPropertyAccessor>("Configuration", x => new LiquidPropertyAccessor(name => ToFluidValue(GetConfigurationValue(name), options)));
            memberAccessStrategy.Register<ConfigurationSectionWrapper, ConfigurationSectionWrapper?>((source, name) => source.GetSection(name));
        }

        // Register all variable types.
        foreach (var variableDescriptor in _options.VariableDescriptorTypes)
            memberAccessStrategy.Register(variableDescriptor);

        return Task.CompletedTask;
    }

    private ConfigurationSectionWrapper GetConfigurationValue(string name)
        => new(configuration.GetSection(name));

    private static Task<FluidValue> ToFluidValue(object? input, TemplateOptions options)
        => Task.FromResult(FluidValue.Create(input, options));

    private static Task<FluidValue> GetVariable(IExpressionExecutionContext context, string key, TemplateOptions options)
    {
        // Lift a stored dynamic (Any) value from its round-tripped JsonElement to the canonical JsonNode,
        // which Fluid binds natively (it does not bind JsonElement) — ADR 0036.
        var value = DynamicValueMaterializer.Materialize(context.GetVariableValueOrDefault(key));
        var result = value == null ? NilValue.Instance : FluidValue.Create(value, options);
        return Task.FromResult(result);
    }

    private static Task<FluidValue> GetInput(IExpressionExecutionContext context, string key, TemplateOptions options)
    {
        FluidValue fluidValue = NilValue.Instance;

        // First, check if the current activity has inputs
        // Else, fall back to workflow inputs if activity inputs don't contain the key
        if (context.TryGetActivityInput(key, out object? input) || context.TryGetWorkflowInput(key, out input))
        {
            var value = DynamicValueMaterializer.Materialize(input);
            fluidValue = value is null
                ? NilValue.Instance
                : FluidValue.Create(value, options);
        }

        return Task.FromResult(fluidValue);
    }
}