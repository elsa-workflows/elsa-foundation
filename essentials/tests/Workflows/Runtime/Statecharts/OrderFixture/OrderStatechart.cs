using System.Text.Json;
using Kernel.Native;
using Kernel.Statecharts;
using Kernel.Statecharts.Native;

namespace Elsa.Workflows.Runtime.Statecharts.OrderFixture;

public static class OrderStatechart
{
    public const string Definition = "order";
    private const string Node = "order-machine";

    public static Workflow Build(bool omitStockRelease = false)
    {
        var chart = new StructuralDefinition(
            [new("Order", NodeKind.Root, Initial: "AwaitingPayment"),
             new("AwaitingPayment", NodeKind.Atomic, "Order", Entry: "prepare"),
             new("Fulfilled", NodeKind.Final, "Order", Entry: "fulfill"),
             new("ReleasingShipment", NodeKind.Atomic, "Order", Entry: "reverse-shipment"),
             new("ReleasingStock", NodeKind.Atomic, "Order", Entry: omitStockRelease ? null : "release-stock"),
             new("Reversed", NodeKind.Final, "Order")],
            [new("approved", 0, "AwaitingPayment", "Fulfilled", VisitMode.Reenter, "approved"),
             new("declined", 1, "AwaitingPayment", "ReleasingShipment", VisitMode.Reenter, "declined"),
             new("shipment-released", 2, "ReleasingShipment", "ReleasingStock", VisitMode.Reenter, "shipment-released"),
             new("stock-released", 3, "ReleasingStock", "Reversed", VisitMode.Reenter, "stock-released")]);
        var machine = Wf.Node<StateMachine>(Node, ("StructuralDefinition", Binding.Literal(chart)))
            .With(Wf.Sequence("prepare", Wf.WriteLine("reserve-stock", Binding.Literal("reserve stock")),
                    Wf.WriteLine("book-shipment", Binding.Literal("book shipment"))),
                Wf.WriteLine("fulfill", Binding.Literal("fulfill order")),
                Wf.WriteLine("reverse-shipment", Binding.Literal("release shipment")),
                Wf.WriteLine("release-stock", Binding.Literal("release stock")));
        return Wf.Workflow(Definition, machine);
    }

    public static (string Frame, string Visit) SettledConfiguration(string checkpoint, string expected)
    {
        var native = JsonSerializer.Deserialize<NativeState>(checkpoint)!;
        var frame = native.Frames.Values.Single(f => f.Node == Node);
        var chart = frame.State?.Deserialize<MachineState>();
        if (native.Mode != Modes.Balanced || native.Agenda.Count != 0 ||
            chart is not { Active: [var state], Pending: null, Visits: [var visit] } ||
            state != expected || visit.State != expected ||
            !visit.Occurrence.StartsWith(frame.Id + "/state/", StringComparison.Ordinal))
            throw new InvalidOperationException($"Expected the persisted order machine to be settled in {expected}.");

        return (frame.Id, visit.Occurrence);
    }
}
