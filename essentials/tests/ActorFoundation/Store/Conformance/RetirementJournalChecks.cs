namespace Kernel.Store.Conformance;

internal static class RetirementJournalChecks
{
    internal static readonly Case Case=new("physical retirement: deletion journal refusal preserves projected state",Level.Required,Run);
    private static async Task Run(Context c)
    {
        const string id="g0/child/0.81.1",other="0/retirement-companion",key="retirement-wait";
        var lease=await c.Take(0);var row=Make.Row(id,1,status:InstanceStatus.Finished);
        await c.Lands(()=>c.Commit(lease,0,[Make.Turn(id,row)]));
        var projector=new Projector(c.S,0);await projector.CatchUpAsync();
        var original=(await c.S.LoadAsync([id])).Single();var views=await c.S.ReadViewsAsync([id]);
        Context.Equal(1,views.Count,"projection setup");var journal=await c.S.ReadJournalAsync(0,0,100);
        var projection=await c.S.ProjectedAsync(0);var inbox=await c.All(0);
        var retirement=Make.Delete(id,1)with{Row=CreationProtocol.Retire(row),Journal=null};
        var companion=Make.Turn(other,Make.Row(other,1),sent:[Make.M("retirement-send",0)],subscribed:[new(key,other,"wait")]);
        try
        {
            await c.Commit(0,lease.Epoch,1,[companion,retirement]);
            throw new ConformanceException("row-bearing retirement without deletion journal was not definitely refused");
        }
        catch(StoreException e)when(e.Failure==Failure.Definite){}
        Context.Equal(original,(await c.S.LoadAsync([id])).Single(),"refused retirement changed original row");
        Context.Equal(0,(await c.S.LoadAsync([other])).Count,"refused retirement leaked companion");
        Context.That(journal.SequenceEqual(await c.S.ReadJournalAsync(0,0,100)),"refused retirement changed journal");
        Context.That(views.SequenceEqual(await c.S.ReadViewsAsync([id])),"refused retirement changed view");
        Context.Equal(projection,await c.S.ProjectedAsync(0),"refused retirement changed projector bounds");
        Context.That(inbox.SequenceEqual(await c.All(0))&&(await c.S.FindSubscriptionsAsync(key)).Count==0,"refused retirement leaked send/wait");
        await projector.CatchUpAsync();
        Context.That(views.SequenceEqual(await c.S.ReadViewsAsync([id])),"catch-up changed view after refused retirement");
        await c.Lands(()=>c.Commit(0,lease.Epoch,1,[companion,retirement with{Journal="[\"deleted\"]"}]));
        await projector.CatchUpAsync();
        Context.That((await c.S.LoadAsync([id])).Single().Retired&&(await c.S.LoadAsync([other])).Count==1,"valid retirement/companion failed");
        Context.Equal(0,(await c.S.ReadViewsAsync([id])).Count,"valid retirement left stale view");
        Context.Equal(1,(await c.S.ReadJournalOfAsync(0,id,0)).Count(j=>j.Revision<0),"valid retirement omitted deletion journal");
        // Trusted legacy Row:null deletion remains admissible without a journal; no new promise is added to it.
        await c.Lands(()=>c.Commit(0,lease.Epoch,3,[Make.New("0/legacy-raw")]));
        await c.Lands(()=>c.Commit(0,lease.Epoch,4,[Make.Delete("0/legacy-raw",1)with{Journal=null}]));
        Context.Equal(0,(await c.S.LoadAsync(["0/legacy-raw"])).Count,"trusted legacy null-row delete changed");
    }
}
