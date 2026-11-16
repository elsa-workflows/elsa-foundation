namespace Kernel.Store.Conformance;

internal static class MarkerInsertChecks
{
    internal static readonly Case Case=new("physical retirement: invalid first marker never becomes an active INSERT",Level.Required,Run);
    private static async Task Run(Context c)
    {
        var lease=await c.Take(0);const string id="g0/key/991";
        var marker=CreationProtocol.Retire(Make.Row(id,0,status:InstanceStatus.Finished));
        var creator=new Message("invalid-marker-creator",new("g0/key/992"),Kinds.Start,Start:new("counter","e",Ephemeral:true)).WithInboxScope(long.MaxValue);
        var variants=new[]{
            new TurnCommit(id,marker,null,[],[],[creator],[],[],"[]",Delete:true),
            new TurnCommit(id,marker,null,[],[],[creator],[],[],"[]"),
            new TurnCommit("g0/KEY/991",marker,null,[],[],[creator],[],[],"[]",Delete:true),
            new TurnCommit(id,marker with{IdentityOnly=true},null,[],[],[creator],[],[],"[]",Delete:true),
            new TurnCommit("0/named",marker with{Id="0/named"},null,[],[],[creator],[],[],"[]",Delete:true),
            new TurnCommit(id,marker with{State="payload"},null,[],[],[creator],[],[],"[]",Delete:true),
            new TurnCommit(id,marker with{Status=InstanceStatus.Running},null,[],[],[creator],[],[],"[]",Delete:true),
            new TurnCommit(id,marker,0,[],[],[creator],[],[],"[]",Delete:true),
            new TurnCommit(id,marker with{Creation=1},null,[],[],[creator],[],[],"[]",Delete:true),
        };
        foreach(var invalid in variants)
        {
            await c.Refused(()=>c.Commit(lease,0,[Make.New("0/marker-unrelated"),invalid]));
            Context.That((await c.S.LoadAsync([id,"g0/KEY/991","0/named","0/marker-unrelated"])).Count==0,"invalid marker materialized a row");
            Context.That((await c.All(0)).Count==0 && (await c.S.ReadJournalAsync(0,0,20)).Count==0,"invalid marker leaked creator/history");
            Context.Equal(lease.Position,(await c.S.ProjectedAsync(0)).Written,"invalid marker moved position");
        }
        var row=Make.Row(id,1,status:InstanceStatus.Finished);
        await c.Lands(()=>c.Commit(lease,0,[Make.Turn(id,row)]));
        Context.Equal(row,(await c.S.LoadAsync([id])).Single(),"active INSERT default disagreed with header");
        var retire=new TurnCommit(id,CreationProtocol.Retire(row),1,[],[],[],[],[],"[]",Delete:true);
        await c.Lands(()=>c.Commit(0,lease.Epoch,lease.Position+1,[retire]));
        Context.Equal(CreationProtocol.Retire(row),(await c.S.LoadAsync([id])).Single(),"retirement UPDATE failed to persist true");
    }
}
