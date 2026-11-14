using Kernel.Durable;

// These fixtures capture generation one at the sender. They never resolve an old invocation against a newer run.
internal static class CreationFixture
{
    internal static Message Pin(Message message)
    {
        var start = message.Start is { Creation: null } spec ? spec with { Creation = new(1, 0, "admission-fixture") } : message.Start;
        return message with { Start = start, TargetCreation = message.TargetCreation ?? start?.Creation?.Generation ?? 1 };
    }
}
