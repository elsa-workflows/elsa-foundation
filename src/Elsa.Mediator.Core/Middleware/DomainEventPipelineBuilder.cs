using Elsa.Mediator.Core.Contracts;

namespace Elsa.Mediator.Core.Middleware;

/// <inheritdoc />
public class DomainEventPipelineBuilder : IDomainEventPipelineBuilder
{
    private const string ServicesKey = "mediator.Services";
    private readonly List<Func<DomainEventMiddlewareDelegate, DomainEventMiddlewareDelegate>> _components = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="RequestPipelineBuilder"/> class.
    /// </summary>
    public DomainEventPipelineBuilder(IServiceProvider serviceProvider)
    {
        ApplicationServices = serviceProvider;
    }

    /// <inheritdoc />
    public IDictionary<string, object?> Properties { get; } = new Dictionary<string, object?>();

    /// <inheritdoc />
    public IServiceProvider ApplicationServices
    {
        get => GetProperty<IServiceProvider>(ServicesKey)!;
        set => SetProperty(ServicesKey, value);
    }

    /// <inheritdoc />
    public IDomainEventPipelineBuilder Use(Func<DomainEventMiddlewareDelegate, DomainEventMiddlewareDelegate> middleware)
    {
        _components.Add(middleware);
        return this;
    }

    /// <inheritdoc />
    public DomainEventMiddlewareDelegate Build()
    {
        DomainEventMiddlewareDelegate pipeline = _ => new ValueTask();

        for (int i = _components.Count - 1; i >= 0; i--)
        {
            pipeline = _components[i](pipeline);
        }

        return pipeline;
    }

    private T? GetProperty<T>(string key) => Properties.TryGetValue(key, out var value) ? (T?)value : default(T);
    private void SetProperty<T>(string key, T value) => Properties[key] = value;
}