namespace Kernel.Durable;

/// <summary>Physical state-cell control encoding. Logical State and IdentityOnly remain separate.
/// Ordinary values using this family are escaped once, so no opaque caller value is a control header.</summary>
public static class StoredState
{
    public const string Family="!kernel-state/";
    public const string Identity="!kernel-state/1:identity-only";
    public const string Data="!kernel-state/1:data:";
    public static string Encode(InstanceRow row)
    {
        StoreBatchRules.ValidateIdentity(row);
        return row.IdentityOnly?Identity:row.State.StartsWith(Family,StringComparison.Ordinal)?Data+row.State:row.State;
    }
    public static InstanceRow Restore(InstanceRow physical)
    {
        var state=Decode(physical.State);
        if(physical.IdentityOnly && !state.IdentityOnly)
            throw new StoreException(Failure.Definite,"Contradictory logical and physical state-free identity headers.");
        var row=physical with{State=state.State,IdentityOnly=state.IdentityOnly};
        StoreBatchRules.ValidateIdentity(row);
        return row;
    }
    public static (string State,bool IdentityOnly) Decode(string stored)
    {
        if(!stored.StartsWith(Family,StringComparison.Ordinal))return(stored,false);
        if(stored==Identity)return("null",true);
        if(stored.StartsWith(Data,StringComparison.Ordinal) && stored[Data.Length..].StartsWith(Family,StringComparison.Ordinal))
            return(stored[Data.Length..],false);
        throw new StoreException(Failure.Definite,"Unsupported or malformed physical state control envelope; original bytes must be retained.");
    }
}
