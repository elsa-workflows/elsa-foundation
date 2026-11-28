namespace Kernel.Sim;

internal static class ChildCompletionRecoveryChecks
{
    private static async Task Pump(Worker w) { for (var i = 0; i < 8; i++) { await w.PollAsync(); await w.RunAsync(); } }
    private static Worker Open(InMemoryStore s, Family f) => new("owner", 0, s, new WorkflowInstance([f]), new() { Monotonic = () => 100 });
    private static StartSpec Spec(int g) => new("recovery-family", "parent", History: History.Minimal, Creation: g == 0 ? null : new(g, g - 1, "family-" + g));
    private static Message Input(string key, InstanceRef id, string kind, int g, StartSpec? spec = null, string? payload = null) =>
        new(key, id, kind, payload, Start: spec, TargetCreation: g == 0 ? null : g);

    internal static async Task Run(Action<bool, string> check)
    {
        foreach (var g in new[] { 0, 1, 2 })
        foreach (var completion in new[] { false, true })
        foreach (var prefix in g == 1 ? new[] { false, true } : new[] { false })
        {
            var s = new InMemoryStore(1, () => 100); var f = new Family(); var id = InstanceRef.In(0, "birth");
            await using var w = Open(s, f); await new DurableHost([w]).ActivateAsync(0);
            if (g == 2)
            {
                await s.AddAsync(Input("old", id, Kinds.Start, 1, Spec(1))); await Pump(w);
                await s.AddAsync(Input("stop", id, Kinds.Terminate, 1)); await Pump(w);
                await s.AddAsync(Input("remove", id, Kinds.Delete, 1)); await Pump(w);
            }
            var before = s.Instances.GetValueOrDefault(id.Id); var calls = f.Calls;
            if (prefix) await s.AddAsync(new("before-creator", id, "unseen"));
            var m = Input("selected", id, completion ? WorkflowKinds.ChildFinished : Kinds.Start, g, Spec(g),
                completion ? Json.Write(new ChildFinished("0/fake", InstanceStatus.Finished, null, g)) : null);
            await s.AddAsync(m); await Pump(w);
            var rejected = g > 0 && completion;
            check((rejected ? s.Instances.GetValueOrDefault(id.Id) == before && f.Calls == calls :
                s.Instances[id.Id].Creation == g && f.Calls == calls + 1) && !s.Messages.ContainsKey(m.InboxKey),
                $"only a valid selected creator opens a parent, g{g}, completion={completion}, earlier input={prefix}");
        }

        foreach (var g in new[] { 0, 1 })
        {
            var s = new InMemoryStore(1, () => 100); var id = InstanceRef.In(0, "held"); var f = new Family { Defer = true }; Message notice;
            await using (var w = Open(s, f))
            {
                await new DurableHost([w]).ActivateAsync(0); await s.AddAsync(Input("birth", id, Kinds.Start, g, Spec(g))); await Pump(w);
                var before = s.Instances[id.Id]; var kid = Json.Read<List<string>>(before.State)[0];
                await s.AddAsync(Input("finish", new(kid), "finish", g));
                await w.PollAsync(); await w.LoadAsync(); await w.FlushAsync(); w.RunTurn(kid); await w.FlushAsync();
                notice = s.Messages.Values.Single(x => x.Message.Kind == WorkflowKinds.ChildFinished).Message;
                await Pump(w); var after = s.Instances[id.Id];
                check(s.Messages.ContainsKey(notice.InboxKey) && (g == 0 ? after.Children != before.Children : after.Children == before.Children),
                    $"a held qualified completion retains its registry while g0 keeps historical behavior, g{g}");
            }
            var next = new Family(); await using var recovered = Open(s, next); await new DurableHost([recovered]).ActivateAsync(0); await Pump(recovered);
            var applied = s.Instances[id.Id]; s.Messages[notice.InboxKey] = new(notice, 1000, 0); await Pump(recovered);
            check(next.Received.Count == (g == 0 ? 2 : 1) && (g == 0 || s.Instances[id.Id] == applied) &&
                Json.Read<System.Text.Json.JsonElement[]>(applied.Children!).Length == 1 && !s.Messages.ContainsKey(notice.InboxKey),
                $"a recovered held result applies once to its qualified registry, g{g}");
        }

        foreach (var status in new[] { InstanceStatus.Finished, InstanceStatus.Faulted, InstanceStatus.Cancelled })
        foreach (var exhausted in new[] { false, true })
        {
            var s = new InMemoryStore(1, () => 100); var id = InstanceRef.In(0, "late"); var f = new Family(); Message notice;
            await using (var initial = Open(s, f))
            {
                await new DurableHost([initial]).ActivateAsync(0); await s.AddAsync(Input("birth", id, Kinds.Start, 1, Spec(1))); await Pump(initial);
                var kid = Json.Read<List<string>>(s.Instances[id.Id].State)[0];
                await s.AddAsync(Input("end-parent", id, status.ToString(), 1)); await Pump(initial);
                await s.AddAsync(Input("end-child", new(kid), "finish", 1));
                await initial.PollAsync(); await initial.LoadAsync(); await initial.FlushAsync(); initial.RunTurn(kid); await initial.FlushAsync();
                notice = s.Messages.Values.Single(x => x.Message.Kind == WorkflowKinds.ChildFinished).Message;
            }
            var before = s.Instances[id.Id] with { Revision = exhausted ? long.MaxValue : long.MaxValue - 1 }; s.Instances[id.Id] = before;
            var failed = false; var next = new Family();
            await using (var recovered = Open(s, next))
            {
                await new DurableHost([recovered]).ActivateAsync(0);
                try { await Pump(recovered); } catch (OverflowException e) { failed = e.Message.StartsWith("Revision exhausted", StringComparison.Ordinal); }
            }
            check(exhausted ? failed && s.Instances[id.Id] == before && s.Messages.ContainsKey(notice.InboxKey) :
                !failed && s.Instances[id.Id] == (before with { Revision = long.MaxValue, Children = Json.Write(new[] { new CreationAddress(Json.Read<List<string>>(before.State)[1], 1) }) }) && !s.Messages.ContainsKey(notice.InboxKey),
                $"terminal-parent cleanup either advances or retains the exact pending input at exhaustion, {status}, exhausted={exhausted}");
            check(next.Calls == 0 && s.Ghost.Count == 0, $"late cleanup never enters the model or Park, {status}, exhausted={exhausted}");
        }

        foreach (var sign in new[] { "−", "minus" })
        foreach (var terminal in new[] { false, true })
        {
            var s = new InMemoryStore(1, () => 100); var id = InstanceRef.In(0, "legacy-sign"); var f = new Family();
            await using var w = Open(s, f); await new DurableHost([w]).ActivateAsync(0); await s.AddAsync(Input("birth", id, Kinds.Start, 1, Spec(1))); await Pump(w);
            if (terminal) { await s.AddAsync(Input("parent-finish", id, "finish", 1)); await Pump(w); }
            var kid = Json.Read<List<string>>(s.Instances[id.Id].State)[0]; var prior = s.Instances[id.Id];
            await s.AddAsync(Input("child-finish", new(kid), "finish", 1)); await w.PollAsync(); await w.LoadAsync(); await w.FlushAsync(); w.RunTurn(kid); await w.FlushAsync();
            var actual = s.Messages.Values.Single(x => x.Message.Kind == WorkflowKinds.ChildFinished).Message;
            var sequence = s.Messages[actual.InboxKey].Seq; await w.DisposeAsync();
            s.Messages.Remove(actual.InboxKey);
            // A persisted previous-writer spelling; external qualification also runs both real old producers.
            var old = (actual with { Id = CreationProtocol.Origin(kid, 1) + "#" + sign + "1.end" }).WithInboxScope(actual.InboxScope);
            var calls = f.Calls;
            await using var recovery = Open(s, f); await new DurableHost([recovery]).ActivateAsync(0);
            foreach (var suffix in new[] { "#minus01.end", "#minus.end", "#minus9223372036854775809.end", "#١1.end", "#minus1\0.end", "-wrong-origin#minus1.end" })
            {
                var bad = old with { Id = CreationProtocol.Origin(kid, 1) + suffix };
                s.Messages[bad.InboxKey] = new(bad, ++sequence, 0); await Pump(recovery);
                check(s.Instances[id.Id] == prior && f.Calls == calls && !s.Messages.ContainsKey(bad.InboxKey),
                    $"a malformed or wrong-origin issued old spelling cannot close the registry: {suffix}, terminal={terminal}");
            }
            var ordinary = old.AsOrdinary(); s.Messages[ordinary.InboxKey] = new(ordinary, ++sequence, 0); await Pump(recovery);
            check(s.Instances[id.Id] == prior && f.Calls == calls && !s.Messages.ContainsKey(ordinary.InboxKey),
                $"ordinary ingress cannot borrow a legacy terminal spelling, {sign}, terminal={terminal}");
            s.Messages[old.InboxKey] = new(old, ++sequence, 0); await Pump(recovery);
            check(s.Instances[id.Id].Revision == prior.Revision + 1 && f.Calls == calls + (terminal ? 0 : 1) && !s.Messages.ContainsKey(old.InboxKey),
                $"an issued prior negative-sign notice keeps its key and applies at its own parent, {sign}, terminal={terminal}");
        }

        foreach (var g in new[] { 0, 1 })
        {
            var s = new InMemoryStore(1, () => 100); var inspector = new Inspect(new WorkflowInstance([new Family()]));
            await using var w = new Worker("guard", 0, s, inspector, new() { Monotonic = () => 100 }); await new DurableHost([w]).ActivateAsync(0);
            await s.AddAsync(Input("open", InstanceRef.In(0, "guard"), Kinds.Start, g, Spec(g))); await Pump(w);
            check(inspector.Guard && inspector.StrictIssuer, $"application Add reserves every accepted old end spelling and the new issuer stays invariant, g{g}");
        }
    }

