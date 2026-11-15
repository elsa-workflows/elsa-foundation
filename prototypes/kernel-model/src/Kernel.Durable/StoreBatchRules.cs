namespace Kernel.Durable;

/// <summary>Reject malformed row mutations before any effect; one row mutation per actor per batch.</summary>
public static class StoreBatchRules
{
    public static void Validate(CommitBatch batch,bool deferUpdateAliases=false,bool deferInsertAliases=false)
    {
        var changed = new HashSet<string>(StringComparer.Ordinal);
        var rows = new HashSet<string>(StringComparer.Ordinal);
        foreach (var turn in batch.Turns)
        {
            if (turn.Row is { } value) ValidateIdentity(value);
            if (turn.Row is { } row && !StringComparer.Ordinal.Equals(turn.Instance,row.Id) &&
                !(turn.Expected is null?deferInsertAliases:deferUpdateAliases))
                throw new StoreException(Failure.Definite,"Turn and row must name the same physical actor.");
            if (turn.Row is { } mutation && !rows.Add(mutation.Id))
                throw new StoreException(Failure.Definite,"A batch cannot mutate the same row twice.");
            if ((turn.Row is not null || turn.Delete) && !changed.Add(turn.Instance))
                throw new StoreException(Failure.Definite,"A batch cannot mutate the same actor row twice.");
        }
    }

    public static void ValidateIdentity(InstanceRow row)
    {
        if (row is { IdentityOnly:true } identity && (identity.Retired || identity.Status==InstanceStatus.Running || identity.Creation!=0 || identity.Parent is not null ||
            identity.State!="null" || !CreationProtocol.IsPhysicalAddress(identity.Id) || !identity.Id.Contains("/key/",StringComparison.Ordinal)))
            throw new StoreException(Failure.Definite,"Identity-only rows must be state-free ended keyed runs.");
    }

    public static void RequirePhysicalCapability(CommitBatch batch,bool available)
    {
        if (!available && batch.Turns.Any(t=>t.Row is { Retired:true,Creation:0 }))
            throw new StoreException(Failure.Definite,"Store has no physical retirement capability.");
    }
}
