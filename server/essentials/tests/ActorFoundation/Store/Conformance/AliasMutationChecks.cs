namespace Kernel.Store.Conformance;

internal static class AliasMutationChecks
{
    internal static readonly Case Case=new("batch integrity: provider-equal aliases retain revision and retirement guards",Level.Required,Run);
    private static async Task Run(Context c)
    {
        const string id="g0/key/771",alias="g0/KEY/771",unrelated="0/alias-unrelated";
        var row=Make.Row(id,1,status:InstanceStatus.Finished);var lease=await c.Take(0);
        await c.Lands(()=>c.Commit(lease,0,[Make.Turn(id,row)]));
        var equal=(await c.S.LoadAsync([alias])).Count==1;
        c.Notes.Add("database accepted key alias: "+equal);
        if(!equal)return; // the ordinary exact-identity case covers ordinal providers separately
        var journal=await c.S.ReadJournalAsync(0,0,10);
        async Task Refuse(params TurnCommit[] turns)
        {
            await c.Refused(()=>c.Commit(0,lease.Epoch,1,[Make.New(unrelated),..turns]));
            Context.That((await c.S.LoadAsync([unrelated])).Count==0 && (await c.S.LoadAsync([id])).Single()==row,
                "aliased mutation partially committed");
            Context.That(journal.SequenceEqual(await c.S.ReadJournalAsync(0,0,10)),"aliased mutation changed history");
        }
        var retire=Make.Turn(alias,CreationProtocol.Retire(row),expected:1) with{Delete=true};
        await Refuse(Make.Turn(id,row with{Revision=2},expected:1),Make.Turn(alias,row with{Id=alias,Revision=2},expected:1));
        await Refuse(retire,Make.Turn(alias,row with{Id=alias,Revision=2},expected:1));
        await Refuse(retire,Make.Turn(id,row with{Id=alias,Revision=3},expected:2));
        await c.Lands(()=>c.Commit(0,lease.Epoch,1,[retire]));
        var marker=CreationProtocol.Retire(row);
        Context.Equal(marker,(await c.S.LoadAsync([id])).Single(),"aliased retirement changed physical identity/status");
        await c.Refused(()=>c.Commit(0,lease.Epoch,2,[Make.New(unrelated),Make.Turn(alias,row with{Id=alias,Revision=3},expected:2)]));
        Context.That((await c.S.LoadAsync([unrelated])).Count==0 && (await c.S.LoadAsync([id])).Single()==marker,
            "aliased update resurrected retirement or leaked unrelated row");
    }
}
