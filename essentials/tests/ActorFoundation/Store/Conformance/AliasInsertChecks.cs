namespace Kernel.Store.Conformance;

internal static class AliasInsertChecks
{
    internal static readonly Case Case=new("batch integrity: provider-equal raw inserts and unrelated mixed rollback",Level.Required,Run);
    private static async Task Run(Context c)
    {
        var lease=await c.Take(0);long position=lease.Position;var serial=0;
        foreach(var (name,spelling)in new[]{("Invoice","invoice"),("café","CAFÉ"),("space","space "),("A","B")})
        {
            var prefix="0/insert-"+(++serial)+"/";var id=prefix+name;var alias=prefix+spelling;var unrelated=prefix+"other";
            var look=await c.S.LoadActorsAsync([id,alias]);var equal=look.Select(x=>x.Canonical).Distinct(StringComparer.Ordinal).Count()==1;
            var before=await c.S.ReadJournalAsync(0,0,100);var messages=(await c.All(0)).Count;
            var row=Make.Row(id,1);var sent=new Message("alias-insert-"+serial,new("0/target"),"signal");
            var turns=new[]{Make.New(unrelated),Make.Turn(alias,row,sent:[sent])};
            if(equal)
            {
                await c.Lands(()=>c.Commit(0,lease.Epoch,position,turns));position+=2;
                Context.Equal(row,(await c.S.LoadAsync([id])).Single(),"new alias insert changed the stored row");
                Context.Equal(row,(await c.S.LoadAsync([alias])).Single(),"new insert alias did not resolve");
                Context.Equal(messages+1,(await c.All(0)).Count,"new alias insert lost its send");
                // Multiple pending creators for the same database identity still have one winner.
                var raceId=prefix+"race/"+name;var raceAlias=prefix+"race/"+spelling;
                var results=await Task.WhenAll(Enumerable.Range(0,4).Select(async n=>
                {
                    try{return(await c.Commit(0,lease.Epoch,position,[Make.New(prefix+"race-other-"+n),Make.Turn(raceAlias,Make.Row(raceId,1))])).Landed;}
                    catch(StoreException){return false;}
                }));
                Context.Equal(1,results.Count(x=>x),"concurrent aliased inserts did not have one winner");position+=2;
                Context.Equal(1,(await c.S.LoadAsync(Enumerable.Range(0,4).Select(n=>prefix+"race-other-"+n).ToArray())).Count,"losing aliased insert leaked unrelated row");
            }
            else
            {
                await c.Refused(()=>c.Commit(0,lease.Epoch,position,turns));
                Context.That((await c.S.LoadAsync([id,alias,unrelated])).Count==0,"unrelated new insert partially committed");
                Context.That(before.SequenceEqual(await c.S.ReadJournalAsync(0,0,100)) && messages==(await c.All(0)).Count,"unrelated new insert leaked history/send");
            }
            c.Notes.Add(name+"/"+spelling+" accepted equality="+equal);
        }
    }
}
