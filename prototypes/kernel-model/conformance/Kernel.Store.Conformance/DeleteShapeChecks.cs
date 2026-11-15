namespace Kernel.Store.Conformance;

internal static class DeleteShapeChecks
{
    internal static readonly Case Case=new("batch integrity: raw Delete shapes and exact stored revisions",Level.Required,Run);
    private static async Task Run(Context c)
    {
        var lease=await c.Take(0);long position=0;
        var rows=new[]{Make.Row("g0/child/0.91.1",1,status:InstanceStatus.Finished),
            Make.Row("g0/key/9100",0,status:InstanceStatus.Finished),Make.Row("g0/key/9101",-1,status:InstanceStatus.Finished)};
        await c.Lands(()=>c.Commit(0,lease.Epoch,position,rows.Select(r=>Make.Turn(r.Id,r)).ToArray()));position+=rows.Length;
        var source=rows[0];var marker=CreationProtocol.Retire(source);
        var valid=Make.Turn(source.Id,marker,expected:source.Revision)with{Delete=true};
        var malformed=new[]{valid with{Row=source with{Revision=2}},valid with{Row=source,Expected=null},
            valid with{Row=null,Expected=null},valid with{Row=null,Expected=0},valid with{Row=null,Expected=-1},
            valid with{Expected=null},valid with{Expected=long.MaxValue},
            valid with{Row=marker with{Revision=3}},valid with{Row=marker with{State="payload"}},
            valid with{Row=marker with{Model="extra"}},valid with{Row=marker with{Status=InstanceStatus.Running}},
            valid with{Row=marker with{Status=InstanceStatus.Cancelled}},
            valid with{Row=marker with{IdentityOnly=true}},valid with{Row=marker with{IdentityMetadataRequired=true}},
            valid with{Row=marker with{Id="0/named"},Instance="0/named"}};
        var serial=0;
        async Task Refuse(TurnCommit bad)
        {
            var other="0/delete-unrelated-"+(++serial);var journal=await c.S.ReadJournalAsync(0,0,100);
            var inbox=await c.All(0);var waits=await c.S.FindSubscriptionsAsync("delete-key-"+serial);
            var effect=Make.Turn(other,Make.Row(other,1),sent:[Make.M("delete-send-"+serial,0)],
                subscribed:[new("delete-key-"+serial,other,"wait")]);
            await c.Refused(()=>c.Commit(0,lease.Epoch,position,[effect,bad]));
            Context.Equal(0,(await c.S.LoadAsync([other])).Count,"malformed Delete leaked unrelated row");
            Context.That(rows.SequenceEqual((await c.S.LoadAsync(rows.Select(r=>r.Id).ToArray())).OrderBy(r=>Array.FindIndex(rows,x=>x.Id==r.Id))),"malformed Delete changed a seeded actor");
            Context.That(journal.SequenceEqual(await c.S.ReadJournalAsync(0,0,100)) && inbox.SequenceEqual(await c.All(0)) &&
                waits.SequenceEqual(await c.S.FindSubscriptionsAsync("delete-key-"+serial)),"malformed Delete leaked history/send/wait");
            Context.Equal(position,(await c.S.ProjectedAsync(0)).Written,"malformed Delete advanced position");
        }
        foreach(var bad in malformed)await Refuse(bad);
        foreach(var expected in new long?[]{null,0,-1,1,long.MinValue,long.MaxValue})
        {
            await Refuse(Make.Turn("g0/key/9199",expected:expected)with{Delete=true});
            if(expected is not null && expected!=long.MaxValue)
            {
                var absent=CreationProtocol.Retire(Make.Row("g0/key/9199",expected.Value,status:InstanceStatus.Finished));
                await Refuse(Make.Turn(absent.Id,absent,expected:expected)with{Delete=true});
            }
        }
        foreach(var row in rows.Skip(1))
        {
            await c.Lands(()=>c.Commit(0,lease.Epoch,position,[Make.Delete(row.Id,row.Revision)]));position++;
            Context.Equal(0,(await c.S.LoadAsync([row.Id])).Count,"exact stored zero/negative revision raw deletion failed");
        }
        await c.Lands(()=>c.Commit(0,lease.Epoch,position,[valid]));position++;
        Context.Equal(marker,(await c.S.LoadAsync([source.Id])).Single(),"canonical retirement changed shape/status");
        await c.Lands(()=>c.Commit(0,lease.Epoch,position,[Make.Delete(source.Id,marker.Revision)]));
        Context.Equal(0,(await c.S.LoadAsync([source.Id])).Count,"exact raw deletion of retained marker failed");
        c.Notes.Add(serial+" malformed/missing/wrong-revision mixed Delete refusals; exact stored 0/-1 and canonical marker/raw controls");
    }
}
