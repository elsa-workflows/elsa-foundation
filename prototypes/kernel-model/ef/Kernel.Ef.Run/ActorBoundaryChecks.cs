// Verify both the legacy default bridge and the full contextual hook through the actual decorator.
internal static class ActorBoundaryChecks
{
    private class Legacy : IActorType
    {
        public bool SingleMessage(Message message) => message.Kind == "legacy";
        public bool Withholds(InstanceRow row, ITurnServices services) => false;
        public TurnOutcome? Command(TurnInput input) => null;
        public ValueTask<TurnOutcome> TurnAsync(TurnInput input) => throw new NotSupportedException();
        public TurnOutcome Park(InstanceRow? row, string actor, IReadOnlyList<Delivery> taken, StartSpec? spec, string error) => throw new NotSupportedException();
        public ValueTask<object?> ExecuteJobAsync(string model, IJob job, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class Contextual : Legacy, IActorType
    {
        public Delivery? Seen;
        public bool SingleMessage(string? model, InstanceRow? row, Delivery delivery)
        {
            Seen = delivery;
            return model == "model" && row?.Model == model;
        }
    }
    public static void Run()
    {
        var message = new Message("legacy", new InstanceRef("0/probe"), "legacy");
        var delivery = new Delivery(message, 1);
        var shape = new Leveled(History.Full, 64);
        var legacy = shape.Over(new Legacy());
        if (!legacy.SingleMessage(message) || !legacy.SingleMessage(null, null, delivery))
            throw new InvalidOperationException("legacy hook and its default bridge must be forwarded");
        var inner = new Contextual();
        var contextual = shape.Over(inner);
        var row = new InstanceRow("0/probe", "model", "definition", InstanceStatus.Running, "{}", 1, null);
        if (!contextual.SingleMessage("model", row, delivery) || !ReferenceEquals(inner.Seen, delivery))
            throw new InvalidOperationException("context and original delivery must be forwarded");
        Console.WriteLine("pass actual Leveled forwards legacy and contextual singleton hooks");
    }
}
