using Kernel.Durable;
using Kernel.Statecharts;
using Kernel.Statecharts.Native;

// Literal reader fixtures, not proof from a historical writer/binary or a product migration contract.
internal static class PhaseReaders
{
    internal static string[] Names => ["phase-reader:pending-prefix", "phase-reader:completed-prefix", "phase-reader:future-version",
        "phase-reader:invalid-running-index", "phase-reader:duplicate-running-step", "phase-reader:invalid-legacy-cursor",
        "phase-reader:mixed-legacy-progress", "phase-reader:mixed-current-cursor"];
    internal static void Run()
    {
        var chart = new Chart(new(new("Working", [new("Working", [new("Leaf", [new("Leaf", Exit: "finished")])], Exit: "held"), new("Done", Final: true)]),
            [new("Working", "move", "Done", "action")]));
        var legacy = Json.Read<MachineState>("""
            {"Active":["Working","Leaf"],"Pending":{"EventId":"event","Next":["Done"],"Steps":[
              {"Phase":"exit","State":"Leaf","Activity":"finished"},
              {"Phase":"exit","State":"Working","Activity":"held"},
              {"Phase":"transition","State":"Working","Activity":"action"},
              {"Phase":"entry","State":"Done","Activity":null}]},"Cursor":2,"Child":"old-child"}
            """);
        void Check(bool ok, string detail) { if (!ok) throw new InvalidOperationException("P7: " + detail); }
        var pending = StateMachine.Restore(legacy, chart);
        Check(pending.Version == 2 && pending.Cursor == 0 && pending.Child is null &&
            pending.Progress!.Done.SequenceEqual([true, false, false, false]) && pending.Progress.Running.Count == 1 &&
            pending.Progress.Running.GetValueOrDefault("old-child", -1) == 1 && legacy.Cursor == 2 && legacy.Version == 1,
            "legacy prefix or exact running child changed");
        Console.WriteLine("PASS " + Names[0]);
        var completed = StateMachine.Restore(legacy with { Child = null }, chart);
        Check(completed.Progress!.Done.SequenceEqual([true, true, false, false]) && completed.Progress.Running.Count == 0,
            "completed prefix became runnable again");
        Console.WriteLine("PASS " + Names[1]);
        void Refuse(MachineState invalid, string name, bool future = false)
        {
            try { StateMachine.Restore(invalid, chart); }
            catch (NotSupportedException) when (future) { Console.WriteLine("PASS " + name); return; }
            catch (InvalidOperationException) when (!future) { Console.WriteLine("PASS " + name); return; }
            throw new InvalidOperationException("P7: accepted invalid checkpoint " + name);
        }
        Refuse(legacy with { Version = 3 }, Names[2], true);
        Refuse(pending with { Progress = pending.Progress! with { Running = new() { ["bad"] = 4 } } }, Names[3]);
        Refuse(pending with { Progress = pending.Progress! with { Running = new() { ["a"] = 1, ["b"] = 1 } } }, Names[4]);
        Refuse(legacy with { Cursor = 0 }, Names[5]);
        Refuse(legacy with { Progress = pending.Progress }, Names[6]);
        Refuse(pending with { Cursor = 2, Child = "old-child" }, Names[7]);
        Console.WriteLine("8/8 phase reader fixtures passed; distinct from the48 normal workflow schedules.");
    }
}
