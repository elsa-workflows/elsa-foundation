namespace Kernel.Durable;

/// <summary>Physical state-cell control encoding. Logical State and IdentityOnly remain separate.
/// Ordinary values using this family are escaped once, so no opaque caller value is a control header.</summary>
public static class StoredState
{
    public const string Family="!kernel-state/";
    public const string Identity="!kernel-state/2:identity-only";
    public const string MetadataIdentity="!kernel-state/2:identity-metadata";
    internal const string Absent = "!kernel-state/3:absent";
    public const string Data="!kernel-state/1:data:";
    public static string Encode(InstanceRow row)
    {
        var (prefix, value) = EncodingParts(row);
        return prefix + value;
    }

    internal static (string Prefix, string Value) EncodingParts(InstanceRow row)
    {
        StoreBatchRules.ValidateIdentity(row);
        if (row.FoundationNullState) return ("", Absent);
        if (row.IdentityOnly) return ("", row.IdentityMetadataRequired ? MetadataIdentity : Identity);
        return (row.State.StartsWith(Family, StringComparison.Ordinal) ? Data : "", row.State);
    }
    public static InstanceRow Restore(InstanceRow physical)
    {
        var state=DecodeEnvelope(physical.State);
        var absent = state.Absent;
        if (physical.FoundationNullState && !absent)
            throw new StoreException(Failure.Definite, "Contradictory physical and logical absent-state markers.");
        if(physical.IdentityOnly && !state.IdentityOnly || physical.IdentityMetadataRequired && !state.IdentityMetadataRequired)
            throw new StoreException(Failure.Definite,"Contradictory logical and physical state-free identity headers.");
        var row=physical with{State=state.State,IdentityOnly=state.IdentityOnly,IdentityMetadataRequired=state.IdentityMetadataRequired, FoundationNullState = absent};
        StoreBatchRules.ValidateIdentity(row);
        return row;
    }
    /// <summary>Decodes a present state cell. Use Restore for rows that can carry absent state.</summary>
    public static (string State,bool IdentityOnly,bool IdentityMetadataRequired) Decode(string stored)
    {
        var state = DecodeEnvelope(stored);
        if (state.Absent)
            throw new StoreException(Failure.Definite, "An absent state cell requires row restoration.");
        return (state.State, state.IdentityOnly, state.IdentityMetadataRequired);
    }

    private static (string State,bool IdentityOnly,bool IdentityMetadataRequired,bool Absent) DecodeEnvelope(string stored)
    {
        if(!stored.StartsWith(Family,StringComparison.Ordinal))return(stored,false,false,false);
        if (stored == Absent) return ("null", false, false, true);
        if(stored==Identity)return("null",true,false,false);
        if(stored==MetadataIdentity)return("null",true,true,false);
        if(stored.StartsWith(Data,StringComparison.Ordinal) && stored[Data.Length..].StartsWith(Family,StringComparison.Ordinal))
            return(stored[Data.Length..],false,false,false);
        throw new StoreException(Failure.Definite,"Unsupported or malformed physical state control envelope; original bytes must be retained.");
    }
}
