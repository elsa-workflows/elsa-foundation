using Kernel.Runtime;

namespace Kernel.Durable;

/// <summary>Dispatch by the stored model key (or the start when creating the row).
/// Existing models and addresses keep their meaning under the host's default actor type.</summary>
public sealed class ActorTypeRouter(IActorType fallback, IReadOnlyDictionary<string, IActorType> types) : IActorType
{
    private IActorType For(string? model) => model is not null && types.TryGetValue(model, out var type) ? type : fallback;

    public string? SelfReference(Delivery delivery)
    {
        if (fallback.SelfReference(delivery) is { } address) return address;
        foreach (var type in types.Values)
            if (type.SelfReference(delivery) is { } reference) return reference;
        return null;
    }

    public bool SingleMessage(Message message) => fallback.SingleMessage(message);
    public bool SingleMessage(string? model, InstanceRow? row, Delivery delivery) => For(model).SingleMessage(model, row, delivery);
    public bool Withholds(InstanceRow row, ITurnServices services) => For(row.Model).Withholds(row, services);
    public TurnOutcome? Command(TurnInput input) => For(input.Row?.Model ?? input.Spec?.Model).Command(input);
    public ValueTask<TurnOutcome> TurnAsync(TurnInput input) => For(input.Model).TurnAsync(input);
    public TurnOutcome Park(InstanceRow? row, string actor, IReadOnlyList<Delivery> taken, StartSpec? spec, string error) =>
        For(row?.Model ?? spec?.Model).Park(row, actor, taken, spec, error);
    public TurnOutcome Park(InstanceRow? row, string actor, IReadOnlyList<Delivery> taken, StartSpec? spec, string error, object? pending) =>
        For(row?.Model ?? spec?.Model).Park(row, actor, taken, spec, error, pending);
    public TurnOutcome Write(InstanceRow row, object? state, object? pending) => For(row.Model).Write(row, state, pending);
    public ValueTask<object?> ExecuteJobAsync(string model, IJob job, CancellationToken cancellationToken) =>
        For(model).ExecuteJobAsync(model, job, cancellationToken);
}
