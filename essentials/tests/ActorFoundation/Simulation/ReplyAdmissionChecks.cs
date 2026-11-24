namespace Kernel.Sim;

internal static class ReplyAdmissionChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        foreach (var explicitTarget in new[] { false, true })
        {
            var (w, owner, id) = await New();
            var prior = Http.Spec(Http.Respond) with { Creation = new(1, 0, "prior") };
            w.Apply(new("add", M: new("first", id, Kinds.Start, Start: prior, TargetCreation: 1)));
            await Pump(owner); w.Apply(new("add", M: new("delete", id, Kinds.Delete, TargetCreation: 1))); await Pump(owner);
            var start = Http.Spec(Http.Wait) with { Creation = new(2, 1, "next") };
            var admitted = w.Apply(new("resume-request", 0, M: new("next", id, "resume", Start: start, TargetCreation: explicitTarget ? 2 : null, ReplyTo: "q1")));
            await Pump(owner); var born = w.Store.Instances[id.Id]; var caller = w.Asked.Single().Request;
            var cancelled = w.Apply(new("disconnect", S: caller.Registration, Flag: true));
            var command = w.Store.Messages.Values.Single(m => m.Message.Kind == "cancel").Message; await Pump(owner);
            check(admitted == "added" && born is { Creation: 2, Revision: 1, Status: InstanceStatus.Running } &&
                born.StartIntent == CreationProtocol.Fingerprint(start, "resume") && cancelled == "cancel added" && command.TargetCreation == 2 &&
                w.Store.Instances[id.Id].Status == InstanceStatus.Cancelled && caller.Error == "disconnected",
                $"durable creator cancellation keeps its addressed generation, explicit={explicitTarget}");
        }
        foreach (var variant in new[] { "different", "retained", "other target", "swept" })
        {
            var (w, owner, id) = await New(); var start = Http.Spec(Http.Wait) with { Creation = new(1, 0, "first") };
            w.Apply(new("request", 0, S: Http.Wait, M: new("first", id, Kinds.Start, Start: start, TargetCreation: 1, ReplyTo: "|q1")));
            await Pump(owner); var caller = w.Asked.Single().Request; var expected = "cancel-" + caller.Registration;
            var target = variant == "other target" ? InstanceRef.In(0, "other") : id;
            var input = new Message(variant == "different" ? "unrelated" : expected, target, "keepalive", TargetCreation: 1,
                KeepUntil: variant == "swept" ? null : w.Time + 3_600_000);
            var admitted = w.Apply(new("add", M: input)); await Pump(owner);
            var cancelled = w.Apply(new("disconnect", S: caller.Registration, Flag: true)); await Pump(owner);
            var preserved = variant == "swept" ? !w.Store.Messages.Values.Any(m => m.Message.Id == input.Id && m.Message.Kind == "keepalive") :
                w.Store.Messages.TryGetValue(input.InboxKey, out var kept) && kept.Message == input && kept.Done;
            check(admitted == "added" && cancelled == "cancel added" && preserved && caller.Error == "disconnected" &&
                w.Store.Instances[id.Id].Status == InstanceStatus.Cancelled &&
                w.Store.Messages.Values.Any(m => m.Message.Kind == "cancel" && m.Message.TargetCreation == 1 && m.Done),
                $"an unrelated ordinary input cannot absorb a caller's cancellation, {variant}");
        }
        foreach (var inlet in new[] { "request", "resume-request" })
        foreach (var rejection in new[] { "unsupported", Kinds.Job, Kinds.Meet })
        {
            var (w, owner, id) = await New(); var legacy = Http.Spec(Http.Wait);
            w.Apply(new("add", M: new("initial", id, Kinds.Start, Start: legacy))); await Pump(owner);
            var unsupported = rejection == "unsupported"; w.Store.CreationIdentityAvailable = !unsupported;
            var start = unsupported ? legacy with { Creation = new(1, 0, "positive") } : legacy;
            Exception? failure = null;
            try
            {
                w.Apply(new(inlet, 0, M: new("invalid", id, unsupported ? Kinds.Start : rejection, Start: start,
                    TargetCreation: unsupported ? 1 : null, ReplyTo: inlet == "request" ? "|q1" : "q1")));
            }
            catch (Exception error) { failure = error; }
            check((unsupported ? failure is StoreException { Failure: Failure.Definite } : failure is ArgumentException) &&
                w.Processes[owner.Node].All.Count == 0 && w.Asked.Count == 0 && !w.Store.Messages.ContainsKey("invalid"),
                $"a rejected {inlet} preserves its error and releases the provisional registration, {rejection}");
        }
    }

    private static async Task<(World, Worker, InstanceRef)> New()
    {
        var world = new World(new Config(1, 1, Flaw.None)) { Auto = true };
        var worker = world.Of(0, 0).Worker; await world.Runtimes[0].ActivateAsync(0);
        return (world, worker, InstanceRef.In(0, "admission"));
    }

    private static async Task Pump(Worker worker)
    {
        for (var pass = 0; pass < 12; pass++) { await worker.PollAsync(); await worker.RunAsync(); }
    }
}
