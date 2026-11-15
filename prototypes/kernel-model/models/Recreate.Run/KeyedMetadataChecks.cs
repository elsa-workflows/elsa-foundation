using Kernel.Durable;
using Kernel.Runtime;
using Kernel.Workflows;

internal static class KeyedMetadataChecks
{
    internal static async Task Run(string label,IKernelStore store,IKeyAdmission ingress,Func<long,Task> waitUntil,Func<long>? trips=null,Func<string,Task<int>>? storedBytes=null)
    {
        foreach(var ephemeral in new[]{true,false})
        {
            var request=new KeyRequest("value-"+Guid.NewGuid().ToString("N"),"first",Kinds.Start,
                Start:new("value-probe","terminal",Ephemeral:ephemeral),KeepForMillis:1);
            var receipt=await ingress.AcceptAsync(request);var id=receipt.RunId!;
            Worker Owner()=>new(Guid.NewGuid().ToString("N"),0,store,new WorkflowInstance([new ValueProbe()]));
            async Task Pump(Worker worker){await worker.WokenAsync(null);for(var i=0;i<12;i++)await worker.RunAsync();}
            var old=Owner();await new DurableHost([old]).ActivateAsync(0);await old.PollAsync();await old.LoadAsync();await old.FlushAsync();
            old.RunTurn(id);var before=trips?.Invoke();await old.FlushAsync();var count=before.HasValue?trips!()-before.Value:(long?)null;
            var ended=(await store.LoadAsync([id])).Single();
            var physicalBytes=storedBytes is null?(int?)null:await storedBytes(id);
            Harness.Check(!ended.IdentityOnly && Json.Read<ValueState>(ended.State).Value==1,"metadata-required typed checkpoint lost");
            await old.DisposeAsync();await ingress.AcceptAsync(new(request.Key,"delete",Kinds.Delete));
            var next=Owner();await new DurableHost([next]).ActivateAsync(0);await Pump(next);
            Harness.Check((await store.LoadAsync([id])).Single().Retired,"cold value-type Delete did not retire");
            await waitUntil(receipt.KeepUntil!.Value);
            var newRun=await ingress.AcceptAsync(request with{Operation="next"});
            Harness.Check(newRun.Outcome=="accepted" && newRun.RunId!=id && await ingress.AcceptAsync(request)==receipt,"metadata promotion failed rebind/replay");
            await Pump(next);await next.DisposeAsync();
            Console.WriteLine($"{label}: raw value-type metadata ephemeral={ephemeral}, typed promotion, cold Delete and rebind passed; terminal trips={count?.ToString()??"memory"}; logical state UTF8 bytes={System.Text.Encoding.UTF8.GetByteCount(ended.State)}, physical state-cell bytes={physicalBytes?.ToString()??"memory-record"}.");
            if(count.HasValue)Harness.Check(count==2,"metadata-required terminal promotion added a wire round trip");
        }
        var stateFree=new StateFreeValueProbe();
        var requestFree=new KeyRequest("state-free-"+Guid.NewGuid().ToString("N"),"first",Kinds.Start,
            Start:new(stateFree.Key,"terminal",Ephemeral:true),KeepForMillis:1);
        var firstFree=await ingress.AcceptAsync(requestFree);var physical=firstFree.RunId!;
        Worker FreeOwner()=>new(Guid.NewGuid().ToString("N"),0,store,new WorkflowInstance([stateFree]));
        async Task Drain(Worker worker){await worker.WokenAsync(null);for(var i=0;i<12;i++)await worker.RunAsync();}
        var warm=FreeOwner();await new DurableHost([warm]).ActivateAsync(0);await warm.PollAsync();await warm.LoadAsync();await warm.FlushAsync();
        warm.RunTurn(physical);var from=trips?.Invoke();await warm.FlushAsync();var freeTrips=from.HasValue?trips!()-from.Value:(long?)null;
        var freeBytes=storedBytes is null?(int?)null:await storedBytes(physical);
        Harness.Check((await store.LoadAsync([physical])).Single() is {IdentityOnly:true,State:"null"},"state-free opt-in retained model state");
        await warm.DisposeAsync();await ingress.AcceptAsync(new(requestFree.Key,"delete-guarded",Kinds.Delete));
        var cold=FreeOwner();await new DurableHost([cold]).ActivateAsync(0);await Drain(cold);
        Harness.Check(!(await store.LoadAsync([physical])).Single().Retired && stateFree.TypedCalls==0,"state-free deletion protection ignored or typed state reconstructed");
        await waitUntil(firstFree.KeepUntil!.Value);
        Harness.Check((await ingress.AcceptAsync(requestFree with{Operation="next"})).Outcome=="in-use","protected metadata run displaced");
        stateFree.Protect=false;await ingress.AcceptAsync(new(requestFree.Key,"delete-unprotected",Kinds.Delete));await Drain(cold);
        Harness.Check((await store.LoadAsync([physical])).Single().Retired,"state-free metadata did not retire after protection released");
        Harness.Check((await ingress.AcceptAsync(requestFree with{Operation="next"})).Outcome=="accepted" && await ingress.AcceptAsync(requestFree)==firstFree,"state-free metadata rebind/replay");
        await Drain(cold);await cold.DisposeAsync();
        if(freeTrips.HasValue)Harness.Check(freeTrips==2,"state-free metadata terminal added a wire round trip");
        Console.WriteLine($"{label}: state-free value-type opt-in, protection, cold Delete and rebind passed; terminal trips={freeTrips?.ToString()??"memory"}; logical state UTF8 bytes=4, physical state-cell bytes={freeBytes?.ToString()??"memory-record"}.");
    }
}