    private sealed class Family : Model<List<string>>
    {
        public override string Key => "recovery-family";
        public bool Defer; public int Calls; public readonly List<string> Received = [];
        protected override void Turn(ITurn t, List<string> state)
        {
            if (t.Instance.Definition == "parent") Calls++;
            foreach (var m in t.Inbox)
                if (m.Kind == Kinds.Start && t.Instance.Definition == "parent") { state.Add(t.Start(new(Key, "child", History: History.Minimal)).Id); state.Add(t.Start(new(Key, "child", History: History.Minimal)).Id); }
                else if (m.Kind == "finish" || Enum.TryParse<InstanceStatus>(m.Kind, out _)) t.SetStatus(m.Kind == "finish" ? InstanceStatus.Finished : Enum.Parse<InstanceStatus>(m.Kind));
                else if (m.Kind == WorkflowKinds.ChildFinished) { if (Defer) t.Hold(m); else Received.Add(m.Id); }
        }
    }

    private sealed class Inspect(WorkflowInstance inner) : IActorType
    {
        public bool Guard = true, StrictIssuer = true;
        public bool Withholds(InstanceRow r, ITurnServices s) => inner.Withholds(r, s);
        public bool SelectsAlone(string? m, InstanceRow? r, Delivery d) => inner.SelectsAlone(m, r, d);
        public bool RevisitsHeld(string? m, InstanceRow r, Delivery d) => inner.RevisitsHeld(m, r, d);
        public TurnOutcome? Command(TurnInput input)
        {
            var origin = CreationProtocol.Origin(input.Actor, input.Creation);
            foreach (var suffix in new[] { "#2.end", "#-1.end", "#−1.end", "#minus1.end", "#−9223372036854775808.end", "#02.end", "#minus01.end", "#minus.end", "#minus9223372036854775809.end", "#2.ordinary" })
            {
                var m = new Message(origin + suffix, new(input.Actor), "marker", Sender: input.Actor); var turn = new DurableTurn(input); var rejected = false;
                try { turn.Add(m); } catch (ArgumentException) { rejected = true; }
                var accepted = suffix is "#2.end" or "#-1.end" or "#−1.end" or "#minus1.end" or "#−9223372036854775808.end";
                Guard &= rejected == (input.Creation > 0 && accepted);
                rejected = false; try { turn.AddTerminalNotice(m); } catch (ArgumentException) { rejected = true; }
                StrictIssuer &= rejected == (m.Id != CreationProtocol.TerminalNoticeId(input.Actor, input.Creation, turn.Revision));
            }
            return inner.Command(input);
        }
        public ValueTask<TurnOutcome> TurnAsync(TurnInput i) => inner.TurnAsync(i);
        public TurnOutcome Park(InstanceRow? r, string a, IReadOnlyList<Delivery> d, StartSpec? s, string e) => inner.Park(r, a, d, s, e);
        public ValueTask<object?> ExecuteJobAsync(string m, IJob j, CancellationToken c) => inner.ExecuteJobAsync(m, j, c);
    }
}
