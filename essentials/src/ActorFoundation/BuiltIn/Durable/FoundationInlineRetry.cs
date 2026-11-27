namespace Kernel.Durable;

// This occurrence retries an initial turn that failed before sealing any model state.
internal static class FoundationInlineRetry
{
    private const string OperationPrefix = "~inline-start/1:";
    private const string KindPrefix = "elsa.inline-retry/";
    internal const int Ordinal = int.MaxValue;

    internal static string Operation(string operation) => OperationPrefix + FoundationRootCreator.OperationDigest(operation);

    internal static bool IsOperation(string operation) => operation.StartsWith(OperationPrefix, StringComparison.Ordinal) &&
        FoundationRootCreator.IsGeneratedReference(operation[OperationPrefix.Length..]);

    private static string Id(FoundationSelfIssuer issuer, string actor) =>
        FoundationSelfSend.Issue(issuer.Handle, CreationProtocol.Origin(actor, 1), 1, Ordinal, due: null);

    internal static InboxKey Key(FoundationSelfIssuer issuer, string actor) =>
        InboxKey.Issued(CreationProtocol.Origin(actor, 1) + "#" + Id(issuer, actor));

    internal static Message Create(FoundationSelfIssuer issuer, Message creator) =>
        FoundationSelfSend.Create(creator.Target.Id, 1, Id(issuer, creator.Target.Id), KindPrefix + creator.Id, creator.Payload);

    internal static bool Matches(FoundationSelfIssuer issuer, InstanceRow row, Message message)
    {
        if (row is not { FoundationNullState: true, Creation: 1, Parent: null, Jobs: null, Children: null, StartIntent: not null } ||
            message.Due is not null || !message.Kind.StartsWith(KindPrefix, StringComparison.Ordinal) ||
            message.InboxKey != Key(issuer, row.Id) || !FoundationSelfSend.IsSelfInput(message, row.Id, 1)) return false;
        var operation = message.Kind[KindPrefix.Length..];
        if (!IsOperation(operation)) return false;
        var spec = new StartSpec(row.Model, row.Definition, message.Payload, Ephemeral: true, History: row.History,
            Creation: new(1, 0, FoundationRootCreator.OperationDigest(operation)));
        return CreationProtocol.Fingerprint(spec, payload: message.Payload) == row.StartIntent;
    }
}
