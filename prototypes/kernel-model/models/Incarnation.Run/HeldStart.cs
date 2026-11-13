using Kernel.Durable;
using Kernel.Sim;
using Kernel.Workflows;

internal static partial class BoundaryCases
{
    internal static async Task HeldStarts()
    {
        foreach (var recreate in new[] { false, true }) foreach (var refuse in new[] { true, false })
        {
            await using var h = new Harness(new HoldingStart()); await h.Activate();
            long generation = 1;
            if (recreate)
            {
                await h.Send(Kinds.Start, start: new("holding-start", "probe", Creation: new(1, 0, "setup")), creation: 1);
                await h.Send(Kinds.Terminate, creation: 1); await h.Send(Kinds.Delete, creation: 1);
                Require(h.Row.Retired, "C12 setup did not normally retire the first creation"); generation++;
            }
            var refusals = 0;
            if (refuse) h.Store.Refuses = row =>
            {
                if (row.Creation != generation || row.Status != InstanceStatus.Running) return false;
                refusals++; return true;
            };
            var spec = new StartSpec("holding-start", "probe", "hold", Creation: new(generation, generation - 1, "held"));
            try { await h.Send(Kinds.Start, start: spec, creation: generation); }
            catch (Exception error) when (error is IndexOutOfRangeException or ArgumentOutOfRangeException)
            { throw new InvalidOperationException("C12: held initial checkpoint refusal threw before Park", error); }
            if (refuse)
            {
                if (refusals == 0 || h.Row is not { Status: InstanceStatus.Faulted, Retired: false, State: "null", PendingStart: null } || h.Row.Creation != generation ||
                    h.Store.Messages.Values.Any(m => m.Message.Start?.Creation?.Generation == generation && !m.Done))
                    throw new InvalidOperationException("C12: held initial checkpoint was not parked and consumed");
            }
            else
            {
                if (h.Row.Status != InstanceStatus.Running || h.Row.PendingStart is null || !h.Store.Messages.ContainsKey(h.Row.PendingStart) || Json.Read<HoldingState>(h.Row.State).Starts != 0)
                    throw new InvalidOperationException("C12: initial work was not held");
                await h.Reload(); var before = h.Row;
                await h.Send(Kinds.Start, start: spec, creation: generation);
                if (h.Last is not CreationResult { Result: "ExistingCreation" } || h.Row != before)
                    throw new InvalidOperationException("C12: duplicate start activated held authored work");
                await h.Send("release", creation: generation);
                if (h.Row.Status != InstanceStatus.Finished || h.Row.PendingStart is not null || Json.Read<HoldingState>(h.Row.State).Starts != 1)
                    throw new InvalidOperationException("C12: committed held start did not continue once after reload");
            }
            Console.WriteLine($"PASS creation:held-start,recreate={recreate},refuse={refuse}");
        }
    }
}

public sealed class HoldingState { public bool Hold { get; set; } public bool Released { get; set; } public int Starts { get; set; } }
public sealed class HoldingStart : Model<HoldingState>
{
    public override string Key => "holding-start";
    public override object CreateState(StartSpec start) => new HoldingState { Hold = start.Input == "hold" };
    protected override void Turn(ITurn turn, HoldingState state)
    {
        state.Released |= turn.Inbox.Any(m => m.Kind == "release");
        foreach (var message in turn.Inbox.Where(m => m.Kind == Kinds.Start))
            if (state.Hold && !state.Released) turn.Hold(message);
            else if (state.Hold) { state.Starts++; turn.SetStatus(InstanceStatus.Finished); }
    }
}
