namespace Kernel.Store.Conformance;

internal static class StoredStateChecks
{
    internal static readonly Case Case=new("state envelope: opaque escaping, identity and future refusal",Level.Required,Run);
    internal static readonly Case AbsentState = new("state envelope: absent actor state survives a physical reload", Level.Required, CheckAbsentState);

    private static async Task CheckAbsentState(Context c)
    {
        const string id = "0/checkpoint";
        var lease = await c.Take(0);
        var absent = Make.Row(id, 1, state: "null") with { FoundationNullState = true };
        var literal = absent with { Revision = 2, FoundationNullState = false };
        await c.Lands(() => c.Commit(lease, 0, [Make.Turn(id, absent)]));
        var reader = c.Provided.NewClient();
        Context.That((await reader.LoadAsync([id])).Single() == absent, "physical reload changed absent actor state");

        await c.Refused(() => c.Commit(0, lease.Epoch, lease.Position, [Make.Turn(id, literal, expected: 1)]));
        Context.Equal(absent, (await reader.LoadActorsAsync([id])).Single().Row, "refused commit changed absent actor state");
        Context.Equal(1, (await reader.ReadJournalOfAsync(0, id, 0)).Count, "refused change appended journal history");

        await c.Lands(() => c.Commit(0, lease.Epoch, lease.Position + 1, [Make.Turn(id, literal, expected: 1)]));
        Context.Equal(literal, (await reader.LoadAsync([id])).Single(), "opaque null text reloaded as absent state");
        Context.Equal(2, (await reader.ReadJournalOfAsync(0, id, 0)).Count, "journal after state change");

        var invalid = literal with { Revision = 3, State = "opaque", FoundationNullState = true };
        await c.Refused(() => c.Commit(0, lease.Epoch, lease.Position + 2, [Make.Turn(id, invalid, expected: 2)]));
        Context.Equal(literal, (await reader.LoadAsync([id])).Single(), "contradictory absent-state flag replaced opaque state");
        Context.Equal(2, (await reader.ReadJournalOfAsync(0, id, 0)).Count, "invalid checkpoint appended journal history");
    }

    private static async Task Run(Context c)
    {
        var values=new[]{"", "null", "{}",StoredState.Identity,StoredState.MetadataIdentity,StoredState.Absent,StoredState.Family+"1:identity-only",StoredState.Data,StoredState.Data+StoredState.Identity,
            StoredState.Family+"2:future/🌐",StoredState.Family+"1:unknown"};
        var lease=await c.Take(0);long position=lease.Position;
        for(var i=0;i<values.Length;i++)
        {
            var id="g0/key/"+(650+i);var row=Make.Row(id,1,state:values[i],status:InstanceStatus.Finished);
            var encoded=StoredState.Encode(row);var decoded=StoredState.Restore(row with{State=encoded});
            Context.Equal(row,decoded,"pure opaque state-cell roundtrip");
            await c.Lands(()=>c.Commit(0,lease.Epoch,position,[Make.Turn(id,row)]));position++;
            Context.Equal(row,(await c.S.LoadAsync([id])).Single(),"physical opaque state-cell roundtrip");
            Context.Equal(row,(await c.S.LoadActorsAsync([id])).Single().Row,"actor lookup opaque state-cell roundtrip");
        }
        var identity=Make.Row("g0/key/700",1,state:"null",status:InstanceStatus.Finished) with{IdentityOnly=true};
        Context.Equal(identity,StoredState.Restore(identity with{State=StoredState.Encode(identity)}),"pure explicit identity roundtrip");
        await c.Lands(()=>c.Commit(0,lease.Epoch,position,[Make.Turn(identity.Id,identity)]));
        Context.Equal(identity,(await c.S.LoadAsync([identity.Id])).Single(),"physical identity roundtrip");
        Context.Equal(identity,(await c.S.LoadActorsAsync([identity.Id])).Single().Row,"actor lookup explicit identity roundtrip");
        foreach(var future in new[]{StoredState.Family,StoredState.Family+"3:identity-only",StoredState.Family+"1:identity-only",StoredState.Family+"1:unknown",StoredState.Data+"invalid-escape"})
        {
            try{StoredState.Restore(identity with{State=future});throw new ConformanceException("unknown envelope accepted");}
            catch(StoreException e)when(e.Failure==Failure.Definite){}
        }
        foreach(var invalid in new[]{identity with{Retired=true},identity with{Id="0/not-keyed"},identity with{State="payload"}})
        {
            try{StoredState.Encode(invalid);throw new ConformanceException("invalid identity shape accepted");}
            catch(StoreException e)when(e.Failure==Failure.Definite){}
        }
        var required=identity with{IdentityMetadataRequired=true};
        Context.Equal(required,StoredState.Restore(required with{State=StoredState.Encode(required)}),"required protocol roundtrip");
        var marker=CreationProtocol.Retire(required);
        Context.That(!marker.IdentityOnly && !marker.IdentityMetadataRequired && StoredState.Encode(marker)=="null","canonical retirement marker acquired an identity envelope");
    }
}
