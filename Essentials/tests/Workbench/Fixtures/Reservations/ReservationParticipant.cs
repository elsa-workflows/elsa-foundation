using CShells.Features;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Reconciliation;
using Elsa.Activities.Design.Core.Reconciliation.Models;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workbench.Tests.Reservations;

[ShellFeature("ReservationWitness", DependsOn = new object[] { "ActivitiesCompensation" })]
public sealed class ReservationWitnessFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services) =>
        services.AddScoped<IActivityReconciliationSource, ReservationParticipantCatalog>();
}

// Feed packages contribute their catalog explicitly; the host's CLR source scans only its own binary folder.
public sealed class ReservationParticipantCatalog : IActivityReconciliationSource
{
    public string SourceId => "ReservationWitness";
    public string SourceKind => "OwnedProcessTest";

    public ValueTask<IEnumerable<ActivityVersionReconciliationModel>> Read(CancellationToken cancellationToken) =>
        ValueTask.FromResult<IEnumerable<ActivityVersionReconciliationModel>>([
            new(null, "1.0.0", typeof(ReservationParticipant).FullName!, "Reservation participant", "Tests", null,
                "elsa.clr-activity", "1", WellKnownRuntimeActivityConsumers.ClrActivity,
                RuntimeActivityDescriptor.InitialSchemaVersion,
                new ClrActivityDescriptor(TypeAliasConvention.CanonicalAlias(typeof(ReservationParticipant))),
                [Input(nameof(ReservationParticipant.DirectoryPath), "String"), Input(nameof(ReservationParticipant.Key), "String"),
                    Input(nameof(ReservationParticipant.Reject), "Boolean"), Input(nameof(ReservationParticipant.HoldAfterEffect), "Boolean"),
                    Input(nameof(ReservationParticipant.Undoes), "String", isRequired: false)],
                [new OutputDefinition(ReservationResult.OutputKey, nameof(ReservationResult.Reservation), new TypeReference("String"), null,
                    nameof(ReservationResult.Reservation), null, IsNullable: false, IsRequired: true)], [])
        ]);

    private static InputDefinition Input(string name, string alias, bool isRequired = true) =>
        new(name, name, new TypeReference(alias), null, name, null, IsNullable: !isRequired, IsRequired: isRequired);
}

/// <summary>
/// An external participant whose state survives termination of its host. It applies each effect once per invocation
/// id, as an idempotent participant does, and journals every call so a test can tell a repeated call from a repeated
/// effect. With <see cref="HoldAfterEffect"/> it applies the effect and then waits for a release file, so the test can
/// kill the host after the effect and before the activity's completion commits. Its result is a reservation id; a
/// handler given that id through <see cref="Undoes"/> journals it, so a test can tell which reservation it undid.
/// </summary>
[ActivityOutcome("Done")]
public sealed class ReservationParticipant : Activity<ReservationResult>
{
    public const string RejectionCode = "order.rejected";

    [ActivityInput] public string DirectoryPath { get; set; } = null!;
    [ActivityInput] public string Key { get; set; } = null!;
    [ActivityInput] public bool Reject { get; set; }
    [ActivityInput] public bool HoldAfterEffect { get; set; }
    [ActivityInput] public string? Undoes { get; set; }

    protected override async ValueTask<ActivityTransition<ReservationResult>> ExecuteAsync(ActivityExecutionContext context)
    {
        Directory.CreateDirectory(DirectoryPath);
        await File.AppendAllTextAsync(Path.Combine(DirectoryPath, Key + ".calls"),
            $"{Environment.ProcessId}|{context.InvocationId}|{context.AttemptId}{Environment.NewLine}", context.CancellationToken);

        if (Undoes is not null)
            await File.AppendAllTextAsync(Path.Combine(DirectoryPath, Key + ".undoes"), Undoes + Environment.NewLine, context.CancellationToken);

        if (Reject)
            return ActivityTransition.Fault<ReservationResult>(new ActivityFault(RejectionCode, "The order was rejected."));

        var marker = Path.Combine(DirectoryPath, $"{Key}.{context.InvocationId}.effect");
        if (!File.Exists(marker))
        {
            await File.WriteAllTextAsync(marker, context.InvocationId, context.CancellationToken);
            await File.AppendAllTextAsync(Path.Combine(DirectoryPath, "effects.log"), Key + Environment.NewLine, context.CancellationToken);
        }

        while (HoldAfterEffect && !File.Exists(Path.Combine(DirectoryPath, Key + ".release")))
            await Task.Delay(25, context.CancellationToken);

        return ActivityTransition.Complete(new ReservationResult($"{Key}:{context.InvocationId}"));
    }
}

/// <summary>The reservation a participant made: its key and the invocation that made it.</summary>
public sealed record ReservationResult([property: Output(Key = ReservationResult.OutputKey, Path = "reservation")] string Reservation)
{
    public const string OutputKey = "Reservation";
}
