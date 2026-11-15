namespace Kernel.Store.Conformance;

internal static class StoredStateChecks
{
    internal static readonly Case Case=new("state envelope: opaque escaping, identity and future refusal",Level.Required,Run);
    private static async Task Run(Context c)
    {
        var values=new[]{"", "null", "{}",StoredState.Identity,StoredState.Data,StoredState.Data+StoredState.Identity,
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
        foreach(var future in new[]{StoredState.Family,StoredState.Family+"2:identity-only",StoredState.Family+"1:unknown",StoredState.Data+"invalid-escape"})
        {
            try{StoredState.Restore(identity with{State=future});throw new ConformanceException("unknown envelope accepted");}
            catch(StoreException e)when(e.Failure==Failure.Definite){}
        }
        foreach(var invalid in new[]{identity with{Retired=true},identity with{Id="0/not-keyed"},identity with{State="payload"}})
        {
            try{StoredState.Encode(invalid);throw new ConformanceException("invalid identity shape accepted");}
            catch(StoreException e)when(e.Failure==Failure.Definite){}
        }
        var marker=CreationProtocol.Retire(identity);
        Context.That(!marker.IdentityOnly && StoredState.Encode(marker)=="null","canonical retirement marker acquired an identity envelope");
    }
}
