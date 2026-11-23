using Kernel.Durable;
using Kernel.Workflows;

internal static class UnicodeKeys
{
    internal static async Task Run(string label,IKernelStore store,IKeyAdmission ingress,Func<long>? requestsCount=null,Func<long,Task>? waitUntil=null,Func<long>? tcpEdges=null)
    {
        var scope=Guid.NewGuid().ToString("N");
        var random=new Random(9041);
        string Wide()=>new(Enumerable.Range(0,4096).Select(_=>(char)random.Next(65536)).ToArray());
        var wideKey=Wide();var wideOperation=Wide();
        var keys=new[]{"plain","nul\0x","high\uD800","low\uDFFF","replacement\uFFFD","astral🌐",wideKey,"long-operation"};
        var accepted=new HashSet<string>(StringComparer.Ordinal);
        var requests=keys.Select(key=>new KeyRequest(scope+key,(key.Length>1000 || key=="long-operation")?wideOperation:"create\0\uD800",
            Kinds.Start,Start:new("probe","child",Ephemeral:true),KeepForMillis:1)).ToArray();
        var gate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending=requests.Select(async request=>{await gate.Task;return await ingress.AcceptAsync(request);}).ToArray();
        var from=requestsCount?.Invoke();var tcpFrom=tcpEdges?.Invoke();gate.SetResult();var firsts=await Task.WhenAll(pending);
        var exchanges=from.HasValue?requestsCount!()-from.Value:(long?)null;
        var edges=tcpFrom.HasValue?tcpEdges!()-tcpFrom.Value:(long?)null;
        if(exchanges.HasValue)Harness.Check(exchanges==requests.Length,"warm concurrent lossless admissions added Execute frames");
        for(var i=0;i<requests.Length;i++)
        {
            var request=requests[i];var first=firsts[i];
            Harness.Check(first.Outcome=="accepted" && accepted.Add(first.RunId!),"lossless key identities aliased");
            Harness.Check(await ingress.AcceptAsync(request)==first,"lossless operation receipt changed");
            Harness.Check((await ingress.AcceptAsync(request with{Payload="changed"})).Outcome=="conflict","lossless digest conflict");
            var operation=i>=keys.Length-2?wideOperation+"query\0\uDFFF":"query\0\uDFFF";
            var command=new KeyRequest(request.Key,operation,"signal",Json.Write("unicode"));
            var second=command with{Operation=operation+"\uFFFD"};
            var commands=await Task.WhenAll(Task.Run(async()=>await ingress.AcceptAsync(command)),Task.Run(async()=>await ingress.AcceptAsync(second)));
            Harness.Check(commands.All(d=>d.Outcome=="accepted" && d.RunId==first.RunId),"concurrent lossless operation identities aliased");
            Harness.Check(await ingress.AcceptAsync(command)==commands[0] && (await ingress.AcceptAsync(second with{Payload="changed"})).Outcome=="conflict","long/NUL operation replay/conflict");
        }
        var owner=new Worker(Guid.NewGuid().ToString("N"),0,store,new WorkflowInstance([new Probe()]));
        await new DurableHost([owner]).ActivateAsync(0);await owner.WokenAsync(null);
        for(var i=0;i<20;i++)await owner.RunAsync();
        var rows=await store.LoadAsync(accepted.ToArray());
        Harness.Check(rows.Count==keys.Length && rows.All(r=>r.IdentityOnly && r.Status==InstanceStatus.Finished),"lossless key starts did not execute against their distinct physical runs");
        await owner.DisposeAsync();
        var deletes=requests.Select((request,i)=>new KeyRequest(request.Key,request.Operation+"delete\0\uD800",Kinds.Delete)).ToArray();
        await Task.WhenAll(deletes.Select(async d=>await ingress.AcceptAsync(d)));
        await using var cold=new Worker(Guid.NewGuid().ToString("N"),0,store,new WorkflowInstance([new Probe()]));
        await new DurableHost([cold]).ActivateAsync(0);await cold.WokenAsync(null);
        for(var i=0;i<20;i++)await cold.RunAsync();
        Harness.Check((await store.LoadAsync(accepted.ToArray())).All(r=>r.Retired),"lossless key cold Delete failed");
        if(waitUntil is not null)await waitUntil(firsts.Max(d=>d.KeepUntil!.Value));
        var rebound=await Task.WhenAll(requests.Select(async request=>await ingress.AcceptAsync(request with{Operation=request.Operation+"rebind\0\uDFFF"})));
        for(var i=0;i<requests.Length;i++)
        {
            Harness.Check(rebound[i].Outcome=="accepted" && accepted.Add(rebound[i].RunId!),"lossless key rebinding aliased physical identity");
            Harness.Check(await ingress.AcceptAsync(requests[i])==firsts[i],"lossless key retained receipt retargeted after rebind");
        }
        await cold.WokenAsync(null);for(var i=0;i<20;i++)await cold.RunAsync();
        Console.WriteLine($"{label}: eight exact UTF-16 key identities (including 4096-unit key/operation), concurrent embedded-NUL/unpaired/long operation replay/conflict, cold Delete/rebind and retained original receipts passed; warm initial Execute frames={exchanges?.ToString()??"memory"}, TCP edges={edges?.ToString()??"memory"} (separate observations, not an RTT identity).");
    }
}
